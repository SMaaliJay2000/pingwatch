using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class MonitorRetargetTests(ApiFactory factory)
{
    [Theory]
    [InlineData("https://example.org", 0)]
    [InlineData("https://example.com", 1)]
    public async Task A_new_target_starts_with_no_failures(string url, int failures)
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().Where(m => m.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ConsecutiveFailures, 1)));

        (await user.Client.PutAsJsonAsync($"/api/v1/monitors/{id}", new { name = "n", type = "http", url }))
            .EnsureSuccessStatusCode();

        var saved = await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters().SingleAsync(m => m.Id == id));
        saved.ConsecutiveFailures.Should().Be(failures);
    }
}
