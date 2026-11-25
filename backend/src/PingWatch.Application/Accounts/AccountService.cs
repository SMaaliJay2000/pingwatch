using PingWatch.Domain;

namespace PingWatch.Application.Accounts;

public sealed record AccountResponse(
    Guid Id, string Name, PlanTier Plan, SubscriptionStatus SubscriptionStatus, DateTimeOffset CreatedAt);

public sealed record QuotaUsage(int Used, int Limit);

public sealed record UsageResponse(
    PlanTier Plan,
    QuotaUsage Monitors,
    QuotaUsage StatusPages,
    int MinIntervalSec,
    int RetentionDays,
    bool CustomDomain,
    IReadOnlyList<AlertChannelType> AlertChannels);

/// <summary>Reads for the current tenant only.</summary>
public interface IAccountReader
{
    Task<Account?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<int> CountMonitorsAsync(CancellationToken cancellationToken);

    Task<int> CountStatusPagesAsync(CancellationToken cancellationToken);
}

public sealed class AccountService(IAccountReader reader)
{
    public async Task<AccountResponse?> GetAsync(CancellationToken cancellationToken)
    {
        var account = await reader.GetCurrentAsync(cancellationToken);

        return account is null
            ? null
            : new AccountResponse(
                account.Id, account.Name, account.Plan, account.SubscriptionStatus, account.CreatedAt);
    }

    public async Task<UsageResponse?> GetUsageAsync(CancellationToken cancellationToken)
    {
        var account = await reader.GetCurrentAsync(cancellationToken);
        if (account is null)
        {
            return null;
        }

        var limits = Plans.For(account.Plan);

        return new UsageResponse(
            account.Plan,
            new QuotaUsage(await reader.CountMonitorsAsync(cancellationToken), limits.MaxMonitors),
            new QuotaUsage(await reader.CountStatusPagesAsync(cancellationToken), limits.MaxStatusPages),
            limits.MinIntervalSec,
            limits.RetentionDays,
            limits.AllowsCustomDomain,
            [.. limits.AllowedChannels.Order()]);
    }
}
