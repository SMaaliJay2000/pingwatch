using Microsoft.EntityFrameworkCore;
using PingWatch.Application;
using PingWatch.Application.Monitors;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Monitors;

// Apart from the lock, every query is scoped to the tenant by the global query filters.
internal sealed class MonitorStore(PingWatchDbContext db, ITenantContext tenant) : IMonitorStore
{
    public async Task<IReadOnlyList<UptimeMonitor>> ListAsync(CancellationToken cancellationToken) =>
        await db.Monitors.AsNoTracking()
            .OrderBy(m => m.CreatedAt).ThenBy(m => m.Id)
            .ToListAsync(cancellationToken);

    public Task<UptimeMonitor?> FindAsync(Guid id, CancellationToken cancellationToken) =>
        db.Monitors.SingleOrDefaultAsync(m => m.Id == id, cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        db.Monitors.CountAsync(cancellationToken);

    public Task<int> CountActiveAsync(CancellationToken cancellationToken) =>
        db.Monitors.CountAsync(m => m.Status != MonitorStatus.Paused, cancellationToken);

    public Task<Incident?> FindOpenIncidentAsync(Guid monitorId, CancellationToken cancellationToken) =>
        db.Incidents.SingleOrDefaultAsync(
            i => i.MonitorId == monitorId && i.ResolvedAt == null, cancellationToken);

    public void Add(UptimeMonitor monitor) => db.Monitors.Add(monitor);

    public async Task<bool> DeleteAsync(Guid id, CancellationToken cancellationToken) =>
        await db.Monitors.Where(m => m.Id == id).ExecuteDeleteAsync(cancellationToken) > 0;

    public Task SaveAsync(CancellationToken cancellationToken) => db.SaveChangesAsync(cancellationToken);

    public async Task<T> WithAccountLockAsync<T>(Func<Task<T>> action, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // NO KEY UPDATE serialises these requests without blocking inserts that reference the account.
        await db.Database.ExecuteSqlAsync(
            $"SELECT 1 FROM accounts WHERE id = {tenant.AccountId} FOR NO KEY UPDATE", cancellationToken);

        var result = await action();
        await transaction.CommitAsync(cancellationToken);

        return result;
    }
}