using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Auth;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken);
}

public class AuthService(IAgroTradeDbContext dbContext, IAppTokenService tokenService) : IAuthService
{
    private readonly PasswordHasher<AppUser> passwordHasher = new();

    public async Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Phone) ||
            string.IsNullOrWhiteSpace(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password))
        {
            throw new ArgumentException("Name, phone, email and password are required.");
        }

        if (request.Password.Length < 6)
        {
            throw new ArgumentException("Password must contain at least 6 characters.");
        }

        var email = request.Email.Trim().ToLowerInvariant();
        if (await dbContext.AppUsers.AnyAsync(user => user.Email == email, cancellationToken))
        {
            throw new InvalidOperationException("User with this email already exists.");
        }

        var now = DateTimeOffset.UtcNow;
        var user = new AppUser
        {
            FirstName = request.FirstName.Trim(),
            LastName = request.LastName.Trim(),
            Phone = request.Phone.Trim(),
            Email = email,
            Role = UserRole.User,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToResponse(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        return result == PasswordVerificationResult.Failed ? null : ToResponse(user);
    }

    public async Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        return await dbContext.AppUsers
            .AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .Select(user => ToDto(user))
            .ToListAsync(cancellationToken);
    }

    private AuthResponse ToResponse(AppUser user)
    {
        return new AuthResponse(ToDto(user), tokenService.CreateToken(user));
    }

    private static AuthUserDto ToDto(AppUser user)
    {
        return new AuthUserDto(user.Id, user.FirstName, user.LastName, user.Email, user.Phone, user.Role.ToValue(), user.CreatedAt);
    }
}
