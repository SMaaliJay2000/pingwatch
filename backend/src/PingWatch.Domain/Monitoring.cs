namespace PingWatch.Domain;

public class UptimeMonitor : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public required string Name { get; set; }
    public MonitorType Type { get; set; }
    public string? Url { get; set; }
    public string? Host { get; set; }
    public int? Port { get; set; }
    public CheckMethod? Method { get; set; }
    public string ExpectedStatus { get; set; } = "200-399";
    public string? Keyword { get; set; }
    public KeywordMode? KeywordMode { get; set; }
    public int TimeoutMs { get; set; } = 10_000;
    public int IntervalSec { get; set; } = 300;
    public MonitorStatus Status { get; set; } = MonitorStatus.Pending;
    public DateTimeOffset? LastCheckedAt { get; set; }
    public DateTimeOffset NextCheckAt { get; set; } = DateTimeOffset.UtcNow;
    public int ConsecutiveFailures { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

    /// <summary>Stops scheduling. Returns false if it was already paused.</summary>
    public bool Pause()
    {
        if (Status == MonitorStatus.Paused)
        {
            return false;
        }

        Status = MonitorStatus.Paused;
        ConsecutiveFailures = 0;
        return true;
    }

    /// <summary>Returns to pending and due now. Returns false if it wasn't paused.</summary>
    public bool Resume(DateTimeOffset now)
    {
        if (Status != MonitorStatus.Paused)
        {
            return false;
        }

        Status = MonitorStatus.Pending;
        ConsecutiveFailures = 0;
        NextCheckAt = now;
        return true;
    }
}

public class CheckResult : ITenantOwned
{
    public long Id { get; set; }
    public Guid AccountId { get; set; }
    public Guid MonitorId { get; set; }
    public DateTimeOffset CheckedAt { get; set; }
    public bool IsUp { get; set; }
    public int? StatusCode { get; set; }
    public int? ResponseMs { get; set; }
    public string? Error { get; set; }
    public string Region { get; set; } = "default";
}

public class DailyStat : ITenantOwned
{
    public Guid AccountId { get; set; }
    public Guid MonitorId { get; set; }
    public DateOnly Date { get; set; }
    public int Checks { get; set; }
    public int Failures { get; set; }
    public decimal UptimePct { get; set; }
    public int? AvgMs { get; set; }
    public int? P95Ms { get; set; }
}

public class Incident : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public Guid MonitorId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset? ResolvedAt { get; set; }
    public required string Cause { get; set; }
    public int? DurationSec { get; set; }

    /// <summary>Closes the incident and records how long it lasted. Does nothing if already closed.</summary>
    public void Resolve(DateTimeOffset now)
    {
        if (ResolvedAt is not null)
        {
            return;
        }

        ResolvedAt = now;
        DurationSec = (int)Math.Max(0, (now - StartedAt).TotalSeconds);
    }
}
