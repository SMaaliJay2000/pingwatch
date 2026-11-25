using FluentValidation;
using PingWatch.Domain;

namespace PingWatch.Application.Monitors;

public static class MonitorRules
{
    public const int MinTimeoutMs = 1_000;
    public const int MaxTimeoutMs = 30_000;
    public const int MaxIntervalSec = 86_400;
}

public sealed class MonitorValidator : AbstractValidator<MonitorRequest>
{
    public MonitorValidator(PlanLimits limits)
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(200);

        RuleFor(x => x.Type)
            .Cascade(CascadeMode.Stop)
            .NotNull()
            .IsInEnum()
            .NotEqual(MonitorType.Heartbeat).WithMessage("Heartbeat monitors are not available yet.");

        RuleFor(x => x.TimeoutMs).InclusiveBetween(MonitorRules.MinTimeoutMs, MonitorRules.MaxTimeoutMs);

        RuleFor(x => x.IntervalSec)
            .Cascade(CascadeMode.Stop)
            .Must(interval => interval is null || limits.AllowsInterval(interval.Value))
            .WithErrorCode(PlanLimitException.Code)
            .WithMessage($"Your plan allows one check every {limits.MinIntervalSec} seconds at the most.")
            .LessThanOrEqualTo(MonitorRules.MaxIntervalSec);

        When(x => x.Type is MonitorType.Http or MonitorType.Keyword, () =>
        {
            RuleFor(x => x.Url)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .MaximumLength(2048)
                .Must(BeHttpUrl).WithMessage("Enter a full http or https URL, without a username or password.");
            RuleFor(x => x.Method).IsInEnum();
            RuleFor(x => x.ExpectedStatus)
                .Cascade(CascadeMode.Stop)
                .MaximumLength(50)
                .Must(value => value is null || StatusCodeRanges.TryParse(value, out _))
                .WithMessage("Use status codes or ranges, such as \"200-399\" or \"200,301\".");
        });

        When(x => x.Type is MonitorType.Keyword, () =>
        {
            RuleFor(x => x.Keyword).NotEmpty().MaximumLength(500);
            RuleFor(x => x.KeywordMode).IsInEnum();
            RuleFor(x => x.Method)
                .NotEqual(CheckMethod.Head)
                .WithMessage("A keyword check needs a response body, so HEAD can't be used.");
        });

        When(x => x.Type is MonitorType.Tcp or MonitorType.Ssl, () =>
        {
            RuleFor(x => x.Host)
                .Cascade(CascadeMode.Stop)
                .NotEmpty()
                .MaximumLength(253)
                .Must(host => Uri.CheckHostName(host) != UriHostNameType.Unknown)
                .WithMessage("Enter a host name or IP address, without a scheme, port or path.");
            RuleFor(x => x.Port).InclusiveBetween(1, 65535);
        });

        // SSL defaults to 443; TCP has no sensible default.
        RuleFor(x => x.Port).NotNull().When(x => x.Type is MonitorType.Tcp);
    }

    // Private and internal addresses are blocked by the SSRF guard at check time (M4),
    // because a name can start resolving to one after the monitor is saved.
    private static bool BeHttpUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && uri.Host.Length > 0
        && uri.UserInfo.Length == 0;
}

internal sealed class ResultsQueryValidator : AbstractValidator<ResultsQuery>
{
    public static readonly ResultsQueryValidator Instance = new();

    private ResultsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.To)
            .Must((query, to) => to is null || query.From is null || to >= query.From)
            .WithMessage("'to' must not be earlier than 'from'.");
    }
}

internal sealed class IncidentsQueryValidator : AbstractValidator<IncidentsQuery>
{
    public static readonly IncidentsQueryValidator Instance = new();

    private IncidentsQueryValidator()
    {
        RuleFor(x => x.Page).InclusiveBetween(1, 10_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 200);
        RuleFor(x => x.Status)
            .Must(status => status is null or "open" or "resolved")
            .WithMessage("Use 'open' or 'resolved'.");
    }
}