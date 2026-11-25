using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Checks;

// The worker serves every account and has no tenant, so each query ignores the filters on purpose.
internal sealed class CheckStore(PingWatchDbContext db) : ICheckStore
{
    public async Task<IReadOnlyList<CheckJob>> ClaimDueAsync(
        int limit, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var freeMinimum = Plans.Free.MinIntervalSec;
        var starterMinimum = Plans.Starter.MinIntervalSec;
        var proMinimum = Plans.Pro.MinIntervalSec;

        // One statement: lock the due rows, skipping ones another worker holds, and reschedule them.
        // "OF m" leaves accounts unlocked. The plan's minimum interval still applies after a downgrade.
        var monitors = await db.Monitors
            .FromSql($"""
                WITH due AS (
                    SELECT m.id, a.plan
                    FROM monitors AS m
                    JOIN accounts AS a ON a.id = m.account_id
                    WHERE m.next_check_at <= {now} AND m.status <> 'paused' AND m.type <> 'heartbeat'
                    ORDER BY m.next_check_at
                    LIMIT {limit}
                    FOR UPDATE OF m SKIP LOCKED
                )
                UPDATE monitors AS m
                SET next_check_at = {now} + make_interval(secs => GREATEST(
                        m.interval_sec,
                        CASE due.plan
                            WHEN 'pro' THEN {proMinimum}
                            WHEN 'starter' THEN {starterMinimum}
                            ELSE {freeMinimum}
                        END))
                FROM due
                WHERE m.id = due.id
                RETURNING m.*
                """)
            .IgnoreQueryFilters()
            .AsNoTracking()
            .ToListAsync(cancellationToken);

        return [.. monitors.Select(CheckJob.From)];
    }

    public async Task<T> WithMonitorLockAsync<T>(
        Guid monitorId, Func<UptimeMonitor?, Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        var locked = await db.Monitors
            .FromSql($"SELECT * FROM monitors WHERE id = {monitorId} FOR NO KEY UPDATE")
            .IgnoreQueryFilters()
            .ToListAsync(cancellationToken);

        var result = await action(locked.SingleOrDefault());
        await transaction.CommitAsync(cancellationToken);

        return result;
    }

    public Task<Incident?> FindOpenIncidentAsync(Guid monitorId, CancellationToken cancellationToken) =>
        db.Incidents.IgnoreQueryFilters()
            .SingleOrDefaultAsync(i => i.MonitorId == monitorId && i.ResolvedAt == null, cancellationToken);

    public void Add(CheckResult result) => db.CheckResults.Add(result);

    public void Add(Incident incident) => db.Incidents.Add(incident);

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);
}