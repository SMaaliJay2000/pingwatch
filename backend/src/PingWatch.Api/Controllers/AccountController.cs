using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PingWatch.Application.Accounts;

namespace PingWatch.Api.Controllers;

[ApiController]
[Route("api/v1/account")]
[Authorize]
public sealed class AccountController(AccountService accounts) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<AccountResponse>> Get(CancellationToken cancellationToken) =>
        await accounts.GetAsync(cancellationToken) is { } account ? account : NotFound();

    [HttpGet("usage")]
    public async Task<ActionResult<UsageResponse>> GetUsage(CancellationToken cancellationToken) =>
        await accounts.GetUsageAsync(cancellationToken) is { } usage ? usage : NotFound();
}
