using AgroTrade.Api;
using AgroTrade.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/activity-logs")]
[Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
public class ActivityLogsController(IActivityLogService activityLogService) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] int take = 80, CancellationToken cancellationToken = default)
    {
        return Ok(await activityLogService.GetImportantAsync(take, cancellationToken));
    }
}
