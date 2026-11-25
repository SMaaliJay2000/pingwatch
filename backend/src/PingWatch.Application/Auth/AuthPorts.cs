namespace PingWatch.Application.Auth;

public interface IUserAccounts
{
    /// <summary>Creates a Free account and its owner. Returns null when the email is already registered.</summary>
    Task<AuthUser?> CreateOwnerAsync(
        string email, string password, string accountName, CancellationToken cancellationToken);

    Task<AuthUser?> FindByEmailAsync(string email, CancellationToken cancellationToken);

    Task<AuthUser?> FindByIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> CheckPasswordAsync(Guid userId, string password, CancellationToken cancellationToken);

    Task<string> CreateEmailVerificationTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> VerifyEmailAsync(Guid userId, string token, CancellationToken cancellationToken);

    Task<string> CreatePasswordResetTokenAsync(Guid userId, CancellationToken cancellationToken);

    Task<bool> ResetPasswordAsync(
        Guid userId, string token, string newPassword, CancellationToken cancellationToken);
}

public interface IRefreshTokenStore
{
    /// <summary>Starts a new token family, bound to the user's current security stamp.</summary>
    Task<IssuedRefreshToken> IssueAsync(AuthUser user, CancellationToken cancellationToken);

    /// <summary>Consumes a token and returns its replacement, or null if it can't be used.</summary>
    Task<RotatedRefreshToken?> RotateAsync(string token, CancellationToken cancellationToken);
}

public interface IAccessTokenIssuer
{
    AccessTokenResponse Issue(AuthUser user);
}
