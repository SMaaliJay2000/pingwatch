using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.IntegrationTests;

[Collection(EngineCollection.Name)]
public class RollupTests(EngineFixture engine) : IAsyncLifetime
{
    public Task InitializeAsync() => engine.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private DateOnly Today => DateOnly.FromDateTime(engine.Time.Now.UtcDateTime);

    [Fact]
    public async Task Finished_days_are_aggregated_and_rerunning_changes_nothing()
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync());
        await engine.SaveAsync(
            Result(monitor, daysAgo: 1, 1, true, 100),
            Result(monitor, daysAgo: 1, 2, true, 200),
            Result(monitor, daysAgo: 1, 3, true, 300),
            Result(monitor, daysAgo: 1, 23, true, 400),
            // A failure's time must not affect avg or p95.
            Result(monitor, daysAgo: 1, 4, false, 9_999),
            Result(monitor, daysAgo: 2, 1, false, null),
            Result(monitor, daysAgo: 0, 0, true, 50));

        await RunAsync();
        await RunAsync();

        var days = await DailyAsync(monitor.Id);
        days.Select(d => d.Date).Should().Equal(Today.AddDays(-2), Today.AddDays(-1));
        var yesterday = days[1];
        yesterday.AccountId.Should().Be(monitor.AccountId);
        yesterday.Checks.Should().Be(5);
        yesterday.Failures.Should().Be(1);
        yesterday.UptimePct.Should().Be(80);
        yesterday.AvgMs.Should().Be(250);
        yesterday.P95Ms.Should().Be(385);
        days[0].UptimePct.Should().Be(0);
        days[0].AvgMs.Should().BeNull();
        days[0].P95Ms.Should().BeNull();
    }

    [Fact]
    public async Task Stored_uptime_matches_the_live_calculation()
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync());
        await engine.SaveAsync(
            Result(monitor, 1, 1, true, 1), Result(monitor, 1, 2, true, 1), Result(monitor, 1, 3, false, 1));

        await RunAsync();

        (await DailyAsync(monitor.Id)).Single().UptimePct.Should().Be(Uptime.Percent(3, 1));
    }

    [Theory]
    [InlineData(PlanTier.Free, 7)]
    [InlineData(PlanTier.Starter, 90)]
    [InlineData(PlanTier.Pro, 365)]
    public async Task Raw_results_are_kept_for_whole_days_by_plan_and_summaries_survive(PlanTier plan, int days)
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync(plan));
        await engine.SaveAsync(
            Result(monitor, daysAgo: days, 0, true, 100),
            Result(monitor, daysAgo: days + 1, 23, true, 100),
            Result(monitor, daysAgo: 1, 1, true, 100));

        await RunAsync();
        var summaries = await DailyAsync(monitor.Id);
        await RunAsync();

        var keptDays = await engine.WithDbAsync(db => db.CheckResults.IgnoreQueryFilters()
            .Where(r => r.MonitorId == monitor.Id).OrderBy(r => r.CheckedAt)
            .Select(r => r.CheckedAt).ToListAsync());
        keptDays.Select(d => DateOnly.FromDateTime(d.UtcDateTime))
            .Should().Equal(Today.AddDays(-days), Today.AddDays(-1));
        summaries.Select(d => d.Date).Should().Contain(Today.AddDays(-days - 1), "it was summarised before purging");
        (await DailyAsync(monitor.Id)).Should().BeEquivalentTo(summaries);
    }

    [Fact]
    public async Task Missed_nights_are_caught_up()
    {
        var monitor = await engine.AddMonitorAsync(await engine.AddAccountAsync(PlanTier.Starter));
        await engine.SaveAsync(Result(monitor, 6, 1, true, 1));
        await RunAsync();
        await engine.SaveAsync(Result(monitor, 4, 1, true, 1), Result(monitor, 3, 1, true, 1));

        await RunAsync();

        (await DailyAsync(monitor.Id)).Select(d => d.Date)
            .Should().Equal(Today.AddDays(-6), Today.AddDays(-4), Today.AddDays(-3));
    }

    [Fact]
    public async Task Partitions_are_created_ahead_and_dropped_after_the_longest_retention()
    {
        await engine.WithDbAsync(db => db.Database.ExecuteSqlRawAsync(
            "SELECT ensure_check_results_partition('2020-03-15')"));
        (await PartitionsAsync()).Should().Contain("check_results_2020_03");

        await RunAsync();

        var partitions = await PartitionsAsync();
        var thisMonth = new DateOnly(Today.Year, Today.Month, 1);
        partitions.Should().NotContain("check_results_2020_03").And.Contain("check_results_default");
        partitions.Should().Contain(Enumerable.Range(0, 4)
            .Select(n => $"check_results_{thisMonth.AddMonths(n):yyyy_MM}"));
    }

    [Fact]
    public async Task Only_expired_refresh_tokens_are_removed()
    {
        var account = await engine.AddAccountAsync();
        var user = new ApplicationUser { AccountId = account.Id, UserName = "u", Email = "u@example.com" };
        await engine.SaveAsync(user);
        await engine.SaveAsync(Token("expired", -1), Token("valid", 1));

        await RunAsync();

        (await engine.WithDbAsync(db => db.RefreshTokens.Select(t => t.TokenHash).ToListAsync()))
            .Should().Equal("valid");

        RefreshToken Token(string hash, int days) => new()
        {
            UserId = user.Id,
            FamilyId = Guid.NewGuid(),
            TokenHash = hash,
            SecurityStamp = "s",
            CreatedAt = engine.Time.Now,
            ExpiresAt = engine.Time.Now.AddDays(days),
        };
    }

    private CheckResult Result(UptimeMonitor monitor, int daysAgo, int hour, bool isUp, int? responseMs) => new()
    {
        AccountId = monitor.AccountId,
        MonitorId = monitor.Id,
        CheckedAt = new DateTimeOffset(
            Today.AddDays(-daysAgo).ToDateTime(new TimeOnly(hour, 0)), TimeSpan.Zero),
        IsUp = isUp,
        ResponseMs = responseMs,
    };

    private Task<bool> RunAsync() => engine.WithScopeAsync<RollupJob, bool>(async job =>
    {
        await job.RunAsync(CancellationToken.None);
        return true;
    });

    private Task<List<DailyStat>> DailyAsync(Guid monitorId) => engine.WithDbAsync(db => db.DailyStats
        .IgnoreQueryFilters().AsNoTracking().Where(d => d.MonitorId == monitorId).OrderBy(d => d.Date).ToListAsync());

    private Task<List<string>> PartitionsAsync() => engine.WithDbAsync(db => db.Database
        .SqlQueryRaw<string>(
            "SELECT inhrelid::regclass::text AS \"Value\" FROM pg_inherits WHERE inhparent = 'check_results'::regclass")
        .ToListAsync());
}
