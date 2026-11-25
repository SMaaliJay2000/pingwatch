using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PingWatch.Application.Email;
using PingWatch.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace PingWatch.IntegrationTests;

[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "api";
}

/// <summary>The real API against a throwaway PostgreSQL, with email captured in memory.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string AppBaseUrl = "https://app.test";
    private const string TestIpHeader = "X-Test-Ip";

    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:16").Build();

    public FakeEmailSender Email { get; } = new();

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();

        // Never let a configuration mistake point the tests at the developer's database.
        var expected = new Npgsql.NpgsqlConnectionStringBuilder(_postgres.GetConnectionString()).Port;
        var actual = new Npgsql.NpgsqlConnectionStringBuilder(db.Database.GetConnectionString()).Port;
        if (actual != expected)
        {
            throw new InvalidOperationException("Tests are not using the Testcontainers database.");
        }

        await db.Database.MigrateAsync();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await DisposeAsync();
        await _postgres.DisposeAsync();
    }

    /// <summary>
    /// A client with its own cookie jar and, unless given one, its own IP so rate limits don't leak
    /// between tests. HTTPS because the refresh cookie is Secure.
    /// </summary>
    public HttpClient CreateApiClient(string? ip = null, bool handleCookies = true)
    {
        var client = CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = handleCookies,
        });
        client.DefaultRequestHeaders.Add(TestIpHeader, ip ?? NewIp());

        return client;
    }

    public static string NewIp() =>
        new IPAddress([10, .. RandomNumberGenerator.GetBytes(3)]).ToString();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseSetting("ConnectionStrings:Default", _postgres.GetConnectionString());
        builder.UseSetting("Jwt:SigningKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(48)));
        builder.UseSetting("App:BaseUrl", AppBaseUrl);

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Email);
            services.AddSingleton<IStartupFilter, TestIpStartupFilter>();
        });
    }

    // TestServer has no client address, so take it from a header.
    private sealed class TestIpStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((context, nextMiddleware) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[TestIpHeader], out var ip))
                {
                    context.Connection.RemoteIpAddress = ip;
                }

                return nextMiddleware(context);
            });
            next(app);
        };
    }
}

public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<EmailMessage> _sent = new();

    public Task SendAsync(EmailMessage message, CancellationToken cancellationToken)
    {
        _sent.Enqueue(message);
        return Task.CompletedTask;
    }

    public IReadOnlyList<EmailMessage> SentTo(string address) =>
        [.. _sent.Where(m => string.Equals(m.To, address, StringComparison.OrdinalIgnoreCase))];
}
