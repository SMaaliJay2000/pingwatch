using PingWatch.Domain;

namespace PingWatch.Application.Monitors;

/// <summary>Monitors of the current tenant only.</summary>
public interface IMonitorStore
{
    Task<IReadOnlyList<UptimeMonitor>> ListAsync(CancellationToken cancellationToken);

    /// <summary>Tracked: changes are stored by <see cref="SaveAsync"/>.</summary>
    Task<UptimeMonitor?> FindAsync(Guid id, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Monitors that are not paused.</summary>
    Task<int> CountActiveAsync(CancellationToken cancellationToken);

    Task<Incident?> FindOpenIncidentAsync(Guid monitorId, CancellationToken cancellationToken);

    void Add(UptimeMonitor monitor);

    /// <summary>Deletes the monitor and, through the database, its history.</summary>
    Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task SaveAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Runs the action in a transaction that holds a lock on the current account,
    /// so two requests can't both pass the same plan limit check.
    /// </summary>
    Task<T> WithAccountLockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken);
}

public sealed record CheckTotals(int Checks, int Failures);

/// <summary>AvgMs covers successful checks only.</summary>
public sealed record StatsBucket(DateTimeOffset At, int Checks, int Failures, int? AvgMs);

/// <summary>Read-only history of the current tenant. Times passed in must be UTC.</summary>
public interface IMonitorHistory
{
    Task<IReadOnlyDictionary<Guid, CheckTotals>> TotalsSinceAsync(
        IReadOnlyCollection<Guid> monitorIds, DateTimeOffset since, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<CheckResultResponse>> ResultsAsync(
        Guid monitorId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take,
        CancellationToken cancellationToken);

    /// <summary>Raw check results grouped into UTC-aligned buckets.</summary>
    Task<IReadOnlyList<StatsBucket>> RawBucketsAsync(
        Guid monitorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucketSize,
        CancellationToken cancellationToken);

    /// <summary>One bucket per daily_stats row in [from, before).</summary>
    Task<IReadOnlyList<StatsBucket>> DailyBucketsAsync(
        Guid monitorId, DateOnly from, DateOnly before, CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<IncidentResponse>> IncidentsAsync(
        Guid? monitorId, bool? open, int skip, int take, CancellationToken cancellationToken);
}