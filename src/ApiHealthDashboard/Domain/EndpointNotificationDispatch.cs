namespace ApiHealthDashboard.Domain;

public sealed class EndpointNotificationDispatch
{
    public string EventType { get; set; } = string.Empty;

    public string ConditionLabel { get; set; } = string.Empty;

    public string Signature { get; set; } = string.Empty;

    public DateTimeOffset SentUtc { get; set; }

    public List<string> To { get; set; } = new();

    public List<string> Cc { get; set; } = new();

    public EndpointNotificationDispatch Clone()
    {
        return new EndpointNotificationDispatch
        {
            EventType = EventType,
            ConditionLabel = ConditionLabel,
            Signature = Signature,
            SentUtc = SentUtc,
            To = [.. To],
            Cc = [.. Cc]
        };
    }
}
