using PingWatch.Domain;

namespace PingWatch.Application.Checks;

/// <summary>Used by the worker, so it works across all accounts.</summary>
public interface ICheckStore
{
    /// <summary>
    /// Takes up to <paramref name="limit"/> due monitors and moves their next check forward.
    /// Safe to call from several workers: no monitor is returned to two of them.
    /// </summary>
    Task<IReadOnlyList<CheckJob>> ClaimDueAsync(int limit, DateTimeOffset now, CancellationToken cancellationToken);

    /// <summary>Runs the action in a transaction with the monitor's row locked.</summary>
    Task<T> WithMonitorLockAsync<T>(
        Guid monitorId, Func<UptimeMonitor?, Task<T>> action, CancellationToken cancellationToken);

    Task<Incident?> FindOpenIncidentAsync(Guid monitorId, CancellationToken cancellationToken);

    void Add(CheckResult result);

    void Add(Incident incident);

    Task SaveAsync(CancellationToken cancellationToken);
}

/// <summary>Maintenance of check history across all accounts. Days are UTC.</summary>
public interface IRollupStore
{
    /// <summary>Creates the check_results partition for the month, if missing.</summary>
    Task EnsurePartitionAsync(DateOnly month, CancellationToken cancellationToken);

    /// <summary>Writes daily_stats for finished days not covered yet. Safe to repeat.</summary>
    Task<int> AggregateBeforeAsync(DateOnly today, CancellationToken cancellationToken);

    /// <summary>Removes raw results older than each account's plan keeps them.</summary>
    Task<int> PurgeRawResultsAsync(DateOnly today, CancellationToken cancellationToken);

    Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset now, CancellationToken cancellationToken);
}

public interface IDomainEventPublisher
{
    ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken);
}