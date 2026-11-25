using System.Net;
using System.Text.Json;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MonitorReportTests(ApiFactory factory)
{
    [Fact]
    public async Task List_and_detail_show_uptime_over_the_last_24_hours_only()
    {
        var user = await factory.NewUserAsync();
        var busy = await user.CreateMonitorAsync();
        var idle = await user.CreateMonitorAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.SeedAsync(
            user.Result(busy, now.AddMinutes(-1), isUp: true),
            user.Result(busy, now.AddHours(-2), isUp: true),
            user.Result(busy, now.AddHours(-23), isUp: true),
            user.Result(busy, now.AddHours(-3), isUp: false),
            user.Result(busy, now.AddHours(-25), isUp: false));

        var list = (await user.Client.GetJsonAsync("/api/v1/monitors")).EnumerateArray()
            .ToDictionary(m => m.GetProperty("id").GetGuid());
        var detail = await user.Client.GetJsonAsync($"/api/v1/monitors/{busy}");

        list[busy].GetProperty("uptime24h").GetDecimal().Should().Be(75);
        list[idle].IsNull("uptime24h").Should().BeTrue();
        detail.GetProperty("uptime24h").GetDecimal().Should().Be(75);
    }

    [Fact]
    public async Task Results_are_paged_newest_first()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.SeedAsync([.. Enumerable.Range(1, 5)
            .Select(i => user.Result(id, now.AddMinutes(-i), isUp: true, responseMs: i))]);

        var first = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/results?pageSize=2");
        var last = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/results?page=3&pageSize=2");
        var everything = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/results");

        ResponseTimes(first).Should().Equal(1, 2);
        first.GetProperty("hasMore").GetBoolean().Should().BeTrue();
        ResponseTimes(last).Should().Equal(5);
        last.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        everything.Int("pageSize").Should().Be(50);
        ResponseTimes(everything).Should().Equal(1, 2, 3, 4, 5);

        static IEnumerable<int> ResponseTimes(JsonElement page) =>
            page.Array("items").Select(r => r.Int("responseMs"));
    }

    [Theory]
    [InlineData("Z", 0)]
    [InlineData("%2B05:30", 330)]
    [InlineData("-08:00", -480)]
    public async Task Results_can_be_filtered_by_time_in_any_offset(string suffix, int offsetMinutes)
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        var start = new DateTimeOffset(DateTimeOffset.UtcNow.UtcDateTime.Date.AddDays(-1), TimeSpan.Zero);
        await factory.SeedAsync([.. Enumerable.Range(0, 5)
            .Select(i => user.Result(id, start.AddHours(i), isUp: true, responseMs: i))]);

        string Format(DateTimeOffset time) =>
            time.ToOffset(TimeSpan.FromMinutes(offsetMinutes)).ToString("yyyy-MM-ddTHH:mm:ss") + suffix;

        var page = await user.Client.GetJsonAsync(
            $"/api/v1/monitors/{id}/results?from={Format(start.AddHours(1))}&to={Format(start.AddHours(3))}");

        page.Array("items").Select(r => r.Int("responseMs")).Should().Equal(3, 2, 1);
    }

    [Theory]
    [InlineData("results?page=0")]
    [InlineData("results?pageSize=0")]
    [InlineData("results?pageSize=201")]
    [InlineData("results?page=2000000000")]
    [InlineData("results?from=2026-01-02T00:00:00Z&to=2026-01-01T00:00:00Z")]
    [InlineData("results?from=yesterday")]
    [InlineData("stats?range=1y")]
    public async Task Bad_report_queries_are_rejected(string query)
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();

        var response = await user.Client.GetAsync($"/api/v1/monitors/{id}/{query}");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Stats_24h_groups_raw_results_into_five_minute_buckets()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        // Start of a five-minute bucket, about two hours ago.
        var now = DateTimeOffset.UtcNow;
        var bucket = new DateTimeOffset(now.Ticks - now.Ticks % TimeSpan.FromMinutes(5).Ticks, TimeSpan.Zero)
            .AddHours(-2);
        await factory.SeedAsync(
            user.Result(id, bucket, isUp: true, responseMs: 100),
            user.Result(id, bucket.AddMinutes(1), isUp: true, responseMs: 200),
            // A failure's time (often a timeout) must not distort the average.
            user.Result(id, bucket.AddMinutes(2), isUp: false, responseMs: 10_000),
            user.Result(id, bucket.AddMinutes(30), isUp: true, responseMs: 450),
            user.Result(id, now.AddHours(-30), isUp: false));

        var stats = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/stats");

        stats.String("range").Should().Be("24h");
        stats.Int("checks").Should().Be(4);
        stats.Int("failures").Should().Be(1);
        stats.GetProperty("uptimePct").GetDecimal().Should().Be(75);
        stats.Int("avgMs").Should().Be(250);
        var series = stats.Array("series");
        series.Select(p => p.GetProperty("at").GetDateTimeOffset()).Should().Equal(bucket, bucket.AddMinutes(30));
        series[0].Int("checks").Should().Be(3);
        series[0].Int("avgMs").Should().Be(150);
        series[0].GetProperty("uptimePct").GetDecimal().Should().Be(66.666m);
    }

    [Fact]
    public async Task Stats_7d_uses_raw_results_in_hourly_buckets()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.SeedAsync(
            user.Result(id, now.AddDays(-6), isUp: true),
            user.Result(id, now.AddDays(-2), isUp: false, responseMs: null),
            user.Result(id, now.AddDays(-8), isUp: false),
            // daily_stats must be ignored for short ranges.
            Daily(user, id, daysAgo: 3, checks: 100, failures: 100, avgMs: null));

        var stats = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/stats?range=7d");

        stats.Int("checks").Should().Be(2);
        stats.Int("failures").Should().Be(1);
        stats.Array("series").Should().HaveCount(2)
            .And.OnlyContain(p => p.GetProperty("at").GetDateTimeOffset().Minute == 0);
        stats.Array("series")[1].IsNull("avgMs").Should().BeTrue();
    }

    [Theory]
    // 30d: yesterday, the oldest day and today. 90d also reaches the row from 45 days ago.
    [InlineData("30d", 30, 3)]
    [InlineData("90d", 90, 4)]
    public async Task Long_ranges_use_daily_stats_plus_todays_raw_results(string range, int days, int expectedPoints)
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        var now = DateTimeOffset.UtcNow;
        await factory.SeedAsync(
            Daily(user, id, daysAgo: 1, checks: 100, failures: 10, avgMs: 200),
            Daily(user, id, daysAgo: 45, checks: 100, failures: 0, avgMs: 300),
            Daily(user, id, daysAgo: days - 1, checks: 100, failures: 0, avgMs: 200),
            Daily(user, id, daysAgo: days, checks: 100, failures: 100, avgMs: null),
            user.Result(id, now, isUp: true, responseMs: 500),
            // An old raw row with no daily_stats entry proves long ranges don't read raw history.
            user.Result(id, now.AddDays(-5), isUp: false));

        var stats = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/stats?range={range}");

        var series = stats.Array("series");
        // The row from exactly `days` ago is just outside the range.
        series.Should().HaveCount(expectedPoints);
        stats.Int("checks").Should().Be((series.Count - 1) * 100 + 1);
        stats.Int("failures").Should().Be(10);
        series[^1].Int("checks").Should().Be(1);
        series[^1].GetProperty("at").GetDateTimeOffset().UtcDateTime.Should().Be(now.UtcDateTime.Date);
        series.Select(p => p.GetProperty("at").GetDateTimeOffset()).Should().BeInAscendingOrder();
    }

    [Fact]
    public async Task Stats_without_data_have_no_uptime()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();

        var stats = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}/stats?range=90d");

        stats.IsNull("uptimePct").Should().BeTrue();
        stats.IsNull("avgMs").Should().BeTrue();
        stats.Int("checks").Should().Be(0);
        stats.Array("series").Should().BeEmpty();
    }

    [Fact]
    public async Task Incidents_can_be_filtered_and_paged()
    {
        var user = await factory.NewUserAsync();
        var first = await user.CreateMonitorAsync(MonitorTestHelpers.HttpMonitor("first"));
        var second = await user.CreateMonitorAsync(MonitorTestHelpers.HttpMonitor("second"));
        var now = DateTimeOffset.UtcNow;
        await factory.SeedAsync(
            Incident(first, now.AddHours(-3), resolved: true),
            Incident(first, now.AddHours(-1), resolved: false),
            Incident(second, now.AddHours(-2), resolved: true));

        var all = (await Get("")).Array("items");
        var open = (await Get("?status=open")).Array("items");
        var resolved = (await Get("?status=resolved")).Array("items");
        var ofSecond = (await Get($"?monitorId={second}")).Array("items");
        var paged = await Get("?pageSize=2&page=2");

        all.Select(i => i.String("monitorName")).Should().Equal("first", "second", "first");
        open.Should().ContainSingle().Which.IsNull("resolvedAt").Should().BeTrue();
        resolved.Should().HaveCount(2);
        ofSecond.Should().ContainSingle().Which.Int("durationSec").Should().Be(60);
        paged.Array("items").Should().ContainSingle();
        paged.GetProperty("hasMore").GetBoolean().Should().BeFalse();
        (await user.Client.GetAsync("/api/v1/incidents?status=closed"))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);

        Task<JsonElement> Get(string query) => user.Client.GetJsonAsync($"/api/v1/incidents{query}");

        Incident Incident(Guid monitorId, DateTimeOffset startedAt, bool resolved) => new()
        {
            AccountId = user.AccountId,
            MonitorId = monitorId,
            StartedAt = startedAt,
            ResolvedAt = resolved ? startedAt.AddSeconds(60) : null,
            DurationSec = resolved ? 60 : null,
            Cause = "HTTP 503",
        };
    }

    private static DailyStat Daily(
        TestUser user, Guid monitorId, int daysAgo, int checks, int failures, int? avgMs) => new()
        {
            AccountId = user.AccountId,
            MonitorId = monitorId,
            Date = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-daysAgo),
            Checks = checks,
            Failures = failures,
            UptimePct = Uptime.Percent(checks, failures)!.Value,
            AvgMs = avgMs,
        };
}
