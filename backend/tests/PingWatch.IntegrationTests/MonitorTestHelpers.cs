using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.IntegrationTests;

/// <summary>A signed-in client with its account id.</summary>
internal sealed record TestUser(HttpClient Client, Guid AccountId);

internal static class MonitorTestHelpers
{
    public static async Task<TestUser> NewUserAsync(this ApiFactory factory, PlanTier plan = PlanTier.Free)
    {
        var client = factory.CreateApiClient();
        await client.SignUpAsync(factory, AuthTestHelpers.NewEmail());
        var accountId = (await client.GetJsonAsync("/api/v1/account")).GetProperty("id").GetGuid();

        if (plan != PlanTier.Free)
        {
            await factory.SetPlanAsync(accountId, plan);
        }

        return new TestUser(client, accountId);
    }

    /// <summary>Direct database access, with no tenant: reads need IgnoreQueryFilters.</summary>
    public static async Task<T> WithDbAsync<T>(this ApiFactory factory, Func<PingWatchDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<PingWatchDbContext>());
    }

    public static Task SetPlanAsync(this ApiFactory factory, Guid accountId, PlanTier plan) =>
        factory.WithDbAsync(db => db.Accounts.IgnoreQueryFilters()
            .Where(a => a.Id == accountId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Plan, plan)));

    public static Task SeedAsync(this ApiFactory factory, params object[] entities) =>
        factory.WithDbAsync(async db =>
        {
            db.AddRange(entities);
            return await db.SaveChangesAsync();
        });

    public static CheckResult Result(
        this TestUser user, Guid monitorId, DateTimeOffset at, bool isUp, int? responseMs = 100) => new()
        {
            AccountId = user.AccountId,
            MonitorId = monitorId,
            CheckedAt = at,
            IsUp = isUp,
            StatusCode = isUp ? 200 : 503,
            ResponseMs = responseMs,
            Error = isUp ? null : "HTTP 503",
        };

    public static object HttpMonitor(string name = "Site", int? intervalSec = null) =>
        new { name, type = "http", url = "https://example.com", intervalSec };

    public static async Task<Guid> CreateMonitorAsync(this TestUser user, object? body = null)
    {
        var response = await user.Client.PostAsJsonAsync("/api/v1/monitors", body ?? HttpMonitor());
        var json = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.Created, json);

        return JsonDocument.Parse(json).RootElement.GetProperty("id").GetGuid();
    }

    public static async Task<JsonElement> GetJsonAsync(this HttpClient client, string url)
    {
        var response = await client.GetAsync(url);
        var json = await response.Content.ReadAsStringAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK, json);

        return JsonDocument.Parse(json).RootElement;
    }

    public static async Task<JsonElement> JsonAsync(this HttpResponseMessage response) =>
        JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

    public static string? String(this JsonElement element, string property) =>
        element.GetProperty(property).GetString();

    public static int Int(this JsonElement element, string property) =>
        element.GetProperty(property).GetInt32();

    public static bool IsNull(this JsonElement element, string property) =>
        element.GetProperty(property).ValueKind == JsonValueKind.Null;

    public static List<JsonElement> Array(this JsonElement element, string property) =>
        [.. element.GetProperty(property).EnumerateArray()];
}
