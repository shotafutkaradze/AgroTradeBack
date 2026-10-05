using AgroTrade.Api;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;

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

    [HttpPost("verify-email")]
    public async Task<IActionResult> VerifyEmail(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        return await authService.VerifyEmailAsync(request, cancellationToken)
            ? Ok(new { message = "Email verified." })
            : BadRequest(new { message = "Verification link is invalid or expired." });
    }

    [HttpPost("resend-verification")]
    public async Task<IActionResult> ResendVerification(ResendEmailVerificationRequest request, CancellationToken cancellationToken)
    {
        await authService.ResendEmailVerificationAsync(request, cancellationToken);
        return Ok(new { message = "If the account exists, verification email has been sent." });
    }

    [HttpGet]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await authService.GetUsersAsync(cancellationToken));
    }

    [HttpPatch("me")]
    [Authorize]
    public async Task<IActionResult> UpdateMe(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!int.TryParse(currentUserId, out var userId))
        {
            return Unauthorized();
        }

        try
        {
            var user = await authService.UpdateProfileAsync(userId, request, cancellationToken);
            return user is null ? NotFound() : Ok(user);
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
