using ApiHealthDashboard.Cli;

namespace ApiHealthDashboard.Tests.Cli;

public sealed class CliOptionsTests
{
    [Fact]
    public void Parse_WithAllMode_ReturnsValidOptions()
    {
        var result = CliOptions.Parse(["--cli", "--all"]);

        Assert.True(result.IsCliMode);
        Assert.True(result.IsValid);
        Assert.NotNull(result.Options);
        Assert.True(result.Options.RunAll);
        Assert.Empty(result.Options.EndpointFiles);
    }

    [Fact]
    public void Parse_WithEndpointFiles_ReturnsValidOptions()
    {
        var result = CliOptions.Parse(
        [
            "--cli",
            "--endpoint-file", "endpoints/orders-api.yaml",
            "--endpoint-file", "endpoints/billing-api.yaml",
            "--output-file", "artifacts/report.xml"
        ]);

        Assert.True(result.IsValid);
        Assert.NotNull(result.Options);
        Assert.False(result.Options.RunAll);
        Assert.Equal(
        [
            "endpoints/orders-api.yaml",
            "endpoints/billing-api.yaml"
        ], result.Options.EndpointFiles);
        Assert.Equal("artifacts/report.xml", result.Options.OutputFilePath);
        Assert.Equal(CliFileOutputFormat.Xml, result.Options.ResolveOutputFileFormat());
    }

    [Fact]
    public void Parse_WithAllAndEndpointFile_ReturnsInvalidResult()
    {
        var result = CliOptions.Parse(
        [
            "--cli",
            "--all",
            "--endpoint-file", "endpoints/orders-api.yaml"
        ]);

        Assert.False(result.IsValid);
        Assert.Contains("either --all or one or more --endpoint-file", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Parse_WithOutputFormatButNoOutputFile_ReturnsInvalidResult()
    {
        var result = CliOptions.Parse(["--cli", "--all", "--output-format", "xml"]);

        Assert.False(result.IsValid);
        Assert.Contains("--output-format requires --output-file", result.ErrorMessage, StringComparison.OrdinalIgnoreCase);
    }
}
