using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class TenantIsolationTests(ApiFactory factory)
{
    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "")]
    [InlineData("DELETE", "")]
    [InlineData("POST", "/pause")]
    [InlineData("POST", "/resume")]
    [InlineData("GET", "/results")]
    [InlineData("GET", "/stats?range=24h")]
    [InlineData("GET", "/stats?range=7d")]
    [InlineData("GET", "/stats?range=30d")]
    public async Task Account_B_gets_not_found_for_account_As_monitor(string method, string suffix)
    {
        var (a, b, id) = await ArrangeAsync();
        var request = new HttpRequestMessage(new HttpMethod(method), $"/api/v1/monitors/{id}{suffix}");
        if (method == "PUT")
        {
            request.Content = JsonContent.Create(MonitorTestHelpers.HttpMonitor("hijacked"));
        }

        var response = await b.Client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await response.Content.ReadAsStringAsync()).Should().NotContain("A's private monitor");

        // And A's monitor and history are untouched.
        var monitor = await a.Client.GetJsonAsync($"/api/v1/monitors/{id}");
        monitor.String("name").Should().Be("A's private monitor");
        monitor.String("status").Should().Be("pending");
        (await a.Client.GetJsonAsync($"/api/v1/monitors/{id}/results")).Array("items").Should().HaveCount(1);
        (await factory.WithDbAsync(db =>
            db.Incidents.IgnoreQueryFilters().CountAsync(i => i.MonitorId == id && i.ResolvedAt == null)))
            .Should().Be(1);
    }

    [Fact]
    public async Task Lists_never_include_another_accounts_data()
    {
        var (a, b, id) = await ArrangeAsync();

        (await b.Client.GetJsonAsync("/api/v1/monitors")).GetArrayLength().Should().Be(0);
        (await b.Client.GetJsonAsync("/api/v1/incidents")).Array("items").Should().BeEmpty();
        (await b.Client.GetJsonAsync($"/api/v1/incidents?monitorId={id}")).Array("items").Should().BeEmpty();
        (await a.Client.GetJsonAsync("/api/v1/incidents")).Array("items").Should().HaveCount(1);
    }

    [Fact]
    public async Task Account_id_in_the_request_body_is_ignored()
    {
        var a = await factory.NewUserAsync();
        var b = await factory.NewUserAsync();

        var id = await b.CreateMonitorAsync(
            new { name = "planted", type = "http", url = "https://example.com", accountId = a.AccountId });

        var saved = await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().SingleAsync(m => m.Id == id));
        saved.AccountId.Should().Be(b.AccountId);
        (await a.Client.GetJsonAsync("/api/v1/monitors")).GetArrayLength().Should().Be(0);
    }

    private async Task<(TestUser A, TestUser B, Guid MonitorId)> ArrangeAsync()
    {
        var a = await factory.NewUserAsync();
        var b = await factory.NewUserAsync();
        var id = await a.CreateMonitorAsync(MonitorTestHelpers.HttpMonitor("A's private monitor"));
        await factory.SeedAsync(
            a.Result(id, DateTimeOffset.UtcNow.AddMinutes(-1), isUp: false),
            new Incident { AccountId = a.AccountId, MonitorId = id, StartedAt = DateTimeOffset.UtcNow, Cause = "x" });

        return (a, b, id);
    }
}
