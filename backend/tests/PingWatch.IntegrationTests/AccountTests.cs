using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class AccountTests(ApiFactory factory)
{
    [Theory]
    [InlineData("/api/v1/account")]
    [InlineData("/api/v1/account/usage")]
    public async Task Account_endpoints_require_a_token(string url)
    {
        (await factory.CreateApiClient().GetAsync(url)).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Token_with_a_changed_signature_is_rejected()
    {
        var client = factory.CreateApiClient();
        await client.SignUpAsync(factory, AuthTestHelpers.NewEmail());
        var parts = client.DefaultRequestHeaders.Authorization!.Parameter!.Split('.');
        var signature = (parts[2][0] == 'A' ? "B" : "A") + parts[2][1..];
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", $"{parts[0]}.{parts[1]}.{signature}");

        (await client.GetAsync("/api/v1/account")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Account_returns_the_signed_in_account_with_snake_case_enums()
    {
        var client = factory.CreateApiClient();
        await client.SignUpAsync(factory, AuthTestHelpers.NewEmail(), "Acme Ltd");

        var account = await client.GetFromJsonAsync<JsonElement>("/api/v1/account");

        account.GetProperty("name").GetString().Should().Be("Acme Ltd");
        account.GetProperty("plan").GetString().Should().Be("free");
        account.GetProperty("subscriptionStatus").GetString().Should().Be("none");
    }

    [Fact]
    public async Task Usage_counts_only_the_signed_in_accounts_resources()
    {
        var mine = factory.CreateApiClient();
        var theirs = factory.CreateApiClient();
        await mine.SignUpAsync(factory, AuthTestHelpers.NewEmail(), "Mine");
        await theirs.SignUpAsync(factory, AuthTestHelpers.NewEmail(), "Theirs");
        await SeedAsync(await AccountIdAsync(mine), monitors: 2, statusPages: 1);
        await SeedAsync(await AccountIdAsync(theirs), monitors: 4, statusPages: 0);

        var usage = await mine.GetFromJsonAsync<JsonElement>("/api/v1/account/usage");

        usage.GetProperty("plan").GetString().Should().Be("free");
        Quota("monitors").Should().Be((2, 5));
        Quota("statusPages").Should().Be((1, 1));
        usage.GetProperty("minIntervalSec").GetInt32().Should().Be(300);
        usage.GetProperty("retentionDays").GetInt32().Should().Be(7);
        usage.GetProperty("customDomain").GetBoolean().Should().BeFalse();
        usage.GetProperty("alertChannels").EnumerateArray().Select(c => c.GetString())
            .Should().Equal("email");

        (int Used, int Limit) Quota(string name)
        {
            var quota = usage.GetProperty(name);
            return (quota.GetProperty("used").GetInt32(), quota.GetProperty("limit").GetInt32());
        }
    }

    private static async Task<Guid> AccountIdAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<JsonElement>("/api/v1/account")).GetProperty("id").GetGuid();

    private async Task SeedAsync(Guid accountId, int monitors, int statusPages)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<PingWatchDbContext>();

        db.Monitors.AddRange(Enumerable.Range(0, monitors).Select(i => new UptimeMonitor
        {
            AccountId = accountId,
            Name = $"monitor {i}",
            Type = MonitorType.Http,
            Url = "https://example.com",
        }));
        db.StatusPages.AddRange(Enumerable.Range(0, statusPages).Select(i => new StatusPage
        {
            AccountId = accountId,
            Slug = $"page-{Guid.NewGuid():N}",
            Title = $"page {i}",
        }));

        await db.SaveChangesAsync();
    }
}
