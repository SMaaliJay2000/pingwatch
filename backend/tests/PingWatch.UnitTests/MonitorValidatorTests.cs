using PingWatch.Application.Monitors;
using PingWatch.Domain;

namespace PingWatch.UnitTests;

public class MonitorValidatorTests
{
    private static readonly MonitorRequest Http = new("Site", MonitorType.Http, Url: "https://example.com");
    private static readonly MonitorRequest Keyword = Http with { Type = MonitorType.Keyword, Keyword = "Welcome" };
    private static readonly MonitorRequest Tcp = new("DB", MonitorType.Tcp, Host: "db.example.com", Port: 5432);
    private static readonly MonitorRequest Ssl = new("Cert", MonitorType.Ssl, Host: "example.com");

    [Fact]
    public void Minimal_request_of_each_type_is_valid()
    {
        foreach (var request in new[] { Http, Keyword, Tcp, Ssl })
        {
            InvalidFields(request).Should().BeEmpty("{0} is complete", request.Type);
        }
    }

    [Theory]
    [InlineData(PlanTier.Free, 299, false)]
    [InlineData(PlanTier.Free, 300, true)]
    [InlineData(PlanTier.Starter, 59, false)]
    [InlineData(PlanTier.Starter, 60, true)]
    [InlineData(PlanTier.Pro, 29, false)]
    [InlineData(PlanTier.Pro, 30, true)]
    public void Interval_must_meet_the_plan_minimum(PlanTier plan, int interval, bool valid)
    {
        var result = new MonitorValidator(Plans.For(plan)).Validate(Http with { IntervalSec = interval });

        result.IsValid.Should().Be(valid);
        if (!valid)
        {
            result.Errors.Should().ContainSingle().Which.ErrorCode.Should().Be(PlanLimitException.Code);
        }
    }

    [Fact]
    public void Interval_cannot_exceed_a_day_and_that_is_not_a_plan_limit()
    {
        var result = new MonitorValidator(Plans.Free).Validate(Http with { IntervalSec = 86_401 });

        result.Errors.Should().ContainSingle().Which.ErrorCode.Should().NotBe(PlanLimitException.Code);
    }

    [Theory]
    [InlineData(999, false)]
    [InlineData(1_000, true)]
    [InlineData(30_000, true)]
    [InlineData(30_001, false)]
    public void Timeout_must_be_between_1_and_30_seconds(int timeoutMs, bool valid)
    {
        InvalidFields(Http with { TimeoutMs = timeoutMs }).Contains("TimeoutMs").Should().Be(!valid);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("example.com")]
    [InlineData("/relative")]
    [InlineData("ftp://example.com")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://")]
    [InlineData("https://user:secret@example.com")]
    public void Bad_urls_are_rejected(string? url)
    {
        InvalidFields(Http with { Url = url }).Should().Equal("Url");
        InvalidFields(Keyword with { Url = url }).Should().Equal("Url");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("https://example.com")]
    [InlineData("example.com:443")]
    [InlineData("example.com/path")]
    [InlineData("two words")]
    public void Bad_hosts_are_rejected(string? host)
    {
        InvalidFields(Tcp with { Host = host }).Should().Equal("Host");
        InvalidFields(Ssl with { Host = host }).Should().Equal("Host");
    }

    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(65_535, true)]
    [InlineData(65_536, false)]
    public void Port_must_be_in_range(int port, bool valid)
    {
        InvalidFields(Tcp with { Port = port }).Contains("Port").Should().Be(!valid);
    }

    [Fact]
    public void Tcp_needs_a_port_but_ssl_does_not()
    {
        InvalidFields(Tcp with { Port = null }).Should().Equal("Port");
        InvalidFields(Ssl with { Port = null }).Should().BeEmpty();
    }

    [Fact]
    public void Keyword_monitor_needs_a_keyword_and_a_method_with_a_body()
    {
        InvalidFields(Keyword with { Keyword = null }).Should().Equal("Keyword");
        InvalidFields(Keyword with { Method = CheckMethod.Head }).Should().Equal("Method");
        InvalidFields(Http with { Method = CheckMethod.Head }).Should().BeEmpty();
    }

    [Fact]
    public void Name_and_type_are_required_and_heartbeat_is_not_available()
    {
        InvalidFields(Http with { Name = " " }).Should().Equal("Name");
        InvalidFields(Http with { Type = null }).Should().Equal("Type");
        InvalidFields(Http with { Type = MonitorType.Heartbeat }).Should().Equal("Type");
        InvalidFields(Http with { Type = (MonitorType)99 }).Should().Equal("Type");
    }

    [Fact]
    public void Expected_status_must_be_parseable()
    {
        InvalidFields(Http with { ExpectedStatus = "ok" }).Should().Equal("ExpectedStatus");
    }

    private static IEnumerable<string> InvalidFields(MonitorRequest request) =>
        new MonitorValidator(Plans.Free).Validate(request).Errors.Select(e => e.PropertyName).Distinct().ToList();
}
