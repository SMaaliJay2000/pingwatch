using System.Linq.Expressions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using PingWatch.Domain;

namespace PingWatch.Infrastructure.Persistence;

internal static class ModelConfiguration
{
    public static void Apply(ModelBuilder builder)
    {
        ConfigureAccounts(builder);
        ConfigureMonitoring(builder);
        ConfigureAlerting(builder);
        ConfigureStatusPages(builder);
    }

    private static void ConfigureAccounts(ModelBuilder builder)
    {
        builder.Entity<Account>(e =>
        {
            e.ToTable("accounts");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.BillingCustomerId).HasMaxLength(100);
            e.HasIndex(x => x.BillingCustomerId).IsUnique();
        });

        builder.Entity<ApplicationUser>(e =>
        {
            e.ToTable("users");
            e.Property(x => x.EmailConfirmed).HasColumnName("email_verified");
            e.HasIndex(x => x.NormalizedEmail).IsUnique().HasDatabaseName("ix_users_normalized_email");
            e.HasIndex(x => x.NormalizedUserName).HasDatabaseName("ix_users_normalized_user_name");
            OwnedByAccount(e, x => x.AccountId);
        });

        builder.Entity<IdentityUserClaim<Guid>>().ToTable("user_claims");
        builder.Entity<IdentityUserLogin<Guid>>().ToTable("user_logins");
        builder.Entity<IdentityUserToken<Guid>>().ToTable("user_tokens");
    }

    private static void ConfigureMonitoring(ModelBuilder builder)
    {
        builder.Entity<UptimeMonitor>(e =>
        {
            e.ToTable("monitors");
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Url).HasMaxLength(2048);
            e.Property(x => x.Host).HasMaxLength(253);
            e.Property(x => x.ExpectedStatus).HasMaxLength(50);
            e.Property(x => x.Keyword).HasMaxLength(500);
            e.HasIndex(x => x.NextCheckAt);
            OwnedByAccount(e, x => x.AccountId);
        });

        // Names are explicit because the PartitionCheckResults migration repeats them in raw SQL.
        builder.Entity<CheckResult>(e =>
        {
            e.ToTable("check_results");
            // A partitioned table's primary key must include the partition column.
            e.HasKey(x => new { x.Id, x.CheckedAt }).HasName("pk_check_results");
            e.Property(x => x.Id).ValueGeneratedOnAdd().UseSerialColumn();
            e.Property(x => x.Region).HasMaxLength(32);
            e.HasIndex(x => new { x.MonitorId, x.CheckedAt })
                .HasDatabaseName("ix_check_results_monitor_id_checked_at");
            // ponytail: monitor_id-only FK avoids a second index on the high-volume table.
            // account_id is only written by the worker, copied from the monitor.
            e.HasOne<UptimeMonitor>().WithMany()
                .HasForeignKey(x => x.MonitorId)
                .HasConstraintName("fk_check_results_monitors_monitor_id")
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<DailyStat>(e =>
        {
            e.ToTable("daily_stats");
            e.HasKey(x => new { x.MonitorId, x.Date });
            e.Property(x => x.UptimePct).HasPrecision(6, 3);
            OwnedByParent<DailyStat, UptimeMonitor>(e, x => new { x.MonitorId, x.AccountId });
        });

        builder.Entity<Incident>(e =>
        {
            e.ToTable("incidents");
            e.HasIndex(x => x.MonitorId)
                .IsUnique()
                .HasFilter("resolved_at IS NULL")
                .HasDatabaseName("ix_incidents_one_open_per_monitor");
            OwnedByParent<Incident, UptimeMonitor>(e, x => new { x.MonitorId, x.AccountId });
        });
    }

    private static void ConfigureAlerting(ModelBuilder builder)
    {
        builder.Entity<AlertChannel>(e =>
        {
            e.ToTable("alert_channels");
            OwnedByAccount(e, x => x.AccountId);
        });

        builder.Entity<MonitorChannel>(e =>
        {
            e.ToTable("monitor_channels");
            e.HasKey(x => new { x.MonitorId, x.ChannelId });
            OwnedByParent<MonitorChannel, UptimeMonitor>(e, x => new { x.MonitorId, x.AccountId });
            OwnedByParent<MonitorChannel, AlertChannel>(e, x => new { x.ChannelId, x.AccountId });
        });

        builder.Entity<AlertLogEntry>(e =>
        {
            e.ToTable("alert_log");
            OwnedByParent<AlertLogEntry, UptimeMonitor>(e, x => new { x.MonitorId, x.AccountId });
            OwnedByParent<AlertLogEntry, AlertChannel>(e, x => new { x.ChannelId, x.AccountId });
            OwnedByParent<AlertLogEntry, Incident>(e, x => new { x.IncidentId, x.AccountId });
        });
    }

    private static void ConfigureStatusPages(ModelBuilder builder)
    {
        builder.Entity<StatusPage>(e =>
        {
            e.ToTable("status_pages");
            e.Property(x => x.Slug).HasMaxLength(63);
            e.Property(x => x.Title).HasMaxLength(200);
            e.Property(x => x.CustomDomain).HasMaxLength(253);
            e.Property(x => x.LogoUrl).HasMaxLength(2048);
            e.HasIndex(x => x.Slug).IsUnique();
            e.HasIndex(x => x.CustomDomain).IsUnique();
            OwnedByAccount(e, x => x.AccountId);
        });

        builder.Entity<StatusPageMonitor>(e =>
        {
            e.ToTable("status_page_monitors");
            e.HasKey(x => new { x.StatusPageId, x.MonitorId });
            e.Property(x => x.DisplayName).HasMaxLength(200);
            OwnedByParent<StatusPageMonitor, StatusPage>(e, x => new { x.StatusPageId, x.AccountId });
            OwnedByParent<StatusPageMonitor, UptimeMonitor>(e, x => new { x.MonitorId, x.AccountId });
        });
    }

    private static void OwnedByAccount<TChild>(
        EntityTypeBuilder<TChild> child,
        Expression<Func<TChild, object?>> accountId)
        where TChild : class =>
        child.HasOne<Account>().WithMany()
            .HasForeignKey(accountId)
            .OnDelete(DeleteBehavior.Cascade);

    // (parent_id, account_id) must match the parent row, so rows can't link across accounts.
    private static void OwnedByParent<TChild, TParent>(
        EntityTypeBuilder<TChild> child,
        Expression<Func<TChild, object?>> parentIdAndAccountId)
        where TChild : class
        where TParent : class =>
        child.HasOne<TParent>().WithMany()
            .HasForeignKey(parentIdAndAccountId)
            .HasPrincipalKey("Id", "AccountId")
            .OnDelete(DeleteBehavior.Cascade);
}
