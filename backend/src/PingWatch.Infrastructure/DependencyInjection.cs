using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PingWatch.Application;
using PingWatch.Application.Accounts;
using PingWatch.Application.Auth;
using PingWatch.Application.Email;
using PingWatch.Infrastructure.Accounts;
using PingWatch.Infrastructure.Auth;
using PingWatch.Infrastructure.Email;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Fails closed unless the host registered its own tenant context first.
        services.TryAddScoped<ITenantContext, NoTenantContext>();

        services.AddDbContext<PingWatchDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return services;
    }

    // Separate because these need ASP.NET Core Identity, which only the API registers.
    public static IServiceCollection AddAccountServices(this IServiceCollection services)
    {
        services.AddScoped<IUserAccounts, UserAccounts>();
        services.AddScoped<IRefreshTokenStore, RefreshTokenStore>();
        services.AddScoped<IAccountReader, AccountReader>();

        return services;
    }

    public static IServiceCollection AddSmtpEmail(this IServiceCollection services, SmtpOptions options)
    {
        services.AddSingleton(options);
        services.AddSingleton<IEmailSender, SmtpEmailSender>();

        return services;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid? AccountId => null;
    }
}
