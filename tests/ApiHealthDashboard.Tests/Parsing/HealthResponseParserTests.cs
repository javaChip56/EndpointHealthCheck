using ApiHealthDashboard.Configuration;
using ApiHealthDashboard.Parsing;
using ApiHealthDashboard.Tests.Logging;
using Microsoft.Extensions.Logging;

namespace ApiHealthDashboard.Tests.Parsing;

public sealed class HealthResponseParserTests
{
    private readonly TestLogger<HealthResponseParser> _logger = new();

    private HealthResponseParser CreateParser()
    {
        return new HealthResponseParser(_logger);
    }

    [Fact]
    public void Parse_WithFlatPayload_ReturnsSnapshotAndPreservesRootMetadata()
    {
        var snapshot = CreateParser().Parse(
            CreateEndpoint("orders-api"),
            """
            {
              "status": "Healthy",
              "description": "All good",
              "duration": "00:00:00.010",
              "data": {
                "version": "1.2.3"
              },
              "tags": ["core", "orders"]
            }
            """,
            10);

        Assert.Equal("Healthy", snapshot.OverallStatus);
        Assert.Empty(snapshot.Nodes);
        Assert.Equal(10, snapshot.DurationMs);
        Assert.Equal("1.2.3", snapshot.Metadata["version"]);
        var tags = Assert.IsType<List<object?>>(snapshot.Metadata["tags"]);
        Assert.Equal(["core", "orders"], tags.Cast<string?>().ToArray());
    }

    [Fact]
    public void Parse_WithEntriesPayload_ParsesTopLevelNodesAndNodeData()
    {
        var snapshot = CreateParser().Parse(
            CreateEndpoint("billing-api"),
            """
            {
              "status": "Degraded",
              "entries": {
                "self": {
                  "status": "Healthy",
                  "description": "OK"
                },
                "database": {
                  "status": "Degraded",
                  "duration": "00:00:00.500",
                  "data": {
                    "provider": "sql"
                  },
                  "thresholdMs": 400
                }
              }
            }
            """,
            500);

        Assert.Equal("Degraded", snapshot.OverallStatus);
        Assert.Equal(2, snapshot.Nodes.Count);

        var self = Assert.Single(snapshot.Nodes.Where(static node => node.Name == "self"));
        Assert.Equal("Healthy", self.Status);
        Assert.Equal("OK", self.Description);

        var database = Assert.Single(snapshot.Nodes.Where(static node => node.Name == "database"));
        Assert.Equal("Degraded", database.Status);
        Assert.Equal("00:00:00.500", database.DurationText);
        Assert.Equal("sql", database.Data["provider"]);
        Assert.Equal(400L, database.Data["thresholdMs"]);
    }

    [Fact]
    public void Parse_WithNestedHealthPayload_ParsesRecursiveChildren()
    {
        var snapshot = CreateParser().Parse(
            CreateEndpoint("identity-api"),
            """
            {
              "status": "Unhealthy",
              "entries": {
                "dependencies": {
                  "status": "Unhealthy",
                  "children": {
                    "cache": {
                      "status": "Healthy"
                    },
                    "reporting": {
                      "status": "Unhealthy",
                      "exception": "Timeout while contacting reporting API"
                    }
                  }
                }
              }
            }
            """,
            900);

        Assert.Equal("Unhealthy", snapshot.OverallStatus);

        var dependencies = Assert.Single(snapshot.Nodes);
        Assert.Equal("dependencies", dependencies.Name);
        Assert.Equal("Unhealthy", dependencies.Status);
        Assert.Equal(2, dependencies.Children.Count);

        var reporting = Assert.Single(dependencies.Children.Where(static child => child.Name == "reporting"));
        Assert.Equal("Unhealthy", reporting.Status);
        Assert.Equal("Timeout while contacting reporting API", reporting.ErrorMessage);
    }

    [Fact]
    public void Parse_WithIncludeAndExcludeFilters_AppliesFilteringRecursively()
    {
        var endpoint = CreateEndpoint("inventory-api");
        endpoint.IncludeChecks = ["database", "reporting"];
        endpoint.ExcludeChecks = ["reporting"];

        var snapshot = CreateParser().Parse(
            endpoint,
            """
            {
              "status": "Degraded",
              "entries": {
                "self": {
                  "status": "Healthy"
                },
                "database": {
                  "status": "Healthy"
                },
                "dependencies": {
                  "status": "Degraded",
                  "children": {
                    "cache": {
                      "status": "Healthy"
                    },
                    "reporting": {
                      "status": "Degraded"
                    }
                  }
                }
              }
            }
            """,
            200);

        Assert.Single(snapshot.Nodes);
        Assert.Equal("database", snapshot.Nodes[0].Name);
    }

    [Fact]
    public void Parse_WithNestedIncludeFilter_KeepsParentWhenIncludedChildSurvives()
    {
        var endpoint = CreateEndpoint("inventory-api");
        endpoint.IncludeChecks = ["reporting"];

        var snapshot = CreateParser().Parse(
            endpoint,
            """
            {
              "entries": {
                "dependencies": {
                  "children": {
                    "cache": {
                      "status": "Healthy"
                    },
                    "reporting": {
                      "status": "Degraded"
                    }
                  }
                }
              }
            }
            """,
            120);

        var dependencies = Assert.Single(snapshot.Nodes);
        Assert.Equal("dependencies", dependencies.Name);
        var reporting = Assert.Single(dependencies.Children);
        Assert.Equal("reporting", reporting.Name);
        Assert.Equal("Degraded", reporting.Status);
    }

    [Fact]
    public void Parse_WithMalformedJson_ReturnsParserErrorSnapshot()
    {
        var snapshot = CreateParser().Parse(
            CreateEndpoint("broken-api"),
            """
            { "status": "Healthy", "entries": { "db": { "status": "Healthy" }
            """,
            15);

        Assert.Equal("Unknown", snapshot.OverallStatus);
        Assert.Empty(snapshot.Nodes);
        Assert.True(snapshot.Metadata.ContainsKey("parserError"));
        Assert.NotNull(snapshot.Metadata["parserError"]);
    }

    [Fact]
    public void Parse_WithMalformedJson_LogsWarning()
    {
        CreateParser().Parse(
            CreateEndpoint("broken-api"),
            """
            { "status": "Healthy",
            """,
            5);

        var warning = Assert.Single(_logger.Entries.Where(static entry => entry.LogLevel == LogLevel.Warning));
        Assert.Contains("broken-api", warning.Message, StringComparison.Ordinal);
        Assert.NotNull(warning.Exception);
    }

    private static EndpointConfig CreateEndpoint(string id)
    {
        return new EndpointConfig
        {
            Id = id,
            Name = id,
            Url = $"https://{id}.example.com/health"
        };
    }
}
