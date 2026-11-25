namespace PingWatch.Domain;

/// <summary>A row that belongs to one account. The DbContext filters these by tenant.</summary>
public interface ITenantOwned
{
    Guid AccountId { get; }
}

public class Account
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public required string Name { get; set; }
    public PlanTier Plan { get; set; } = PlanTier.Free;
    public string? BillingCustomerId { get; set; }
    public SubscriptionStatus SubscriptionStatus { get; set; } = SubscriptionStatus.None;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
