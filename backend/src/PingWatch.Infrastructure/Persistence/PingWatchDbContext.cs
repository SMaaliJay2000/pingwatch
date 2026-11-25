using System.Reflection;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Internal;
using PingWatch.Application;
using PingWatch.Domain;

namespace PingWatch.Infrastructure.Persistence;

public sealed class PingWatchDbContext(DbContextOptions<PingWatchDbContext> options, ITenantContext tenant)
    : IdentityUserContext<ApplicationUser, Guid>(options)
{
    private static readonly MethodInfo ApplyTenantFilterMethod = typeof(PingWatchDbContext)
        .GetMethod(nameof(ApplyTenantFilter), BindingFlags.Instance | BindingFlags.NonPublic)!;

    // Evaluated per context instance by the global filters. Null (no tenant) matches no rows.
    private Guid? CurrentAccountId => tenant.AccountId;

    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<UptimeMonitor> Monitors => Set<UptimeMonitor>();
    public DbSet<CheckResult> CheckResults => Set<CheckResult>();
    public DbSet<DailyStat> DailyStats => Set<DailyStat>();
    public DbSet<Incident> Incidents => Set<Incident>();
    public DbSet<AlertChannel> AlertChannels => Set<AlertChannel>();
    public DbSet<MonitorChannel> MonitorChannels => Set<MonitorChannel>();
    public DbSet<StatusPage> StatusPages => Set<StatusPage>();
    public DbSet<StatusPageMonitor> StatusPageMonitors => Set<StatusPageMonitor>();
    public DbSet<AlertLogEntry> AlertLog => Set<AlertLogEntry>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);

        StoreAsText<PlanTier>(configurationBuilder);
        StoreAsText<SubscriptionStatus>(configurationBuilder);
        StoreAsText<UserRole>(configurationBuilder);
        StoreAsText<MonitorType>(configurationBuilder);
        StoreAsText<MonitorStatus>(configurationBuilder);
        StoreAsText<CheckMethod>(configurationBuilder);
        StoreAsText<KeywordMode>(configurationBuilder);
        StoreAsText<AlertChannelType>(configurationBuilder);
        StoreAsText<AlertType>(configurationBuilder);
    }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        ModelConfiguration.Apply(builder);

        builder.Entity<Account>().HasQueryFilter(a => a.Id == CurrentAccountId);

        // Every ITenantOwned entity is filtered automatically, so a new one can't be forgotten.
        var tenantOwned = builder.Model.GetEntityTypes()
            .Select(t => t.ClrType)
            .Where(typeof(ITenantOwned).IsAssignableFrom)
            .ToList();

        foreach (var type in tenantOwned)
        {
            ApplyTenantFilterMethod.MakeGenericMethod(type).Invoke(this, [builder]);
        }
    }

    private void ApplyTenantFilter<TEntity>(ModelBuilder builder) where TEntity : class, ITenantOwned =>
        builder.Entity<TEntity>().HasQueryFilter(e => e.AccountId == CurrentAccountId);

    private static void StoreAsText<TEnum>(ModelConfigurationBuilder builder) where TEnum : struct, Enum =>
        builder.Properties<TEnum>().HaveConversion<SnakeCaseEnumConverter<TEnum>>().HaveMaxLength(32);
}
