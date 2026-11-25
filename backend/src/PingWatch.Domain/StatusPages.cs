namespace PingWatch.Domain;

public class StatusPage : ITenantOwned
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid AccountId { get; set; }
    public required string Slug { get; set; }
    public required string Title { get; set; }
    public string? CustomDomain { get; set; }
    public string? LogoUrl { get; set; }
    public bool IsPublic { get; set; }
}

public class StatusPageMonitor : ITenantOwned
{
    public Guid AccountId { get; set; }
    public Guid StatusPageId { get; set; }
    public Guid MonitorId { get; set; }
    public string? DisplayName { get; set; }
    public int SortOrder { get; set; }
}
