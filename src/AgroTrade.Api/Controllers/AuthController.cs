using AgroTrade.Api;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.RegisterAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var response = await authService.LoginAsync(request, cancellationToken);
        return response is null ? Unauthorized(new { message = "Email or password is incorrect." }) : Ok(response);
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await authService.GetUsersAsync(cancellationToken));
    }

    [HttpPost("admin/users")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> CreateUser(AdminCreateUserRequest request, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(await authService.CreateUserAsync(request, cancellationToken));
        }
        catch (InvalidOperationException exception)
        {
            return Conflict(new { message = exception.Message });
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPatch("{id:int}/role")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateRole(int id, UpdateUserRoleRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var user = await authService.UpdateRoleAsync(id, request, cancellationToken);
            return user is null ? NotFound() : Ok(user);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPatch("{id:int}/block")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> UpdateBlock(int id, UpdateUserBlockRequest request, CancellationToken cancellationToken)
    {
        var user = await authService.UpdateBlockAsync(id, request, cancellationToken);
        return user is null ? NotFound() : Ok(user);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        return await authService.DeleteUserAsync(id, cancellationToken) ? NoContent() : NotFound();
    }
}
