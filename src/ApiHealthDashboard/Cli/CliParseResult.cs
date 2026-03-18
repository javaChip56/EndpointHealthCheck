namespace ApiHealthDashboard.Cli;

public sealed class CliParseResult
{
    public bool IsCliMode { get; init; }

    public bool IsHelpRequested { get; init; }

    public bool IsValid { get; init; }

    public string? ErrorMessage { get; init; }

    public CliOptions? Options { get; init; }

    public static CliParseResult NotCli()
    {
        return new CliParseResult
        {
            IsValid = true
        };
    }

    public static CliParseResult Help()
    {
        return new CliParseResult
        {
            IsCliMode = true,
            IsHelpRequested = true,
            IsValid = true
        };
    }

    public static CliParseResult Success(CliOptions options)
    {
        return new CliParseResult
        {
            IsCliMode = true,
            IsValid = true,
            Options = options
        };
    }

    public static CliParseResult Invalid(string errorMessage)
    {
        return new CliParseResult
        {
            IsCliMode = true,
            ErrorMessage = errorMessage
        };
    }
}
