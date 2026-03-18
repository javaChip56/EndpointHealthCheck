using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Serialization;

namespace ApiHealthDashboard.Cli;

public static class CliReportSerializer
{
    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static string SerializeJson(CliExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        return JsonSerializer.Serialize(report, JsonOptions);
    }

    public static string SerializeXml(CliExecutionReport report)
    {
        ArgumentNullException.ThrowIfNull(report);

        var serializer = new XmlSerializer(typeof(CliExecutionReport));
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            OmitXmlDeclaration = false
        };

        using var writer = new StringWriter();
        using var xmlWriter = XmlWriter.Create(writer, settings);
        serializer.Serialize(xmlWriter, report);
        return writer.ToString();
    }

    public static async Task WriteToFileAsync(
        CliExecutionReport report,
        string outputFilePath,
        CliFileOutputFormat format,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(outputFilePath);

        var directory = Path.GetDirectoryName(Path.GetFullPath(outputFilePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var content = format switch
        {
            CliFileOutputFormat.Xml => SerializeXml(report),
            _ => SerializeJson(report)
        };

        await File.WriteAllTextAsync(outputFilePath, content, Encoding.UTF8, cancellationToken);
    }
}
