using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using PingWatch.Application.Auth;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure.Auth;

// ponytail: used and expired rows are never deleted; purge them from the M4 nightly job.
internal sealed class RefreshTokenStore(PingWatchDbContext db) : IRefreshTokenStore
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(14);

    public Task<IssuedRefreshToken> IssueAsync(AuthUser user, CancellationToken cancellationToken) =>
        AddAsync(user.Id, Guid.NewGuid(), user.SecurityStamp, cancellationToken);

    public async Task<RotatedRefreshToken?> RotateAsync(string token, CancellationToken cancellationToken)
    {
        var hash = Hash(token);
        var now = DateTimeOffset.UtcNow;

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);

        // One conditional UPDATE, so two simultaneous requests can't both consume the token.
        var consumed = await db.RefreshTokens
            .Where(t => t.TokenHash == hash && t.UsedAt == null && t.RevokedAt == null && t.ExpiresAt > now)
            .ExecuteUpdateAsync(s => s.SetProperty(t => t.UsedAt, now), cancellationToken);

        var current = await db.RefreshTokens
            .AsNoTracking()
            .SingleOrDefaultAsync(t => t.TokenHash == hash, cancellationToken);

        if (current is null)
        {
            return null;
        }

        if (consumed == 0)
        {
            // A used token coming back means it was copied: end the whole session.
            if (current.UsedAt is not null)
            {
                await db.RefreshTokens
                    .Where(t => t.FamilyId == current.FamilyId && t.RevokedAt == null)
                    .ExecuteUpdateAsync(s => s.SetProperty(t => t.RevokedAt, now), cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }

            return null;
        }

        var stampStillValid = await db.Users.AnyAsync(
            u => u.Id == current.UserId && u.SecurityStamp == current.SecurityStamp, cancellationToken);

        if (!stampStillValid)
        {
            return null;
        }

        var next = await AddAsync(current.UserId, current.FamilyId, current.SecurityStamp, cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RotatedRefreshToken(current.UserId, next);
    }

    private async Task<IssuedRefreshToken> AddAsync(
        Guid userId, Guid familyId, string securityStamp, CancellationToken cancellationToken)
    {
        var token = Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + Lifetime;

        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = userId,
            FamilyId = familyId,
            TokenHash = Hash(token),
            SecurityStamp = securityStamp,
            CreatedAt = now,
            ExpiresAt = expiresAt,
        });
        await db.SaveChangesAsync(cancellationToken);

        return new IssuedRefreshToken(token, expiresAt);
    }

    private static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
}
