namespace PingWatch.Domain;

public interface IDomainEvent
{
    Guid AccountId { get; }

    Guid MonitorId { get; }
}

public sealed record MonitorWentDown(
    Guid AccountId, Guid MonitorId, Guid IncidentId, DateTimeOffset At, string Cause) : IDomainEvent;

public sealed record MonitorRecovered(
    Guid AccountId, Guid MonitorId, Guid IncidentId, DateTimeOffset At, int DurationSec) : IDomainEvent;

/// <summary>Raised on every check inside the warning window. M5 sends each threshold only once.</summary>
public sealed record SslExpiring(
    Guid AccountId, Guid MonitorId, DateTimeOffset ExpiresAt, int DaysLeft) : IDomainEvent;