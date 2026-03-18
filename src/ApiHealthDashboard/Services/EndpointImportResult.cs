using System.Net;
using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Services;

public sealed class EndpointImportResult
{
    public required EndpointConfig SuggestedEndpoint { get; init; }

    public string? GeneratedYaml { get; init; }

    public required PollResult ProbeResult { get; init; }

    public required string ProbeStatusText { get; init; }

    public required string MatchSummary { get; init; }

    public EndpointConfig? ExistingEndpoint { get; init; }

    public string? ExistingYaml { get; init; }

    public IReadOnlyList<EndpointImportDiffLine> DiffLines { get; init; } = [];

    public IReadOnlyList<EndpointImportCheckSummary> DiscoveredChecks { get; init; } = [];

    public IReadOnlyList<string> TopLevelCheckNames { get; init; } = [];

    public string ResponsePreview { get; init; } = string.Empty;

    public bool ResponsePreviewWasTruncated { get; init; }

    public string? ParserStatus { get; init; }

    public string? ParserError { get; init; }

    public bool HasExistingMatch => ExistingEndpoint is not null;

    public bool HasDiff => DiffLines.Count > 0;

    public bool HasGeneratedYamlPreview => !string.IsNullOrWhiteSpace(GeneratedYaml);

    public bool HasResponsePreview => !string.IsNullOrWhiteSpace(ResponsePreview);

    public bool IsEndpointNotFound => ProbeResult.Kind == PollResultKind.HttpError &&
                                      ProbeResult.StatusCode == HttpStatusCode.NotFound;

    public string ProbeHttpStatusText => ProbeResult.StatusCode is HttpStatusCode statusCode
        ? $"{(int)statusCode} {statusCode}"
        : "None";
}

public sealed class EndpointImportDiffLine
{
    public required string Prefix { get; init; }

    public required string Text { get; init; }

    public required string CssClass { get; init; }
}

public sealed class EndpointImportCheckSummary
{
    public required string Path { get; init; }

    public required string Status { get; init; }

    public required int Depth { get; init; }
}
