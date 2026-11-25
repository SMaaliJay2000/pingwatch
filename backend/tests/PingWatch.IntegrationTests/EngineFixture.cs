using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PingWatch.Application.Checks;
using PingWatch.Domain;
using PingWatch.Infrastructure;
using PingWatch.Infrastructure.Checks;
using PingWatch.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PingWatch.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class EngineCollection : ICollectionFixture<EngineFixture>
{
    public const string Name = "engine";
}

/// <summary>
/// The check engine's database side as the worker wires it: no tenant. It has its own
/// PostgreSQL, because claiming due monitors would otherwise grab other tests' data.
/// </summary>
public sealed class EngineFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();
    private ServiceProvider _services = null!;

    public FixedTime Time { get; } = new();

    public CapturingPublisher Events { get; } = new();

    public string ConnectionString => _postgres.GetConnectionString();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddInfrastructure(ConnectionString).AddCheckEngine();
        services.AddSingleton<IDomainEventPublisher>(Events);
        services.AddScoped<CheckResultProcessor>();
        services.AddScoped<RollupJob>();
        _services = services.BuildServiceProvider(validateScopes: true);

        await WithDbAsync(async db =>
        {
            await db.Database.MigrateAsync();
            return 0;
        });
    }

    public async Task DisposeAsync()
    {
        await _services.DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>An empty database and the clock set to now.</summary>
    public async Task ResetAsync()
    {
        Time.Reset();
        Events.All.Clear();
        await WithDbAsync(db => db.Accounts.IgnoreQueryFilters().ExecuteDeleteAsync());
    }

    public async Task<T> WithScopeAsync<TService, T>(Func<TService, Task<T>> action) where TService : notnull
    {
        await using var scope = _services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public Task<T> WithDbAsync<T>(Func<PingWatchDbContext, Task<T>> action) => WithScopeAsync(action);

    public async Task<Account> AddAccountAsync(PlanTier plan = PlanTier.Free)
    {
        var account = new Account { Name = "a", Plan = plan };
        await SaveAsync(account);
        return account;
    }

    public async Task<UptimeMonitor> AddMonitorAsync(Account account, Action<UptimeMonitor>? change = null)
    {
        var monitor = new UptimeMonitor
        {
            AccountId = account.Id,
            Name = "m",
            Type = MonitorType.Http,
            Url = "https://example.com",
            Method = CheckMethod.Get,
            NextCheckAt = Time.Now.AddSeconds(-1),
        };
        change?.Invoke(monitor);
        await SaveAsync(monitor);
        return monitor;
    }

    public Task SaveAsync(params object[] entities) => WithDbAsync(async db =>
    {
        db.AddRange(entities);
        return await db.SaveChangesAsync();
    });

    public Task<UptimeMonitor> ReloadAsync(Guid monitorId) => WithDbAsync(
        db => db.Monitors.IgnoreQueryFilters().SingleAsync(m => m.Id == monitorId));
}

public sealed class FixedTime : TimeProvider
{
    public DateTimeOffset Now { get; set; }

    public FixedTime() => Reset();

    // Whole milliseconds, because PostgreSQL keeps less precision than .NET.
    public void Reset() =>
        Now = DateTimeOffset.FromUnixTimeMilliseconds(DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());

    public override DateTimeOffset GetUtcNow() => Now;
}

public sealed class CapturingPublisher : IDomainEventPublisher
{
    public ConcurrentQueue<IDomainEvent> All { get; } = new();

    public ValueTask PublishAsync(IDomainEvent domainEvent, CancellationToken cancellationToken)
    {
        All.Enqueue(domainEvent);
        return ValueTask.CompletedTask;
    }
}
