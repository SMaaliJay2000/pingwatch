using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MonitorCrudTests(ApiFactory factory)
{
    [Fact]
    public async Task Create_applies_http_defaults_and_returns_the_location()
    {
        var user = await factory.NewUserAsync();

        var response = await user.Client.PostAsJsonAsync(
            "/api/v1/monitors", new { name = "  Site  ", type = "http", url = "https://example.com" });

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var monitor = await response.JsonAsync();
        response.Headers.Location!.AbsolutePath.Should().Be($"/api/v1/monitors/{monitor.GetProperty("id")}");
        monitor.String("name").Should().Be("Site");
        monitor.String("type").Should().Be("http");
        monitor.String("method").Should().Be("get");
        monitor.String("expectedStatus").Should().Be("200-399");
        monitor.String("status").Should().Be("pending");
        monitor.Int("timeoutMs").Should().Be(10_000);
        monitor.Int("intervalSec").Should().Be(300);
        monitor.IsNull("port").Should().BeTrue();
        monitor.IsNull("uptime24h").Should().BeTrue();
        (await user.Client.GetAsync(response.Headers.Location)).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Each_type_gets_its_own_defaults_and_drops_unrelated_fields()
    {
        var user = await factory.NewUserAsync();
        object[] bodies =
        [
            new { name = "k", type = "keyword", url = "https://example.com", keyword = "ok", host = "x", port = 1 },
            new { name = "t", type = "tcp", host = "db.example.com", port = 5432, url = "https://x", keyword = "no" },
            new { name = "s", type = "ssl", host = "example.com", method = "post" },
        ];
        foreach (var body in bodies)
        {
            await user.CreateMonitorAsync(body);
        }

        var all = (await user.Client.GetJsonAsync("/api/v1/monitors")).EnumerateArray().ToList();

        all.Select(m => m.String("name")).Should().Equal("k", "t", "s");
        var (keyword, tcp, ssl) = (all[0], all[1], all[2]);
        keyword.String("keywordMode").Should().Be("contains");
        keyword.IsNull("host").Should().BeTrue();
        keyword.IsNull("port").Should().BeTrue();
        tcp.Int("timeoutMs").Should().Be(5_000);
        tcp.IsNull("url").Should().BeTrue();
        tcp.IsNull("keyword").Should().BeTrue();
        tcp.IsNull("method").Should().BeTrue();
        ssl.Int("port").Should().Be(443);
        ssl.Int("intervalSec").Should().Be(86_400);
        ssl.IsNull("method").Should().BeTrue();
    }

    [Theory]
    [InlineData("""{"type":"http","url":"https://example.com"}""", "name")]
    [InlineData("""{"name":"x","url":"https://example.com"}""", "type")]
    [InlineData("""{"name":"x","type":"http","url":"not a url"}""", "url")]
    [InlineData("""{"name":"x","type":"tcp","host":"example.com"}""", "port")]
    [InlineData("""{"name":"x","type":"keyword","url":"https://example.com"}""", "keyword")]
    [InlineData("""{"name":"x","type":"http","url":"https://example.com","timeoutMs":60000}""", "timeoutMs")]
    public async Task Invalid_monitor_returns_field_errors(string json, string field)
    {
        var user = await factory.NewUserAsync();

        var response = await user.Client.PostAsync(
            "/api/v1/monitors", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.JsonAsync();
        problem.GetProperty("errors").TryGetProperty(field, out _).Should().BeTrue(problem.ToString());
        problem.TryGetProperty("code", out _).Should().BeFalse("this isn't a plan limit");
    }

    [Theory]
    [InlineData("""{"name":"x","type":99,"url":"https://example.com"}""")]
    [InlineData("""{"name":"x","type":"http","url":"https://example.com","method":7}""")]
    [InlineData("""{"name":"x","type":"smtp","url":"https://example.com"}""")]
    public async Task Unknown_or_numeric_enum_values_are_rejected(string json)
    {
        var user = await factory.NewUserAsync();

        var response = await user.Client.PostAsync(
            "/api/v1/monitors", new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await user.Client.GetJsonAsync("/api/v1/monitors")).GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task Update_replaces_settings_and_switching_type_clears_old_fields()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/monitors/{id}",
            new { name = "DB", type = "tcp", host = "db.example.com", port = 5432, intervalSec = 600 });

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var monitor = await user.Client.GetJsonAsync($"/api/v1/monitors/{id}");
        monitor.String("name").Should().Be("DB");
        monitor.String("type").Should().Be("tcp");
        monitor.Int("intervalSec").Should().Be(600);
        monitor.IsNull("url").Should().BeTrue();
        monitor.IsNull("method").Should().BeTrue();
    }

    [Theory]
    // Same target, longer interval: leave the schedule alone.
    [InlineData("https://example.com", 3600, 1800)]
    // Same target, shorter interval: pull the next check in.
    [InlineData("https://example.com", 600, 600)]
    // New target: check right away.
    [InlineData("https://example.org", 3600, 0)]
    public async Task Update_reschedules_only_when_needed(string url, int intervalSec, int expectedSeconds)
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        var scheduled = DateTimeOffset.UtcNow.AddSeconds(1800);
        await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.NextCheckAt, scheduled)));

        await user.Client.PutAsJsonAsync(
            $"/api/v1/monitors/{id}", new { name = "Site", type = "http", url, intervalSec });

        var saved = await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().SingleAsync(m => m.Id == id));
        saved.NextCheckAt.Should().BeCloseTo(
            DateTimeOffset.UtcNow.AddSeconds(expectedSeconds), TimeSpan.FromSeconds(30));
    }

    [Fact]
    public async Task Pause_closes_the_open_incident_and_resume_makes_the_monitor_due()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        await factory.SeedAsync(new Incident
        {
            AccountId = user.AccountId,
            MonitorId = id,
            StartedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            Cause = "HTTP 503",
        });

        var paused = await user.Client.PostAsync($"/api/v1/monitors/{id}/pause", null);

        (await paused.JsonAsync()).String("status").Should().Be("paused");
        var incident = await factory.WithDbAsync(db =>
            db.Incidents.IgnoreQueryFilters().SingleAsync(i => i.MonitorId == id));
        incident.ResolvedAt.Should().NotBeNull();
        incident.DurationSec.Should().BeInRange(600, 660);
        (await user.Client.PostAsync($"/api/v1/monitors/{id}/pause", null))
            .StatusCode.Should().Be(HttpStatusCode.OK, "pausing twice is harmless");

        var resumed = await user.Client.PostAsync($"/api/v1/monitors/{id}/resume", null);

        (await resumed.JsonAsync()).String("status").Should().Be("pending");
        var saved = await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().SingleAsync(m => m.Id == id));
        saved.NextCheckAt.Should().BeOnOrBefore(DateTimeOffset.UtcNow);
    }

    [Fact]
    public async Task Delete_removes_the_monitor_and_its_history()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        await factory.SeedAsync(
            user.Result(id, DateTimeOffset.UtcNow, isUp: false),
            user.Result(id, DateTimeOffset.UtcNow.AddMonths(-6), isUp: true),
            new Incident { AccountId = user.AccountId, MonitorId = id, StartedAt = DateTimeOffset.UtcNow, Cause = "x" },
            new DailyStat { AccountId = user.AccountId, MonitorId = id, Date = new DateOnly(2026, 1, 1) });

        (await user.Client.DeleteAsync($"/api/v1/monitors/{id}")).StatusCode.Should().Be(HttpStatusCode.NoContent);

        (await user.Client.GetAsync($"/api/v1/monitors/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await user.Client.DeleteAsync($"/api/v1/monitors/{id}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        var leftovers = await factory.WithDbAsync(async db =>
            await db.CheckResults.IgnoreQueryFilters().CountAsync(r => r.MonitorId == id)
            + await db.Incidents.IgnoreQueryFilters().CountAsync(i => i.MonitorId == id)
            + await db.DailyStats.IgnoreQueryFilters().CountAsync(d => d.MonitorId == id));
        leftovers.Should().Be(0);
    }

    [Theory]
    [InlineData("GET", "/api/v1/monitors")]
    [InlineData("POST", "/api/v1/monitors")]
    [InlineData("GET", "/api/v1/monitors/{id}")]
    [InlineData("PUT", "/api/v1/monitors/{id}")]
    [InlineData("DELETE", "/api/v1/monitors/{id}")]
    [InlineData("POST", "/api/v1/monitors/{id}/pause")]
    [InlineData("POST", "/api/v1/monitors/{id}/resume")]
    [InlineData("GET", "/api/v1/monitors/{id}/results")]
    [InlineData("GET", "/api/v1/monitors/{id}/stats")]
    [InlineData("GET", "/api/v1/incidents")]
    public async Task Every_endpoint_requires_a_token(string method, string url)
    {
        var request = new HttpRequestMessage(
            new HttpMethod(method), url.Replace("{id}", Guid.NewGuid().ToString()));

        var response = await factory.CreateApiClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }
}
