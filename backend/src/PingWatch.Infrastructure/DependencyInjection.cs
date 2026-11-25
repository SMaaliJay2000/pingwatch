using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PingWatch.Application;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Fails closed until M2 registers a tenant context backed by the signed-in user.
        services.TryAddScoped<ITenantContext, NoTenantContext>();

        services.AddDbContext<PingWatchDbContext>(options => options
            .UseNpgsql(connectionString)
            .UseSnakeCaseNamingConvention());

        return services;
    }

    private sealed class NoTenantContext : ITenantContext
    {
        public Guid? AccountId => null;
    }
}
