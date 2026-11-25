using PingWatch.Domain;

namespace PingWatch.UnitTests;

/// <summary>The incident lifecycle table from SPEC section 6.</summary>
public class CheckStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    // from, failures before, check passed => to, failures after, transition
    [InlineData(MonitorStatus.Pending, 0, true, MonitorStatus.Up, 0, CheckTransition.NoChange)]
    [InlineData(MonitorStatus.Up, 0, true, MonitorStatus.Up, 0, CheckTransition.NoChange)]
    [InlineData(MonitorStatus.Up, 0, false, MonitorStatus.Up, 1, CheckTransition.RetryScheduled)]
    [InlineData(MonitorStatus.Up, 1, false, MonitorStatus.Down, 2, CheckTransition.WentDown)]
    [InlineData(MonitorStatus.Up, 1, true, MonitorStatus.Up, 0, CheckTransition.NoChange)]
    [InlineData(MonitorStatus.Down, 2, true, MonitorStatus.Up, 0, CheckTransition.Recovered)]
    [InlineData(MonitorStatus.Down, 2, false, MonitorStatus.Down, 3, CheckTransition.NoChange)]
    [InlineData(MonitorStatus.Pending, 0, false, MonitorStatus.Pending, 1, CheckTransition.RetryScheduled)]
    [InlineData(MonitorStatus.Pending, 1, false, MonitorStatus.Down, 2, CheckTransition.WentDown)]
    [InlineData(MonitorStatus.Paused, 0, false, MonitorStatus.Paused, 0, CheckTransition.Ignored)]
    [InlineData(MonitorStatus.Paused, 0, true, MonitorStatus.Paused, 0, CheckTransition.Ignored)]
    public void State_transitions_follow_the_spec(
        MonitorStatus from, int failuresBefore, bool passed,
        MonitorStatus to, int failuresAfter, CheckTransition expected)
    {
        var monitor = new UptimeMonitor { Name = "m", Status = from, ConsecutiveFailures = failuresBefore };

        var transition = monitor.RecordCheck(passed, Now);

        transition.Should().Be(expected);
        monitor.Status.Should().Be(to);
        monitor.ConsecutiveFailures.Should().Be(failuresAfter);
    }

    [Fact]
    public void First_failure_schedules_a_retry_in_20_seconds()
    {
        var monitor = new UptimeMonitor { Name = "m", Status = MonitorStatus.Up, NextCheckAt = Now.AddHours(1) };

        monitor.RecordCheck(false, Now);

        monitor.NextCheckAt.Should().Be(Now.AddSeconds(20));
        monitor.LastCheckedAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(MonitorStatus.Up, 1, false)]
    [InlineData(MonitorStatus.Down, 2, false)]
    [InlineData(MonitorStatus.Up, 0, true)]
    public void Other_results_leave_the_schedule_alone(MonitorStatus status, int failures, bool passed)
    {
        var monitor = new UptimeMonitor
        {
            Name = "m",
            Status = status,
            ConsecutiveFailures = failures,
            NextCheckAt = Now.AddHours(1),
        };

        monitor.RecordCheck(passed, Now);

        monitor.NextCheckAt.Should().Be(Now.AddHours(1));
    }
}
