using System.Text;
using Microsoft.IdentityModel.Tokens;

namespace PingWatch.Api.Auth;

public sealed class JwtOptions
{
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public string SigningKey { get; init; } = "";

    public SymmetricSecurityKey CreateSecurityKey() => new(Encoding.UTF8.GetBytes(SigningKey));

    public static JwtOptions From(IConfiguration configuration)
    {
        var options = configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

        if (options.Issuer.Length == 0 || options.Audience.Length == 0)
        {
            throw new InvalidOperationException("Jwt:Issuer and Jwt:Audience must be configured.");
        }

        if (Encoding.UTF8.GetByteCount(options.SigningKey) < 32)
        {
            throw new InvalidOperationException(
                "Jwt:SigningKey must be at least 32 bytes. Use 'dotnet user-secrets' locally "
                + "or the Jwt__SigningKey environment variable.");
        }

        return options;
    }
}
