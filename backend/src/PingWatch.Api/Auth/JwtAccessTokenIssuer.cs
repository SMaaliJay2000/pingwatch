using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using PingWatch.Application.Auth;

namespace PingWatch.Api.Auth;

public static class PingWatchClaims
{
    public const string AccountId = "account_id";
}

internal sealed class JwtAccessTokenIssuer(JwtOptions options) : IAccessTokenIssuer
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private static readonly JsonWebTokenHandler Handler = new();

    public AccessTokenResponse Issue(AuthUser user)
    {
        var now = DateTimeOffset.UtcNow;
        var expiresAt = now + Lifetime;

        var token = Handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = options.Issuer,
            Audience = options.Audience,
            IssuedAt = now.UtcDateTime,
            NotBefore = now.UtcDateTime,
            Expires = expiresAt.UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                [JwtRegisteredClaimNames.Sub] = user.Id.ToString(),
                [JwtRegisteredClaimNames.Email] = user.Email,
                [PingWatchClaims.AccountId] = user.AccountId.ToString(),
            },
            SigningCredentials = new SigningCredentials(options.CreateSecurityKey(), SecurityAlgorithms.HmacSha256),
        });

        return new AccessTokenResponse(token, expiresAt);
    }
}
