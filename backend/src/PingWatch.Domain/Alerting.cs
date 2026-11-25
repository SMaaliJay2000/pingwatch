namespace PingWatch.Domain;

public class AlertChannel : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public AlertChannelType Type { get; set; }

    /// <summary>Stored as ciphertext once M5 adds Data Protection, so it is text rather than jsonb.</summary>
    public required string ConfigJson { get; set; }

    public bool IsVerified { get; set; }
}

public class MonitorChannel : ITenantOwned
{
    public Guid AccountId { get; set; }
    public Guid MonitorId { get; set; }
    public Guid ChannelId { get; set; }
}

public class AlertLogEntry : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public Guid MonitorId { get; set; }
    public Guid? IncidentId { get; set; }
    public Guid ChannelId { get; set; }
    public AlertType Type { get; set; }
    public DateTimeOffset SentAt { get; set; }
    public bool Success { get; set; }
    public string? Error { get; set; }
}
