using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PingWatch.Application.Checks;

namespace PingWatch.Infrastructure.Checks;

public static class CheckEngineRegistration
{
    public static IServiceCollection AddCheckEngine(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<IDnsResolver, SystemDnsResolver>();
        services.AddSingleton(provider => new SsrfGuard(provider.GetRequiredService<IDnsResolver>()));

        // Each check sets its own timeout. The factory's loggers are off: URLs can contain secrets.
        services
            .AddHttpClient(HttpCheckRunner.ClientName, client => client.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(
                provider => CheckHttpHandler.Create(provider.GetRequiredService<SsrfGuard>()))
            .RemoveAllLoggers();

        services.AddTransient<ICheckRunner, HttpCheckRunner>();
        services.AddTransient<ICheckRunner, KeywordCheckRunner>();
        services.AddTransient<ICheckRunner, TcpCheckRunner>();
        services.AddTransient<ICheckRunner, SslCheckRunner>();

        services.AddScoped<ICheckStore, CheckStore>();
        services.AddScoped<IRollupStore, RollupStore>();

        return services;
    }
}