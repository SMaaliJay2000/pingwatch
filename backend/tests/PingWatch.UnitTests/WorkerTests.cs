using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Worker;

namespace PingWatch.UnitTests;

public class WorkerTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Scheduler_is_ready_only_after_a_recent_cycle()
    {
        var time = new ManualTime(Start);
        var health = new SchedulerHealth(time);

        health.IsReady.Should().BeFalse("no cycle has run yet");

        health.RecordCycle();
        time.Advance(60);
        health.IsReady.Should().BeTrue();

        time.Advance(1);
        health.IsReady.Should().BeFalse();
    }

    [Fact]
    public void A_missed_heartbeat_is_reported_after_60_seconds_then_once_a_minute()
    {
        var time = new ManualTime(Start);
        var health = new SchedulerHealth(time);

        // Also covers a scheduler that never completes its first cycle.
        time.Advance(60);
        health.ShouldReportStall().Should().BeFalse();
        time.Advance(1);
        health.ShouldReportStall().Should().BeTrue();
        time.Advance(10);
        health.ShouldReportStall().Should().BeFalse();
        time.Advance(50);
        health.ShouldReportStall().Should().BeTrue();

        health.RecordCycle();
        health.ShouldReportStall().Should().BeFalse();
        time.Advance(61);
        health.ShouldReportStall().Should().BeTrue("a new stall is reported straight away");
    }

    [Theory]
    [InlineData("2026-11-25T00:00:00Z", 5)]
    [InlineData("2026-11-25T00:05:00Z", 24 * 60)]
    [InlineData("2026-11-25T12:00:00Z", 12 * 60 + 5)]
    [InlineData("2026-11-25T23:59:00Z", 6)]
    public void Rollup_runs_just_after_midnight_utc(string now, int minutes)
    {
        RollupService.DelayUntilNextRun(DateTimeOffset.Parse(now)).Should().Be(TimeSpan.FromMinutes(minutes));
    }

    [Fact]
    public async Task Nothing_is_purged_when_aggregation_fails()
    {
        var store = Substitute.For<IRollupStore>();
        store.AggregateBeforeAsync(default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        var run = () => Job(store).RunAsync(CancellationToken.None);

        await run.Should().ThrowAsync<InvalidOperationException>();
        await store.DidNotReceiveWithAnyArgs().PurgeRawResultsAsync(default, default);
    }

    [Fact]
    public async Task A_failing_partition_does_not_stop_the_rollup()
    {
        var store = Substitute.For<IRollupStore>();
        store.EnsurePartitionAsync(default, default).ThrowsAsyncForAnyArgs(new InvalidOperationException("boom"));

        await Job(store).RunAsync(CancellationToken.None);

        await store.ReceivedWithAnyArgs(4).EnsurePartitionAsync(default, default);
        await store.Received(1).EnsurePartitionAsync(new DateOnly(2027, 2, 1), Arg.Any<CancellationToken>());
        await store.Received(1).PurgeRawResultsAsync(new DateOnly(2026, 11, 25), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void A_job_is_stale_when_the_monitor_was_edited_but_not_when_only_the_timeout_changed()
    {
        var monitor = new UptimeMonitor { Name = "m", Type = MonitorType.Http, Url = "https://a.example" };
        var job = CheckJob.From(monitor);

        monitor.Name = "renamed";
        monitor.IntervalSec = 900;
        monitor.TimeoutMs = 2_000;
        job.IsStaleFor(monitor).Should().BeFalse();

        monitor.Url = "https://b.example";
        job.IsStaleFor(monitor).Should().BeTrue();
    }

    private static RollupJob Job(IRollupStore store) =>
        new(store, new ManualTime(Start), NullLogger<RollupJob>.Instance);

    private sealed class ManualTime(DateTimeOffset start) : TimeProvider
    {
        private DateTimeOffset _now = start;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(int seconds) => _now = _now.AddSeconds(seconds);
    }
}
