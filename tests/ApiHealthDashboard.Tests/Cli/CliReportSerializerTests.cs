using ApiHealthDashboard.Cli;

namespace ApiHealthDashboard.Tests.Cli;

public sealed class CliReportSerializerTests
{
    [Fact]
    public void SerializeJson_UsesMachineReadableCamelCaseOutput()
    {
        var report = new CliExecutionReport
        {
            Mode = "suite",
            DashboardConfigPath = "dashboard.yaml",
            ExecutedUtc = "2026-03-18T00:00:00.0000000+00:00",
            Summary = new CliExecutionSummary
            {
                TotalEndpoints = 1,
                OverallStatus = "Healthy"
            }
        };

        var json = CliReportSerializer.SerializeJson(report);

        Assert.Contains("\"dashboardConfigPath\"", json, StringComparison.Ordinal);
        Assert.Contains("\"overallStatus\": \"Healthy\"", json, StringComparison.Ordinal);
    }

    [Fact]
    public void SerializeXml_WritesSuiteExecutionDocument()
    {
        var report = new CliExecutionReport
        {
            Mode = "suite",
            DashboardConfigPath = "dashboard.yaml",
            ExecutedUtc = "2026-03-18T00:00:00.0000000+00:00",
            Summary = new CliExecutionSummary
            {
                TotalEndpoints = 1,
                OverallStatus = "Healthy"
            }
        };

        var xml = CliReportSerializer.SerializeXml(report);

        Assert.Contains("<suiteExecution", xml, StringComparison.Ordinal);
        Assert.Contains("<OverallStatus>Healthy</OverallStatus>", xml, StringComparison.Ordinal);
    }
}
