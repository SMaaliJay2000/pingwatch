using System.Security.Claims;
using PingWatch.Application;

namespace PingWatch.Api.Auth;

/// <summary>The tenant is whatever the validated access token says. Anonymous requests have none.</summary>
internal sealed class HttpTenantContext(IHttpContextAccessor accessor) : ITenantContext
{
    public Guid? AccountId =>
        Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(PingWatchClaims.AccountId), out var accountId)
            ? accountId
            : null;
}
