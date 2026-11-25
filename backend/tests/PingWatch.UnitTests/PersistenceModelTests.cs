using Microsoft.EntityFrameworkCore;
using NSubstitute;
using PingWatch.Application;
using PingWatch.Domain;
using PingWatch.Infrastructure.Persistence;

namespace PingWatch.UnitTests;

public class PersistenceModelTests
{
    [Fact]
    public void Account_and_every_tenant_owned_entity_have_a_query_filter()
    {
        using var context = CreateContext(null);

        var filtered = context.Model.GetEntityTypes()
            .Where(t => t.ClrType == typeof(Account) || typeof(ITenantOwned).IsAssignableFrom(t.ClrType))
            .ToList();

        filtered.Should().HaveCount(10);
        filtered.Should().OnlyContain(t => t.GetQueryFilter() != null);
    }

    [Fact]
    public void Query_filter_uses_each_context_instances_own_tenant()
    {
        // EF caches the model, so this catches a filter that captured the first tenant forever.
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        using var firstContext = CreateContext(first);
        using var secondContext = CreateContext(second);

        var firstSql = firstContext.Monitors.ToQueryString();
        var secondSql = secondContext.Monitors.ToQueryString();

        firstSql.Should().Contain(first.ToString()).And.NotContain(second.ToString());
        secondSql.Should().Contain(second.ToString()).And.NotContain(first.ToString());
    }

    [Theory]
    [InlineData(typeof(UptimeMonitor), nameof(UptimeMonitor.Status), MonitorStatus.Paused, "paused")]
    [InlineData(typeof(AlertLogEntry), nameof(AlertLogEntry.Type), AlertType.SslWarning, "ssl_warning")]
    public void Enums_are_stored_as_snake_case_text(Type entity, string property, object value, string expected)
    {
        using var context = CreateContext(null);

        var converter = context.Model.FindEntityType(entity)!
            .FindProperty(property)!
            .GetTypeMapping()
            .Converter!;

        converter.ConvertToProvider(value).Should().Be(expected);
        converter.ConvertFromProvider(expected).Should().Be(value);
    }

    private static PingWatchDbContext CreateContext(Guid? accountId)
    {
        var tenant = Substitute.For<ITenantContext>();
        tenant.AccountId.Returns(accountId);

        // Never connects: these tests only inspect the model and generated SQL.
        var options = new DbContextOptionsBuilder<PingWatchDbContext>()
            .UseNpgsql("Host=unused")
            .UseSnakeCaseNamingConvention()
            .Options;

        return new PingWatchDbContext(options, tenant);
    }
}
