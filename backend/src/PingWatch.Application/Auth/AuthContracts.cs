namespace PingWatch.Application.Auth;

// Requests and responses.
public sealed record RegisterRequest(string Email, string Password, string? AccountName = null);

public sealed record VerifyEmailRequest(Guid UserId, string Token);

public sealed record LoginRequest(string Email, string Password);

public sealed record ForgotPasswordRequest(string Email);

public sealed record ResetPasswordRequest(string Email, string Token, string NewPassword);

public sealed record AccessTokenResponse(string AccessToken, DateTimeOffset ExpiresAt);

public sealed record MessageResponse(string Message);

// Results.
public sealed record AuthUser(Guid Id, Guid AccountId, string Email, bool EmailVerified, string SecurityStamp);

public sealed record IssuedRefreshToken(string Token, DateTimeOffset ExpiresAt);

public sealed record RotatedRefreshToken(Guid UserId, IssuedRefreshToken Next);

public sealed record AuthSession(AccessTokenResponse AccessToken, IssuedRefreshToken RefreshToken);

public enum LoginFailure { InvalidCredentials, EmailNotVerified }

public sealed record LoginResult(AuthSession? Session, LoginFailure? Failure);

/// <summary>Where the web app lives; used to build the links sent by email.</summary>
public sealed record AuthLinkOptions(string AppBaseUrl);
