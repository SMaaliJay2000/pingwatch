using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Accounts;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Accounts;

// No account_id conditions here: the global query filters scope every query to the current tenant.
internal sealed class AccountReader(PingWatchDbContext db) : IAccountReader
{
    public Task<Account?> GetCurrentAsync(CancellationToken cancellationToken) =>
        db.Accounts.AsNoTracking().SingleOrDefaultAsync(cancellationToken);

    public Task<int> CountMonitorsAsync(CancellationToken cancellationToken) =>
        db.Monitors.CountAsync(cancellationToken);

    public Task<int> CountStatusPagesAsync(CancellationToken cancellationToken) =>
        db.StatusPages.CountAsync(cancellationToken);
}
