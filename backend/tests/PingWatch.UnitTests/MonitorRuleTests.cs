using PingWatch.Domain;

namespace PingWatch.UnitTests;

public class MonitorRuleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 12, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("200-399", 200, true)]
    [InlineData("200-399", 399, true)]
    [InlineData("200-399", 400, false)]
    [InlineData("200-399", 199, false)]
    [InlineData("200", 200, true)]
    [InlineData("200", 201, false)]
    [InlineData("200, 301-302", 302, true)]
    [InlineData("200, 301-302", 250, false)]
    public void Status_code_ranges_match_inclusively(string text, int code, bool expected)
    {
        StatusCodeRanges.TryParse(text, out var ranges).Should().BeTrue();

        ranges!.Contains(code).Should().Be(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("200-")]
    [InlineData("-200")]
    [InlineData("200,")]
    [InlineData("399-200")]
    [InlineData("99")]
    [InlineData("600")]
    [InlineData("200-300-400")]
    [InlineData("+200")]
    public void Bad_status_code_ranges_are_rejected(string? text)
    {
        StatusCodeRanges.TryParse(text, out _).Should().BeFalse();
    }

    [Fact]
    public void Uptime_is_unknown_without_checks()
    {
        Uptime.Percent(0, 0).Should().BeNull();
    }

    [Theory]
    [InlineData(4, 0, 100)]
    [InlineData(4, 1, 75)]
    [InlineData(4, 4, 0)]
    [InlineData(3, 1, 66.666)]
    [InlineData(1_000_000, 1, 99.999)]
    public void Uptime_is_truncated_so_a_failure_never_shows_as_100(long checks, long failures, decimal expected)
    {
        Uptime.Percent(checks, failures).Should().Be(expected);
    }

    [Fact]
    public void Pausing_stops_the_monitor_and_resets_failures_once()
    {
        var monitor = NewMonitor(MonitorStatus.Down);
        monitor.ConsecutiveFailures = 3;

        monitor.Pause().Should().BeTrue();
        monitor.Pause().Should().BeFalse();

        monitor.Status.Should().Be(MonitorStatus.Paused);
        monitor.ConsecutiveFailures.Should().Be(0);
    }

    [Fact]
    public void Resuming_makes_a_paused_monitor_pending_and_due()
    {
        var monitor = NewMonitor(MonitorStatus.Paused);
        monitor.NextCheckAt = Now.AddDays(-3);

        monitor.Resume(Now).Should().BeTrue();

        monitor.Status.Should().Be(MonitorStatus.Pending);
        monitor.NextCheckAt.Should().Be(Now);
    }

    [Theory]
    [InlineData(MonitorStatus.Up)]
    [InlineData(MonitorStatus.Down)]
    [InlineData(MonitorStatus.Pending)]
    public void Resuming_a_monitor_that_is_not_paused_changes_nothing(MonitorStatus status)
    {
        var monitor = NewMonitor(status);
        monitor.NextCheckAt = Now.AddMinutes(5);

        monitor.Resume(Now).Should().BeFalse();

        monitor.Status.Should().Be(status);
        monitor.NextCheckAt.Should().Be(Now.AddMinutes(5));
    }

    [Fact]
    public void Resolving_an_incident_records_its_duration_once()
    {
        var incident = new Incident { StartedAt = Now.AddSeconds(-90), Cause = "HTTP 503" };

        incident.Resolve(Now);
        incident.Resolve(Now.AddHours(1));

        incident.ResolvedAt.Should().Be(Now);
        incident.DurationSec.Should().Be(90);
    }

    private static UptimeMonitor NewMonitor(MonitorStatus status) => new() { Name = "m", Status = status };
}
