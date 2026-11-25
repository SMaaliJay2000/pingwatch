using PingWatch.Domain;

namespace PingWatch.UnitTests;

public class PlanTests
{
    [Theory]
    [InlineData(PlanTier.Free, 5, 300, 7, 1, false)]
    [InlineData(PlanTier.Starter, 25, 60, 90, 3, false)]
    [InlineData(PlanTier.Pro, 100, 30, 365, 10, true)]
    public void Limits_match_the_spec(
        PlanTier tier, int monitors, int interval, int retention, int statusPages, bool customDomain)
    {
        var limits = Plans.For(tier);

        limits.MaxMonitors.Should().Be(monitors);
        limits.MinIntervalSec.Should().Be(interval);
        limits.RetentionDays.Should().Be(retention);
        limits.MaxStatusPages.Should().Be(statusPages);
        limits.AllowsCustomDomain.Should().Be(customDomain);
    }

    [Theory]
    [InlineData(PlanTier.Free)]
    [InlineData(PlanTier.Starter)]
    [InlineData(PlanTier.Pro)]
    public void Count_and_interval_limits_hold_at_the_boundary(PlanTier tier)
    {
        var limits = Plans.For(tier);

        limits.CanAddMonitor(limits.MaxMonitors - 1).Should().BeTrue();
        limits.CanAddMonitor(limits.MaxMonitors).Should().BeFalse();
        limits.CanAddStatusPage(limits.MaxStatusPages - 1).Should().BeTrue();
        limits.CanAddStatusPage(limits.MaxStatusPages).Should().BeFalse();
        limits.AllowsInterval(limits.MinIntervalSec).Should().BeTrue();
        limits.AllowsInterval(limits.MinIntervalSec - 1).Should().BeFalse();
    }

    [Fact]
    public void Free_allows_email_only()
    {
        Plans.Free.AllowedChannels.Should().BeEquivalentTo([AlertChannelType.Email]);
    }

    [Fact]
    public void Starter_adds_webhook_slack_and_discord()
    {
        Plans.Starter.AllowedChannels.Should().BeEquivalentTo(
        [
            AlertChannelType.Email,
            AlertChannelType.Webhook,
            AlertChannelType.Slack,
            AlertChannelType.Discord,
        ]);
    }

    [Fact]
    public void Pro_allows_every_channel()
    {
        Enum.GetValues<AlertChannelType>().Should().OnlyContain(type => Plans.Pro.AllowsChannel(type));
    }

    [Fact]
    public void Unknown_tier_is_rejected()
    {
        var act = () => Plans.For((PlanTier)99);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
