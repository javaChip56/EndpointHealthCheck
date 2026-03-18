using System.Text;

namespace ApiHealthDashboard.Cli;

public sealed class CliOptions
{
    public bool IsCliMode { get; set; }

    public bool RunAll { get; set; }

    public string? DashboardConfigPathOverride { get; set; }

    public List<string> EndpointFiles { get; set; } = new();

    public string? OutputFilePath { get; set; }

    public CliFileOutputFormat? OutputFileFormat { get; set; }

    public static CliParseResult Parse(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length == 0)
        {
            return CliParseResult.NotCli();
        }

        var isCliMode = args.Any(static arg =>
            string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(arg, "cli", StringComparison.OrdinalIgnoreCase));

        if (!isCliMode)
        {
            return CliParseResult.NotCli();
        }

        var options = new CliOptions
        {
            IsCliMode = true
        };

        var endpointFiles = new List<string>();
        string? dashboardConfigPathOverride = null;
        string? outputFilePath = null;
        CliFileOutputFormat? outputFileFormat = null;
        var runAll = false;

        for (var index = 0; index < args.Length; index++)
        {
            var arg = args[index];

            if (string.Equals(arg, "--cli", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "cli", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (string.Equals(arg, "--help", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "-h", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(arg, "/?", StringComparison.OrdinalIgnoreCase))
            {
                return CliParseResult.Help();
            }

            if (string.Equals(arg, "--all", StringComparison.OrdinalIgnoreCase))
            {
                runAll = true;
                continue;
            }

            if (string.Equals(arg, "--endpoint-file", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref index, out var endpointFile))
                {
                    return CliParseResult.Invalid("Missing value for --endpoint-file.");
                }

                endpointFiles.Add(endpointFile);
                continue;
            }

            if (string.Equals(arg, "--dashboard-config", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref index, out var dashboardConfigPath))
                {
                    return CliParseResult.Invalid("Missing value for --dashboard-config.");
                }

                dashboardConfigPathOverride = dashboardConfigPath;
                continue;
            }

            if (string.Equals(arg, "--output-file", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref index, out var outputPath))
                {
                    return CliParseResult.Invalid("Missing value for --output-file.");
                }

                outputFilePath = outputPath;
                continue;
            }

            if (string.Equals(arg, "--output-format", StringComparison.OrdinalIgnoreCase))
            {
                if (!TryReadValue(args, ref index, out var formatText))
                {
                    return CliParseResult.Invalid("Missing value for --output-format.");
                }

                if (!TryParseOutputFormat(formatText, out var parsedFormat))
                {
                    return CliParseResult.Invalid("Invalid value for --output-format. Allowed values are 'json' and 'xml'.");
                }

                outputFileFormat = parsedFormat;
                continue;
            }

            return CliParseResult.Invalid($"Unknown CLI argument '{arg}'.");
        }

        if (runAll && endpointFiles.Count > 0)
        {
            return CliParseResult.Invalid("Use either --all or one or more --endpoint-file values, but not both.");
        }

        if (!runAll && endpointFiles.Count == 0)
        {
            return CliParseResult.Invalid("CLI mode requires --all or at least one --endpoint-file value.");
        }

        if (outputFileFormat is not null && string.IsNullOrWhiteSpace(outputFilePath))
        {
            return CliParseResult.Invalid("--output-format requires --output-file.");
        }

        options.RunAll = runAll;
        options.DashboardConfigPathOverride = dashboardConfigPathOverride;
        options.EndpointFiles.AddRange(endpointFiles);
        options.OutputFilePath = outputFilePath;
        options.OutputFileFormat = outputFileFormat;

        return CliParseResult.Success(options);
    }

    public CliFileOutputFormat ResolveOutputFileFormat()
    {
        if (OutputFileFormat is not null)
        {
            return OutputFileFormat.Value;
        }

        if (!string.IsNullOrWhiteSpace(OutputFilePath))
        {
            var extension = Path.GetExtension(OutputFilePath);
            if (string.Equals(extension, ".xml", StringComparison.OrdinalIgnoreCase))
            {
                return CliFileOutputFormat.Xml;
            }
        }

        return CliFileOutputFormat.Json;
    }

    public static string GetHelpText()
    {
        var builder = new StringBuilder();
        builder.AppendLine("ApiHealthDashboard CLI");
        builder.AppendLine();
        builder.AppendLine("Usage:");
        builder.AppendLine("  dotnet run --project .\\src\\ApiHealthDashboard\\ApiHealthDashboard.csproj -- --cli --all");
        builder.AppendLine("  dotnet run --project .\\src\\ApiHealthDashboard\\ApiHealthDashboard.csproj -- --cli --endpoint-file .\\src\\ApiHealthDashboard\\endpoints\\orders-api.yaml");
        builder.AppendLine();
        builder.AppendLine("Options:");
        builder.AppendLine("  --cli                       Run one-shot CLI execution mode.");
        builder.AppendLine("  --all                       Execute all endpoints from the dashboard suite.");
        builder.AppendLine("  --endpoint-file <path>      Execute only the specified endpoint YAML file. Repeat to include more than one file.");
        builder.AppendLine("  --dashboard-config <path>   Override the dashboard YAML path used for defaults and endpoint resolution.");
        builder.AppendLine("  --output-file <path>        Write the report to a file.");
        builder.AppendLine("  --output-format <json|xml>  File format for --output-file. Defaults to the output file extension or JSON.");
        builder.AppendLine("  --help, -h                  Show this help text.");
        builder.AppendLine();
        builder.AppendLine("Behavior:");
        builder.AppendLine("  - CLI output written to stdout is always JSON.");
        builder.AppendLine("  - File output is optional and can be JSON or XML.");
        return builder.ToString().TrimEnd();
    }

    private static bool TryReadValue(string[] args, ref int index, out string value)
    {
        if (index + 1 >= args.Length)
        {
            value = string.Empty;
            return false;
        }

        value = args[++index];
        return !string.IsNullOrWhiteSpace(value);
    }

    private static bool TryParseOutputFormat(string value, out CliFileOutputFormat format)
    {
        if (string.Equals(value, "json", StringComparison.OrdinalIgnoreCase))
        {
            format = CliFileOutputFormat.Json;
            return true;
        }

        if (string.Equals(value, "xml", StringComparison.OrdinalIgnoreCase))
        {
            format = CliFileOutputFormat.Xml;
            return true;
        }

        format = default;
        return false;
    }
}
