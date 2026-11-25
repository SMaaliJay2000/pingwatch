using System.Globalization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using PingWatch.Application;
using PingWatch.Application.Auth;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Api.Auth;

public static class RateLimitPolicies
{
    public const string Login = "login";

    /// <summary>For anonymous endpoints that send email, to stop inbox flooding.</summary>
    public const string AuthEmail = "auth-email";
}

public static class AuthSetup
{
    public static IServiceCollection AddPingWatchAuth(
        this IServiceCollection services, IConfiguration configuration)
    {
        var jwt = JwtOptions.From(configuration);

        services.AddSingleton(jwt);
        services.AddSingleton<IAccessTokenIssuer, JwtAccessTokenIssuer>();
        services.AddHttpContextAccessor();
        services.AddScoped<ITenantContext, HttpTenantContext>();

        services.AddIdentityCore<ApplicationUser>(options =>
        {
            options.User.RequireUniqueEmail = true;
            // The user name is the email address, so don't restrict its characters.
            options.User.AllowedUserNameCharacters = string.Empty;
            // Length over composition rules (NIST SP 800-63B).
            options.Password.RequiredLength = PasswordRules.MinLength;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
        })
            .AddEntityFrameworkStores<PingWatchDbContext>()
            .AddDefaultTokenProviders();

        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.MapInboundClaims = false;
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidIssuer = jwt.Issuer,
                    ValidAudience = jwt.Audience,
                    IssuerSigningKey = jwt.CreateSecurityKey(),
                    ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                    ClockSkew = TimeSpan.FromSeconds(30),
                };
            });

        // Secure by default: an endpoint is public only if it says so.
        services.AddAuthorizationBuilder()
            .SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build());

        services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.OnRejected = (context, _) =>
            {
                if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
                {
                    context.HttpContext.Response.Headers.RetryAfter =
                        ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
                }

                return ValueTask.CompletedTask;
            };

            options.AddPolicy(RateLimitPolicies.Login, context => PerIpPerMinute(context, 5));
            options.AddPolicy(RateLimitPolicies.AuthEmail, context => PerIpPerMinute(context, 5));
        });

        return services;
    }

    // ponytail: in-memory per instance. Behind Caddy, RemoteIpAddress is the proxy,
    // so M9 must add forwarded headers or all users will share one limit.
    private static RateLimitPartition<string> PerIpPerMinute(HttpContext context, int permits) =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions { PermitLimit = permits, Window = TimeSpan.FromMinutes(1) });
}
