using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using PingWatch.Domain;

namespace PingWatch.IntegrationTests;

[Collection(ApiCollection.Name)]
public class PlanLimitTests(ApiFactory factory)
{
    [Fact]
    public async Task Free_plan_stops_at_five_monitors_until_one_is_deleted()
    {
        var user = await factory.NewUserAsync();
        var ids = new List<Guid>();
        for (var i = 0; i < 5; i++)
        {
            ids.Add(await user.CreateMonitorAsync());
        }

        await ShouldHitPlanLimit(await CreateAsync(user));

        await user.Client.DeleteAsync($"/api/v1/monitors/{ids[0]}");
        (await CreateAsync(user)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task Paused_monitors_count_towards_the_limit()
    {
        var user = await factory.NewUserAsync();
        for (var i = 0; i < 5; i++)
        {
            var id = await user.CreateMonitorAsync();
            await user.Client.PostAsync($"/api/v1/monitors/{id}/pause", null);
        }

        await ShouldHitPlanLimit(await CreateAsync(user));
    }

    [Fact]
    public async Task Simultaneous_creates_cannot_exceed_the_limit()
    {
        var user = await factory.NewUserAsync();
        for (var round = 0; round < 3; round++)
        {
            var responses = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => CreateAsync(user)));

            responses.Count(r => r.StatusCode == HttpStatusCode.Created).Should().Be(5);
            responses.Count(r => r.StatusCode == HttpStatusCode.Forbidden).Should().Be(7);
            (await user.Client.GetJsonAsync("/api/v1/monitors")).GetArrayLength().Should().Be(5);

            await factory.WithDbAsync(db => db.Monitors.IgnoreQueryFilters()
                .Where(m => m.AccountId == user.AccountId).ExecuteDeleteAsync());
        }
    }

    [Fact]
    public async Task One_accounts_monitors_do_not_use_up_anothers_limit()
    {
        var full = await factory.NewUserAsync();
        var empty = await factory.NewUserAsync();
        for (var i = 0; i < 5; i++)
        {
            await full.CreateMonitorAsync();
        }

        (await CreateAsync(empty)).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Theory]
    [InlineData(PlanTier.Free, 300)]
    [InlineData(PlanTier.Starter, 60)]
    [InlineData(PlanTier.Pro, 30)]
    public async Task Interval_below_the_plan_minimum_is_rejected_on_create_and_update(PlanTier plan, int minimum)
    {
        var user = await factory.NewUserAsync(plan);

        var create = await user.Client.PostAsJsonAsync(
            "/api/v1/monitors", MonitorTestHelpers.HttpMonitor(intervalSec: minimum - 1));
        var id = await user.CreateMonitorAsync(MonitorTestHelpers.HttpMonitor(intervalSec: minimum));
        var update = await user.Client.PutAsJsonAsync(
            $"/api/v1/monitors/{id}", MonitorTestHelpers.HttpMonitor(intervalSec: minimum - 1));

        foreach (var response in new[] { create, update })
        {
            response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
            var problem = await response.JsonAsync();
            problem.String("code").Should().Be("plan_limit");
            problem.GetProperty("errors").TryGetProperty("intervalSec", out _).Should().BeTrue();
        }
    }

    [Fact]
    public async Task An_account_at_its_limit_can_still_edit_monitors()
    {
        var user = await factory.NewUserAsync();
        var id = await user.CreateMonitorAsync();
        for (var i = 0; i < 4; i++)
        {
            await user.CreateMonitorAsync();
        }

        var response = await user.Client.PutAsJsonAsync(
            $"/api/v1/monitors/{id}", MonitorTestHelpers.HttpMonitor("Renamed"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task After_a_downgrade_resume_cannot_exceed_the_active_limit()
    {
        var user = await factory.NewUserAsync(PlanTier.Starter);
        var ids = new List<Guid>();
        for (var i = 0; i < 7; i++)
        {
            ids.Add(await user.CreateMonitorAsync());
        }

        await user.Client.PostAsync($"/api/v1/monitors/{ids[0]}/pause", null);
        await user.Client.PostAsync($"/api/v1/monitors/{ids[1]}/pause", null);
        await factory.SetPlanAsync(user.AccountId, PlanTier.Free);

        // Five are still active, which fills the Free plan.
        await ShouldHitPlanLimit(await user.Client.PostAsync($"/api/v1/monitors/{ids[0]}/resume", null));

        await user.Client.PostAsync($"/api/v1/monitors/{ids[2]}/pause", null);
        (await user.Client.PostAsync($"/api/v1/monitors/{ids[0]}/resume", null))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task After_a_downgrade_resume_requires_an_allowed_interval()
    {
        var user = await factory.NewUserAsync(PlanTier.Starter);
        var id = await user.CreateMonitorAsync(MonitorTestHelpers.HttpMonitor(intervalSec: 60));
        await user.Client.PostAsync($"/api/v1/monitors/{id}/pause", null);
        await factory.SetPlanAsync(user.AccountId, PlanTier.Free);

        await ShouldHitPlanLimit(await user.Client.PostAsync($"/api/v1/monitors/{id}/resume", null));

        (await user.Client.GetJsonAsync($"/api/v1/monitors/{id}")).String("status").Should().Be("paused");
    }

    private static Task<HttpResponseMessage> CreateAsync(TestUser user) =>
        user.Client.PostAsJsonAsync("/api/v1/monitors", MonitorTestHelpers.HttpMonitor());

    private static async Task ShouldHitPlanLimit(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        (await response.JsonAsync()).String("code").Should().Be("plan_limit");
    }
}
