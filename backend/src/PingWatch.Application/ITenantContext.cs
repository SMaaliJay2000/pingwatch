namespace PingWatch.Application;

public interface ITenantContext
{
    /// <summary>The signed-in account, or null when there is none.</summary>
    Guid? AccountId { get; }
}
