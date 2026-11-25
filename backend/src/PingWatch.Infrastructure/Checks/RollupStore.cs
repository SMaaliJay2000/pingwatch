using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Checks;

internal sealed partial class RollupStore(PingWatchDbContext db) : IRollupStore
{
    public Task EnsurePartitionAsync(DateOnly month, CancellationToken cancellationToken) =>
        db.Database.ExecuteSqlAsync($"SELECT ensure_check_results_partition({month})", cancellationToken);

    public async Task<int> AggregateBeforeAsync(DateOnly today, CancellationToken cancellationToken)
    {
        // Redo from the day before the newest summary. A day with raw rows is always complete,
        // because purging only ever removes whole days.
        var newest = await db.DailyStats.IgnoreQueryFilters()
            .MaxAsync(d => (DateOnly?)d.Date, cancellationToken);
        var from = StartOfDay(newest?.AddDays(-1) ?? new DateOnly(2000, 1, 1));
        var before = StartOfDay(today);

        // avg_ms and p95_ms cover successful checks only, matching the live stats.
        return await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO daily_stats (account_id, monitor_id, date, checks, failures, uptime_pct, avg_ms, p95_ms)
            SELECT account_id,
                   monitor_id,
                   (checked_at AT TIME ZONE 'UTC')::date,
                   count(*)::int,
                   (count(*) FILTER (WHERE NOT is_up))::int,
                   trunc(100.0 * count(*) FILTER (WHERE is_up) / count(*), 3),
                   (round(avg(response_ms) FILTER (WHERE is_up)))::int,
                   (round((percentile_cont(0.95) WITHIN GROUP (ORDER BY response_ms)
                           FILTER (WHERE is_up))::numeric))::int
            FROM check_results
            WHERE checked_at >= {from} AND checked_at < {before}
            GROUP BY 1, 2, 3
            ON CONFLICT (monitor_id, date) DO UPDATE SET
                checks = EXCLUDED.checks,
                failures = EXCLUDED.failures,
                uptime_pct = EXCLUDED.uptime_pct,
                avg_ms = EXCLUDED.avg_ms,
                p95_ms = EXCLUDED.p95_ms
            """,
            cancellationToken);
    }

    public async Task<int> PurgeRawResultsAsync(DateOnly today, CancellationToken cancellationToken)
    {
        var removed = 0;

        // ponytail: one DELETE per plan; batch it if the nightly delete starts holding up inserts.
        foreach (var tier in Enum.GetValues<PlanTier>())
        {
            var plan = SnakeCaseEnum.ToText(tier);
            var cutoff = StartOfDay(today.AddDays(-Plans.For(tier).RetentionDays));

            removed += await db.Database.ExecuteSqlAsync(
                $"""
                DELETE FROM check_results AS r
                USING accounts AS a
                WHERE r.account_id = a.id AND a.plan = {plan} AND r.checked_at < {cutoff}
                """,
                cancellationToken);
        }

        await DropExpiredPartitionsAsync(
            today.AddDays(-Enum.GetValues<PlanTier>().Max(t => Plans.For(t).RetentionDays)), cancellationToken);

        return removed;
    }

    public Task<int> PurgeExpiredRefreshTokensAsync(DateTimeOffset now, CancellationToken cancellationToken) =>
        db.RefreshTokens.Where(t => t.ExpiresAt < now).ExecuteDeleteAsync(cancellationToken);

    // Months that even the longest plan no longer keeps are dropped whole.
    private async Task DropExpiredPartitionsAsync(DateOnly oldestKept, CancellationToken cancellationToken)
    {
        var names = await db.Database
            .SqlQuery<string>($"""
                SELECT c.relname::text AS "Value"
                FROM pg_inherits AS i
                JOIN pg_class AS c ON c.oid = i.inhrelid
                WHERE i.inhparent = 'check_results'::regclass
                """)
            .ToListAsync(cancellationToken);

        foreach (var name in names)
        {
            var match = MonthlyPartition().Match(name);
            if (!match.Success)
            {
                continue;
            }

            var month = new DateOnly(
                int.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(match.Groups[2].Value, CultureInfo.InvariantCulture),
                1);

            if (month.AddMonths(1) <= oldestKept)
            {
                // Identifiers can't be parameters. The name matched the strict pattern above.
                var sql = "DROP TABLE \"" + name + "\"";
                await db.Database.ExecuteSqlRawAsync(sql, cancellationToken);
            }
        }
    }

    private static DateTimeOffset StartOfDay(DateOnly date) =>
        new(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero);

    [GeneratedRegex(@"^check_results_(\d{4})_(0[1-9]|1[0-2])$")]
    private static partial Regex MonthlyPartition();
}