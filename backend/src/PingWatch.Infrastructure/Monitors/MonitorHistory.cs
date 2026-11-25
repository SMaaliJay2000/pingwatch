using Microsoft.EntityFrameworkCore;
using PingWatch.Application;
using PingWatch.Application.Monitors;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Monitors;

internal sealed class MonitorHistory(PingWatchDbContext db, ITenantContext tenant) : IMonitorHistory
{
    // ponytail: aggregates raw rows on every request; cache or pre-aggregate if the dashboard gets slow.
    public async Task<IReadOnlyDictionary<Guid, CheckTotals>> TotalsSinceAsync(
        IReadOnlyCollection<Guid> monitorIds, DateTimeOffset since, CancellationToken cancellationToken)
    {
        var rows = await db.CheckResults
            .Where(r => monitorIds.Contains(r.MonitorId) && r.CheckedAt >= since)
            .GroupBy(r => r.MonitorId)
            .Select(g => new { MonitorId = g.Key, Checks = g.Count(), Failures = g.Count(r => !r.IsUp) })
            .ToListAsync(cancellationToken);

        return rows.ToDictionary(r => r.MonitorId, r => new CheckTotals(r.Checks, r.Failures));
    }

    public async Task<IReadOnlyList<CheckResultResponse>> ResultsAsync(
        Guid monitorId, DateTimeOffset? from, DateTimeOffset? to, int skip, int take,
        CancellationToken cancellationToken)
    {
        var query = db.CheckResults.Where(r => r.MonitorId == monitorId);

        if (from is not null)
        {
            query = query.Where(r => r.CheckedAt >= from);
        }

        if (to is not null)
        {
            query = query.Where(r => r.CheckedAt <= to);
        }

        return await query
            .OrderByDescending(r => r.CheckedAt).ThenByDescending(r => r.Id)
            .Skip(skip).Take(take)
            .Select(r => new CheckResultResponse(
                r.CheckedAt, r.IsUp, r.StatusCode, r.ResponseMs, r.Error, r.Region))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StatsBucket>> RawBucketsAsync(
        Guid monitorId, DateTimeOffset from, DateTimeOffset to, TimeSpan bucketSize,
        CancellationToken cancellationToken)
    {
        if (tenant.AccountId is not { } accountId)
        {
            return [];
        }

        // Raw SQL bypasses the global query filters, so account_id MUST stay in this WHERE clause.
        var rows = await db.Database
            .SqlQuery<BucketRow>($"""
                SELECT date_bin({bucketSize}, checked_at, TIMESTAMPTZ '2000-01-01 00:00:00+00') AS at,
                       count(*)::int AS checks,
                       (count(*) FILTER (WHERE NOT is_up))::int AS failures,
                       (round(avg(response_ms) FILTER (WHERE is_up)))::int AS avg_ms
                FROM check_results
                WHERE account_id = {accountId} AND monitor_id = {monitorId}
                  AND checked_at >= {from} AND checked_at <= {to}
                GROUP BY 1
                ORDER BY 1
                """)
            .ToListAsync(cancellationToken);

        return [.. rows.Select(r => new StatsBucket(r.At, r.Checks, r.Failures, r.AvgMs))];
    }

    public async Task<IReadOnlyList<StatsBucket>> DailyBucketsAsync(
        Guid monitorId, DateOnly from, DateOnly before, CancellationToken cancellationToken)
    {
        var days = await db.DailyStats
            .Where(d => d.MonitorId == monitorId && d.Date >= from && d.Date < before)
            .OrderBy(d => d.Date)
            .Select(d => new { d.Date, d.Checks, d.Failures, d.AvgMs })
            .ToListAsync(cancellationToken);

        return [.. days.Select(d => new StatsBucket(
            new DateTimeOffset(d.Date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            d.Checks, d.Failures, d.AvgMs))];
    }

    public async Task<IReadOnlyList<IncidentResponse>> IncidentsAsync(
        Guid? monitorId, bool? open, int skip, int take, CancellationToken cancellationToken)
    {
        var incidents = db.Incidents.AsQueryable();

        if (monitorId is not null)
        {
            incidents = incidents.Where(i => i.MonitorId == monitorId);
        }

        if (open is not null)
        {
            incidents = incidents.Where(i => (i.ResolvedAt == null) == open);
        }

        return await (
                from incident in incidents
                join monitor in db.Monitors on incident.MonitorId equals monitor.Id
                orderby incident.StartedAt descending, incident.Id descending
                select new IncidentResponse(
                    incident.Id, incident.MonitorId, monitor.Name, incident.StartedAt,
                    incident.ResolvedAt, incident.Cause, incident.DurationSec))
            .Skip(skip).Take(take)
            .ToListAsync(cancellationToken);
    }

    private sealed class BucketRow
    {
        public DateTimeOffset At { get; init; }
        public int Checks { get; init; }
        public int Failures { get; init; }
        public int? AvgMs { get; init; }
    }
}