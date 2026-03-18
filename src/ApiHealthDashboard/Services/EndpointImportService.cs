using System.Globalization;
using System.Text;
using System.Text.Json;
using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Domain;
using ApiHealthDashboard.Parsing;

namespace ApiHealthDashboard.Services;

public sealed class EndpointImportService : IEndpointImportService
{
    private const int ResponsePreviewLimit = 12000;
    private static readonly string[] GenericPathSegments = ["health", "healthz", "status", "ready", "live"];

    private readonly DashboardConfig _dashboardConfig;
    private readonly IHealthResponseParser _healthResponseParser;
    private readonly ILogger<EndpointImportService> _logger;
    private readonly IEndpointPoller _endpointPoller;

    public EndpointImportService(
        DashboardConfig dashboardConfig,
        IEndpointPoller endpointPoller,
        IHealthResponseParser healthResponseParser,
        ILogger<EndpointImportService> logger)
    {
        _dashboardConfig = dashboardConfig;
        _endpointPoller = endpointPoller;
        _healthResponseParser = healthResponseParser;
        _logger = logger;
    }

    public async Task<EndpointImportResult> ImportAsync(EndpointImportRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        var validationErrors = ValidateRequest(request);
        if (validationErrors.Count > 0)
        {
            throw new EndpointImportException(validationErrors);
        }

        var headers = ParseHeaders(request.HeadersText);
        var suggestedId = string.IsNullOrWhiteSpace(request.Id)
            ? SuggestEndpointId(request.Url)
            : request.Id.Trim();
        var suggestedName = string.IsNullOrWhiteSpace(request.Name)
            ? SuggestEndpointName(suggestedId)
            : request.Name.Trim();

        var probeEndpoint = new EndpointConfig
        {
            Id = suggestedId,
            Name = suggestedName,
            Url = request.Url.Trim(),
            Enabled = request.Enabled,
            FrequencySeconds = request.FrequencySeconds,
            TimeoutSeconds = request.TimeoutSeconds,
            Headers = headers
        };

        _logger.LogInformation(
            "Starting endpoint import probe for suggested endpoint {EndpointId} against {Url}.",
            probeEndpoint.Id,
            probeEndpoint.Url);

        var pollResult = await _endpointPoller.PollAsync(probeEndpoint, cancellationToken);
        var snapshot = string.IsNullOrWhiteSpace(pollResult.ResponseBody)
            ? null
            : _healthResponseParser.Parse(probeEndpoint, pollResult.ResponseBody, pollResult.DurationMs);
        var topLevelCheckNames = snapshot?.Nodes
            .Select(static node => node.Name)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray() ?? [];

        var suggestedEndpoint = new EndpointConfig
        {
            Id = probeEndpoint.Id,
            Name = probeEndpoint.Name,
            Url = probeEndpoint.Url,
            Enabled = probeEndpoint.Enabled,
            FrequencySeconds = probeEndpoint.FrequencySeconds,
            TimeoutSeconds = probeEndpoint.TimeoutSeconds,
            Headers = new Dictionary<string, string>(probeEndpoint.Headers, StringComparer.OrdinalIgnoreCase),
            IncludeChecks = request.IncludeDiscoveredChecks ? [.. topLevelCheckNames] : [],
            ExcludeChecks = []
        };

        var shouldGenerateYamlPreview = !(pollResult.Kind == PollResultKind.HttpError &&
                                          pollResult.StatusCode == System.Net.HttpStatusCode.NotFound);
        var generatedYaml = shouldGenerateYamlPreview ? RenderEndpointYaml(suggestedEndpoint) : null;
        var existingEndpoint = FindExistingEndpoint(suggestedEndpoint);
        var existingYaml = existingEndpoint is null ? null : RenderEndpointYaml(existingEndpoint);
        var diffLines = existingYaml is null || string.IsNullOrWhiteSpace(generatedYaml)
            ? []
            : BuildDiff(existingYaml, generatedYaml);
        var responsePreview = BuildResponsePreview(pollResult.ResponseBody, out var responsePreviewWasTruncated);
        var discoveredChecks = snapshot is null ? [] : FlattenChecks(snapshot.Nodes);
        var parserError = TryGetParserError(snapshot);

        _logger.LogInformation(
            "Completed endpoint import probe for {EndpointId} with poll result {ResultKind} and existing match {HasExistingMatch}.",
            suggestedEndpoint.Id,
            pollResult.Kind,
            existingEndpoint is not null);

        return new EndpointImportResult
        {
            SuggestedEndpoint = suggestedEndpoint,
            GeneratedYaml = generatedYaml,
            ProbeResult = pollResult,
            ProbeStatusText = DescribeProbeStatus(pollResult),
            MatchSummary = DescribeExistingMatch(existingEndpoint, suggestedEndpoint),
            ExistingEndpoint = existingEndpoint,
            ExistingYaml = existingYaml,
            DiffLines = diffLines,
            DiscoveredChecks = discoveredChecks,
            TopLevelCheckNames = topLevelCheckNames,
            ResponsePreview = responsePreview,
            ResponsePreviewWasTruncated = responsePreviewWasTruncated,
            ParserStatus = snapshot?.OverallStatus,
            ParserError = parserError
        };
    }

    private static List<string> ValidateRequest(EndpointImportRequest request)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(request.Url))
        {
            errors.Add("Endpoint URL is required.");
        }
        else if (!Uri.TryCreate(request.Url.Trim(), UriKind.Absolute, out var uri) ||
                 (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            errors.Add("Endpoint URL must be an absolute HTTP or HTTPS address.");
        }

        if (request.FrequencySeconds <= 0)
        {
            errors.Add("Frequency seconds must be greater than zero.");
        }

        if (request.TimeoutSeconds is <= 0)
        {
            errors.Add("Timeout seconds must be greater than zero when specified.");
        }

        return errors;
    }

    private static Dictionary<string, string> ParseHeaders(string headersText)
    {
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(headersText))
        {
            return headers;
        }

        var lines = headersText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n', StringSplitOptions.TrimEntries);

        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index];
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            var separatorIndex = line.IndexOf(':');
            if (separatorIndex <= 0)
            {
                throw new EndpointImportException([$"Header line {index + 1} must use the format 'Name: value'."]);
            }

            var headerName = line[..separatorIndex].Trim();
            var headerValue = line[(separatorIndex + 1)..].Trim();

            if (string.IsNullOrWhiteSpace(headerName))
            {
                throw new EndpointImportException([$"Header line {index + 1} contains an empty header name."]);
            }

            if (headers.ContainsKey(headerName))
            {
                throw new EndpointImportException([$"Header '{headerName}' is defined more than once."]);
            }

            headers[headerName] = headerValue;
        }

        return headers;
    }

    private EndpointConfig? FindExistingEndpoint(EndpointConfig suggestedEndpoint)
    {
        return _dashboardConfig.Endpoints.FirstOrDefault(endpoint =>
                   string.Equals(endpoint.Id, suggestedEndpoint.Id, StringComparison.OrdinalIgnoreCase))
               ?? _dashboardConfig.Endpoints.FirstOrDefault(endpoint =>
                   string.Equals(endpoint.Url, suggestedEndpoint.Url, StringComparison.OrdinalIgnoreCase));
    }

    private static string DescribeExistingMatch(EndpointConfig? existingEndpoint, EndpointConfig suggestedEndpoint)
    {
        if (existingEndpoint is null)
        {
            return "No existing endpoint matched the suggested id or URL.";
        }

        if (string.Equals(existingEndpoint.Id, suggestedEndpoint.Id, StringComparison.OrdinalIgnoreCase))
        {
            return $"Matched existing endpoint '{existingEndpoint.Id}' by id.";
        }

        return $"Matched existing endpoint '{existingEndpoint.Id}' by URL.";
    }

    private static string DescribeProbeStatus(PollResult pollResult)
    {
        return pollResult.Kind switch
        {
            PollResultKind.Success => "Probe completed successfully.",
            PollResultKind.HttpError => pollResult.ErrorMessage ?? "Probe completed with an HTTP error response.",
            PollResultKind.EmptyResponse => pollResult.ErrorMessage ?? "Probe completed but returned an empty body.",
            PollResultKind.Timeout => pollResult.ErrorMessage ?? "Probe timed out.",
            PollResultKind.NetworkError => pollResult.ErrorMessage ?? "Probe failed with a network error.",
            _ => pollResult.ErrorMessage ?? "Probe failed with an unexpected error."
        };
    }

    private static string BuildResponsePreview(string? responseBody, out bool truncated)
    {
        if (string.IsNullOrEmpty(responseBody))
        {
            truncated = false;
            return string.Empty;
        }

        var formattedResponse = FormatResponsePreview(responseBody);

        if (formattedResponse.Length <= ResponsePreviewLimit)
        {
            truncated = false;
            return formattedResponse;
        }

        truncated = true;
        return formattedResponse[..ResponsePreviewLimit];
    }

    private static string FormatResponsePreview(string responseBody)
    {
        if (string.IsNullOrWhiteSpace(responseBody))
        {
            return responseBody;
        }

        try
        {
            using var document = JsonDocument.Parse(responseBody);
            return JsonSerializer.Serialize(
                document.RootElement,
                new JsonSerializerOptions
                {
                    WriteIndented = true
                });
        }
        catch (JsonException)
        {
            return responseBody;
        }
    }

    private static string? TryGetParserError(HealthSnapshot? snapshot)
    {
        if (snapshot?.Metadata.TryGetValue("parserError", out var parserError) == true)
        {
            return parserError?.ToString();
        }

        return null;
    }

    private static IReadOnlyList<EndpointImportCheckSummary> FlattenChecks(IReadOnlyList<HealthNode> nodes)
    {
        var results = new List<EndpointImportCheckSummary>();

        for (var index = 0; index < nodes.Count; index++)
        {
            FlattenChecks(nodes[index], nodes[index].Name, 0, results);
        }

        return results;
    }

    private static void FlattenChecks(
        HealthNode node,
        string path,
        int depth,
        ICollection<EndpointImportCheckSummary> results)
    {
        results.Add(new EndpointImportCheckSummary
        {
            Path = path,
            Status = node.Status,
            Depth = depth
        });

        foreach (var child in node.Children)
        {
            FlattenChecks(child, $"{path} / {child.Name}", depth + 1, results);
        }
    }

    private static string SuggestEndpointId(string url)
    {
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri))
        {
            return "imported-endpoint";
        }

        var tokens = new List<string>();
        var hostTokens = uri.Host.Split('.', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (hostTokens.Length > 0)
        {
            tokens.Add(Slugify(hostTokens[0]));
        }

        var pathTokens = uri.AbsolutePath
            .Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Slugify)
            .Where(static token => !string.IsNullOrWhiteSpace(token) && !GenericPathSegments.Contains(token, StringComparer.OrdinalIgnoreCase))
            .ToList();

        tokens.AddRange(pathTokens);

        var combined = string.Join('-', tokens.Where(static token => !string.IsNullOrWhiteSpace(token)));
        if (string.IsNullOrWhiteSpace(combined))
        {
            combined = "imported-endpoint";
        }

        if (!combined.Contains("api", StringComparison.OrdinalIgnoreCase))
        {
            combined = $"{combined}-api";
        }

        return combined;
    }

    private static string SuggestEndpointName(string id)
    {
        var words = id
            .Split('-', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(static word => string.Equals(word, "api", StringComparison.OrdinalIgnoreCase)
                ? "API"
                : CultureInfo.InvariantCulture.TextInfo.ToTitleCase(word))
            .ToArray();

        return words.Length == 0 ? "Imported Endpoint" : string.Join(' ', words);
    }

    private static string Slugify(string value)
    {
        var builder = new StringBuilder(value.Length);
        var lastWasDash = false;

        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                lastWasDash = false;
                continue;
            }

            if (lastWasDash)
            {
                continue;
            }

            builder.Append('-');
            lastWasDash = true;
        }

        return builder.ToString().Trim('-');
    }

    private static string RenderEndpointYaml(EndpointConfig endpoint)
    {
        var lines = new List<string>
        {
            $"id: {Quote(endpoint.Id)}",
            $"name: {Quote(endpoint.Name)}",
            $"url: {Quote(endpoint.Url)}",
            $"enabled: {endpoint.Enabled.ToString().ToLowerInvariant()}",
            $"frequencySeconds: {endpoint.FrequencySeconds}"
        };

        if (endpoint.TimeoutSeconds is int timeoutSeconds)
        {
            lines.Add($"timeoutSeconds: {timeoutSeconds}");
        }

        if (endpoint.Headers.Count > 0)
        {
            lines.Add("headers:");

            foreach (var header in endpoint.Headers.OrderBy(static item => item.Key, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  {header.Key}: {Quote(header.Value)}");
            }
        }

        if (endpoint.IncludeChecks.Count > 0)
        {
            lines.Add("includeChecks:");

            foreach (var check in endpoint.IncludeChecks.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  - {Quote(check)}");
            }
        }

        if (endpoint.ExcludeChecks.Count > 0)
        {
            lines.Add("excludeChecks:");

            foreach (var check in endpoint.ExcludeChecks.OrderBy(static item => item, StringComparer.OrdinalIgnoreCase))
            {
                lines.Add($"  - {Quote(check)}");
            }
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string Quote(string value)
    {
        var normalized = value.Replace("'", "''", StringComparison.Ordinal);
        return $"'{normalized}'";
    }

    private static IReadOnlyList<EndpointImportDiffLine> BuildDiff(string existingYaml, string generatedYaml)
    {
        var existingLines = SplitLines(existingYaml);
        var generatedLines = SplitLines(generatedYaml);
        var lcs = BuildLongestCommonSubsequence(existingLines, generatedLines);
        var results = new List<EndpointImportDiffLine>();
        var left = 0;
        var right = 0;

        while (left < existingLines.Count || right < generatedLines.Count)
        {
            if (left < existingLines.Count && right < generatedLines.Count &&
                string.Equals(existingLines[left], generatedLines[right], StringComparison.Ordinal))
            {
                results.Add(CreateDiffLine(" ", existingLines[left], "diff-context"));
                left++;
                right++;
                continue;
            }

            if (right < generatedLines.Count &&
                (left == existingLines.Count || lcs[left, right + 1] >= lcs[left + 1, right]))
            {
                results.Add(CreateDiffLine("+", generatedLines[right], "diff-added"));
                right++;
                continue;
            }

            if (left < existingLines.Count)
            {
                results.Add(CreateDiffLine("-", existingLines[left], "diff-removed"));
                left++;
            }
        }

        return results;
    }

    private static EndpointImportDiffLine CreateDiffLine(string prefix, string text, string cssClass)
    {
        return new EndpointImportDiffLine
        {
            Prefix = prefix,
            Text = text,
            CssClass = cssClass
        };
    }

    private static List<string> SplitLines(string value)
    {
        return value
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Split('\n')
            .ToList();
    }

    private static int[,] BuildLongestCommonSubsequence(IReadOnlyList<string> left, IReadOnlyList<string> right)
    {
        var matrix = new int[left.Count + 1, right.Count + 1];

        for (var leftIndex = left.Count - 1; leftIndex >= 0; leftIndex--)
        {
            for (var rightIndex = right.Count - 1; rightIndex >= 0; rightIndex--)
            {
                matrix[leftIndex, rightIndex] = string.Equals(left[leftIndex], right[rightIndex], StringComparison.Ordinal)
                    ? matrix[leftIndex + 1, rightIndex + 1] + 1
                    : Math.Max(matrix[leftIndex + 1, rightIndex], matrix[leftIndex, rightIndex + 1]);
            }
        }

        return matrix;
    }
}
