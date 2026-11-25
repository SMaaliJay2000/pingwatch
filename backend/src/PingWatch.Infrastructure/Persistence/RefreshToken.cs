namespace PingWatch.Infrastructure.Persistence;

/// <summary>Only a hash is stored. Not tenant-owned: it is looked up before the caller is known.</summary>
public class RefreshToken
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public Guid UserId { get; set; }

    /// <summary>Shared by every token descended from one login.</summary>
    public Guid FamilyId { get; set; }

    public required string TokenHash { get; set; }

    /// <summary>The user's stamp at login. A password reset changes it, which ends the session.</summary>
    public required string SecurityStamp { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public DateTimeOffset? UsedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}
