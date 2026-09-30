using AgroTrade.Api.Data;
using AgroTrade.Api.Models;
using AgroTrade.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AgroTradeDbContext dbContext, AppTokenService tokenService) : ControllerBase
{
    private readonly PasswordHasher<AppUser> passwordHasher = new();

    [HttpPost("register")]
    public async Task<IActionResult> Register(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            return BadRequest(new { message = "Name, phone, email and password are required." });
        }

        if (request.Password.Length < 6)
        {
            return BadRequest(new { message = "Password must contain at least 6 characters." });
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var exists = await dbContext.AppUsers.AnyAsync(user => user.Email == email, cancellationToken);
        if (exists)
        {
            return Conflict(new { message = "User with this email already exists." });
        }

        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Email = email,
            Role = "user",
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToResponse(user));
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null)
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        if (result == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { message = "Email or password is incorrect." });
        }

        return Ok(ToResponse(user));
    }

    [HttpGet]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var users = await dbContext.AppUsers
            .AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(users.Select(ToDto));
    }

    private AuthResponse ToResponse(AppUser user)
    {
        return new AuthResponse(ToDto(user), tokenService.CreateToken(user));
    }

    private static AuthUserDto ToDto(AppUser user)
    {
        return new AuthUserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Phone, user.Role, user.CreatedAt);
    }
}

public record RegisterRequest(string FirstName, string LastName, string Phone, string Email, string Password);

public record LoginRequest(string Email, string Password);

public record AuthUserDto(int Id, string FirstName, string LastName, string Email, string Phone, string Role, DateTimeOffset CreatedAt);

public record AuthResponse(AuthUserDto User, string Token);
