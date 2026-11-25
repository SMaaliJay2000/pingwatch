using FluentValidation;
using FluentValidation.Results;
using PingWatch.Domain;

namespace PingWatch.Application.Monitors;

public sealed class MonitorReportService(IMonitorStore monitors, IMonitorHistory history)
{
    public async Task<PagedResponse<CheckResultResponse>?> GetResultsAsync(
        Guid monitorId, ResultsQuery query, CancellationToken cancellationToken)
    {
        await ResultsQueryValidator.Instance.ValidateAndThrowAsync(query, cancellationToken);

        if (await monitors.FindAsync(monitorId, cancellationToken) is null)
        {
            return null;
        }

        // One extra row tells us whether there is another page, without counting the whole history.
        var rows = await history.ResultsAsync(
            monitorId,
            query.From?.ToUniversalTime(),
            query.To?.ToUniversalTime(),
            (query.Page - 1) * query.PageSize,
            query.PageSize + 1,
            cancellationToken);

        return ToPage(rows, query.Page, query.PageSize);
    }

    public async Task<PagedResponse<IncidentResponse>> GetIncidentsAsync(
        IncidentsQuery query, CancellationToken cancellationToken)
    {
        await IncidentsQueryValidator.Instance.ValidateAndThrowAsync(query, cancellationToken);

        var rows = await history.IncidentsAsync(
            query.MonitorId,
            query.Status is null ? null : query.Status == "open",
            (query.Page - 1) * query.PageSize,
            query.PageSize + 1,
            cancellationToken);

        return ToPage(rows, query.Page, query.PageSize);
    }

    /// <summary>
    /// 24h and 7d come from raw results. 30d and 90d come from daily_stats, plus today's
    /// raw results because the nightly rollup hasn't covered today yet.
    /// </summary>
    public async Task<StatsResponse?> GetStatsAsync(
        Guid monitorId, string range, CancellationToken cancellationToken)
    {
        if (range is not ("24h" or "7d" or "30d" or "90d"))
        {
            throw new ValidationException([new ValidationFailure("Range", "Use 24h, 7d, 30d or 90d.")]);
        }

        if (await monitors.FindAsync(monitorId, cancellationToken) is null)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        IReadOnlyList<StatsBucket> buckets = range switch
        {
            "24h" => await history.RawBucketsAsync(
                monitorId, now.AddHours(-24), now, TimeSpan.FromMinutes(5), cancellationToken),
            "7d" => await history.RawBucketsAsync(
                monitorId, now.AddDays(-7), now, TimeSpan.FromHours(1), cancellationToken),
            "30d" => await DailyWithTodayAsync(monitorId, 30, now, cancellationToken),
            _ => await DailyWithTodayAsync(monitorId, 90, now, cancellationToken),
        };

        return Summarise(range, buckets);
    }

    // ponytail: between midnight UTC and the rollup run, yesterday is in neither source.
    private async Task<IReadOnlyList<StatsBucket>> DailyWithTodayAsync(
        Guid monitorId, int days, DateTimeOffset now, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var startOfToday = new DateTimeOffset(now.UtcDateTime.Date, TimeSpan.Zero);

        var past = await history.DailyBucketsAsync(
            monitorId, today.AddDays(1 - days), today, cancellationToken);
        var current = await history.RawBucketsAsync(
            monitorId, startOfToday, now, TimeSpan.FromDays(1), cancellationToken);

        return [.. past, .. current];
    }

    private static StatsResponse Summarise(string range, IReadOnlyList<StatsBucket> buckets)
    {
        var checks = buckets.Sum(b => b.Checks);
        var failures = buckets.Sum(b => b.Failures);

        // Weight each bucket's average by the successful checks behind it.
        var timed = buckets.Where(b => b.AvgMs is not null && b.Checks > b.Failures).ToList();
        var timedChecks = timed.Sum(b => (long)(b.Checks - b.Failures));
        int? averageMs = timedChecks == 0
            ? null
            : (int)Math.Round(timed.Sum(b => (double)b.AvgMs!.Value * (b.Checks - b.Failures)) / timedChecks);

        return new StatsResponse(
            range,
            Uptime.Percent(checks, failures),
            checks,
            failures,
            averageMs,
            [.. buckets
                .Where(b => b.Checks > 0)
                .Select(b => new StatsPoint(
                    b.At, b.Checks, b.Failures, Uptime.Percent(b.Checks, b.Failures)!.Value, b.AvgMs))]);
    }

    private static PagedResponse<T> ToPage<T>(IReadOnlyList<T> rows, int page, int pageSize) =>
        new([.. rows.Take(pageSize)], page, pageSize, rows.Count > pageSize);
}