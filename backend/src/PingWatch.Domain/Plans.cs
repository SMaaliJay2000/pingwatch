using System.Collections.Frozen;

namespace PingWatch.Domain;

public sealed record PlanLimits(
    int MaxMonitors,
    int MinIntervalSec,
    int RetentionDays,
    int MaxStatusPages,
    bool AllowsCustomDomain,
    FrozenSet<AlertChannelType> AllowedChannels)
{
    public bool CanAddMonitor(int currentCount) => currentCount < MaxMonitors;

    public bool AllowsInterval(int intervalSec) => intervalSec >= MinIntervalSec;

    public bool CanAddStatusPage(int currentCount) => currentCount < MaxStatusPages;

    public bool AllowsChannel(AlertChannelType type) => AllowedChannels.Contains(type);
}

/// <summary>Plan limits from SPEC section 3.</summary>
public static class Plans
{
    public static PlanLimits Free { get; } = new(
        MaxMonitors: 5,
        MinIntervalSec: 300,
        RetentionDays: 7,
        MaxStatusPages: 1,
        AllowsCustomDomain: false,
        AllowedChannels: new[] { AlertChannelType.Email }.ToFrozenSet());

    public static PlanLimits Starter { get; } = new(
        MaxMonitors: 25,
        MinIntervalSec: 60,
        RetentionDays: 90,
        MaxStatusPages: 3,
        AllowsCustomDomain: false,
        AllowedChannels: new[]
        {
            AlertChannelType.Email,
            AlertChannelType.Webhook,
            AlertChannelType.Slack,
            AlertChannelType.Discord,
        }.ToFrozenSet());

    public static PlanLimits Pro { get; } = new(
        MaxMonitors: 100,
        MinIntervalSec: 30,
        RetentionDays: 365,
        MaxStatusPages: 10,
        AllowsCustomDomain: true,
        AllowedChannels: Enum.GetValues<AlertChannelType>().ToFrozenSet());

    public static PlanLimits For(PlanTier tier) => tier switch
    {
        PlanTier.Free => Free,
        PlanTier.Starter => Starter,
        PlanTier.Pro => Pro,
        _ => throw new ArgumentOutOfRangeException(nameof(tier), tier, null),
    };
}
