using System.Xml.Serialization;
using ApiHealthDashboard.Configuration;

namespace ApiHealthDashboard.Cli;

[XmlRoot("suiteExecution")]
public sealed class CliExecutionReport
{
    public string Mode { get; set; } = "suite";

    public string DashboardConfigPath { get; set; } = string.Empty;

    public string ExecutedUtc { get; set; } = string.Empty;

    [XmlArray("selectedEndpointFiles")]
    [XmlArrayItem("file")]
    public List<string> SelectedEndpointFiles { get; set; } = new();

    [XmlArray("configurationWarnings")]
    [XmlArrayItem("warning")]
    public List<string> ConfigurationWarnings { get; set; } = new();

    public CliExecutionSummary Summary { get; set; } = new();

    [XmlArray("endpoints")]
    [XmlArrayItem("endpoint")]
    public List<CliEndpointExecutionReport> Endpoints { get; set; } = new();
}

public sealed class CliExecutionSummary
{
    public int TotalEndpoints { get; set; }

    public int EnabledEndpoints { get; set; }

    public int ExecutedEndpoints { get; set; }

    public int SkippedEndpoints { get; set; }

    public int SuccessfulPolls { get; set; }

    public int FailedPolls { get; set; }

    public int HealthyEndpoints { get; set; }

    public int DegradedEndpoints { get; set; }

    public int UnhealthyEndpoints { get; set; }

    public int UnknownEndpoints { get; set; }

    public string OverallStatus { get; set; } = "Unknown";
}

public sealed class CliEndpointExecutionReport
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string Url { get; set; } = string.Empty;

    public bool Enabled { get; set; }

    public string Priority { get; set; } = EndpointPriority.Normal;

    public int FrequencySeconds { get; set; }

    public int? TimeoutSeconds { get; set; }

    public string ExecutionState { get; set; } = "Skipped";

    public string Status { get; set; } = "Unknown";

    public string PollResultKind { get; set; } = "Unknown";

    public string? CheckedUtc { get; set; }

    public long? DurationMs { get; set; }

    public int? StatusCode { get; set; }

    public string? ErrorMessage { get; set; }

    public string? ResponseBody { get; set; }

    public CliSnapshotReport? Snapshot { get; set; }
}

public sealed class CliSnapshotReport
{
    public string OverallStatus { get; set; } = "Unknown";

    public string RetrievedUtc { get; set; } = string.Empty;

    public long DurationMs { get; set; }

    public string RawPayload { get; set; } = string.Empty;

    [XmlArray("metadataEntries")]
    [XmlArrayItem("entry")]
    public List<CliKeyValueEntry> MetadataEntries { get; set; } = new();

    [XmlArray("nodes")]
    [XmlArrayItem("node")]
    public List<CliNodeReport> Nodes { get; set; } = new();
}

public sealed class CliNodeReport
{
    public string Name { get; set; } = string.Empty;

    public string Status { get; set; } = "Unknown";

    public string? Description { get; set; }

    public string? ErrorMessage { get; set; }

    public string? DurationText { get; set; }

    [XmlArray("dataEntries")]
    [XmlArrayItem("entry")]
    public List<CliKeyValueEntry> DataEntries { get; set; } = new();

    [XmlArray("children")]
    [XmlArrayItem("node")]
    public List<CliNodeReport> Children { get; set; } = new();
}

public sealed class CliKeyValueEntry
{
    [XmlAttribute("key")]
    public string Key { get; set; } = string.Empty;

    public string? Value { get; set; }
}
