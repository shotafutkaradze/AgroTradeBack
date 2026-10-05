using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Auth;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Application.Services;

public interface IAuthService
{
    Task<AuthResponse> RegisterAsync(RegisterRequest request, CancellationToken cancellationToken);
    Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken);
    Task<bool> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken);
    Task<bool> ResendEmailVerificationAsync(ResendEmailVerificationRequest request, CancellationToken cancellationToken);
    Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken);
    Task<AuthUserDto?> UpdateProfileAsync(int id, UpdateProfileRequest request, CancellationToken cancellationToken);
    Task<AuthUserDto> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken);
    Task<AuthUserDto?> UpdateRoleAsync(int id, UpdateUserRoleRequest request, CancellationToken cancellationToken);
    Task<AuthUserDto?> UpdateBlockAsync(int id, UpdateUserBlockRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteUserAsync(int id, CancellationToken cancellationToken);
}

public class AuthService(
    IAgroTradeDbContext dbContext,
    IAppTokenService tokenService,
    IAccountEmailService accountEmailService,
    IConfiguration configuration,
    ILogger<AuthService> logger) : IAuthService
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
            EmailVerificationToken = CreateEmailVerificationToken(),
            EmailVerificationTokenExpiresAt = now.AddDays(2),
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        await SendVerificationEmailAsync(user, cancellationToken);

        return ToResponse(user);
    }

    public async Task<AuthResponse?> LoginAsync(LoginRequest request, CancellationToken cancellationToken)
    {
        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null || user.IsBlocked)
        {
            return null;
        }

        var result = passwordHasher.VerifyHashedPassword(user, user.PasswordHash, request.Password);
        return result == PasswordVerificationResult.Failed ? null : ToResponse(user);
    }

    public async Task<bool> VerifyEmailAsync(VerifyEmailRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Token))
        {
            return false;
        }

        var token = request.Token.Trim();
        var now = DateTimeOffset.UtcNow;
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(
            user => user.EmailVerificationToken == token,
            cancellationToken);

        if (user is null ||
            user.EmailVerificationTokenExpiresAt is null ||
            user.EmailVerificationTokenExpiresAt < now)
        {
            return false;
        }

        user.IsEmailVerified = true;
        user.EmailVerifiedAt = now;
        user.EmailVerificationToken = null;
        user.EmailVerificationTokenExpiresAt = null;
        user.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> ResendEmailVerificationAsync(ResendEmailVerificationRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Email))
        {
            return false;
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Email == email, cancellationToken);
        if (user is null || user.IsBlocked || user.IsEmailVerified)
        {
            return false;
        }

        user.EmailVerificationToken = CreateEmailVerificationToken();
        user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(2);
        user.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await SendVerificationEmailAsync(user, cancellationToken);
        return true;
    }

    public async Task<IReadOnlyList<AuthUserDto>> GetUsersAsync(CancellationToken cancellationToken)
    {
        return await dbContext.AppUsers
            .AsNoTracking()
            .OrderByDescending(user => user.CreatedAt)
            .Select(user => ToDto(user))
            .ToListAsync(cancellationToken);
    }

    public async Task<AuthUserDto?> UpdateProfileAsync(int id, UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.FirstName) ||
            string.IsNullOrWhiteSpace(request.LastName) ||
            string.IsNullOrWhiteSpace(request.Phone))
        {
            throw new ArgumentException("Name and phone are required.");
        }

        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null || user.IsBlocked)
        {
            return null;
        }

        var requestedEmail = request.Email.Trim().ToLowerInvariant();
        if (!user.IsEmailVerified && !string.IsNullOrWhiteSpace(requestedEmail) && requestedEmail != user.Email)
        {
            if (await dbContext.AppUsers.AnyAsync(other => other.Id != id && other.Email == requestedEmail, cancellationToken))
            {
                throw new InvalidOperationException("User with this email already exists.");
            }

            user.Email = requestedEmail;
            user.IsEmailVerified = false;
            user.EmailVerifiedAt = null;
            user.EmailVerificationToken = CreateEmailVerificationToken();
            user.EmailVerificationTokenExpiresAt = DateTimeOffset.UtcNow.AddDays(2);
        }

        user.FirstName = request.FirstName.Trim();
        user.LastName = request.LastName.Trim();
        user.Phone = request.Phone.Trim();
        user.City = Clean(request.City);
        user.Region = Clean(request.Region);
        user.Street = Clean(request.Street);
        user.Building = Clean(request.Building);
        user.AddressNote = Clean(request.AddressNote);
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        if (!user.IsEmailVerified && !string.IsNullOrWhiteSpace(user.EmailVerificationToken))
        {
            await SendVerificationEmailAsync(user, cancellationToken);
        }

        return ToDto(user);
    }

    public async Task<AuthUserDto> CreateUserAsync(AdminCreateUserRequest request, CancellationToken cancellationToken)
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

        if (!request.Role.TryToUserRole(out var role))
        {
            throw new ArgumentException("User role is not supported.");
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
            Role = role,
            IsEmailVerified = true,
            EmailVerifiedAt = now,
            CreatedAt = now,
            UpdatedAt = now
        };
        user.PasswordHash = passwordHasher.HashPassword(user, request.Password);

        dbContext.AppUsers.Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return ToDto(user);
    }

    public async Task<AuthUserDto?> UpdateRoleAsync(int id, UpdateUserRoleRequest request, CancellationToken cancellationToken)
    {
        if (!request.Role.TryToUserRole(out var role))
        {
            throw new ArgumentException("User role is not supported.");
        }

        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.Role = role;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<AuthUserDto?> UpdateBlockAsync(int id, UpdateUserBlockRequest request, CancellationToken cancellationToken)
    {
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null)
        {
            return null;
        }

        user.IsBlocked = request.IsBlocked;
        user.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return ToDto(user);
    }

    public async Task<bool> DeleteUserAsync(int id, CancellationToken cancellationToken)
    {
        var user = await dbContext.AppUsers.FirstOrDefaultAsync(user => user.Id == id, cancellationToken);
        if (user is null)
        {
            return false;
        }

        dbContext.AppUsers.Remove(user);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private AuthResponse ToResponse(AppUser user)
    {
        return new AuthResponse(ToDto(user), tokenService.CreateToken(user));
    }

    private static AuthUserDto ToDto(AppUser user)
    {
        return new AuthUserDto(
            user.Id,
            user.FirstName,
            user.LastName,
            user.Email,
            user.Phone,
            user.Role.ToValue(),
            user.IsBlocked,
            user.IsEmailVerified,
            user.City,
            user.Region,
            user.Street,
            user.Building,
            user.AddressNote,
            user.CreatedAt);
    }

    private static string? Clean(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private async Task SendVerificationEmailAsync(AppUser user, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.EmailVerificationToken))
        {
            return;
        }

        var url = BuildVerificationUrl(user.EmailVerificationToken);
        try
        {
            await accountEmailService.SendEmailVerificationAsync(ToDto(user), url, cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to send email verification link for user {UserId}", user.Id);
        }
    }

    private string BuildVerificationUrl(string token)
    {
        var baseUrl = configuration["Auth:EmailVerificationUrl"]
            ?? configuration["BogInstallment:FrontendReturnUrl"]
            ?? "http://localhost:5173/AgroTradefrontend/";
        var separator = baseUrl.Contains('?') ? "&" : "?";
        return $"{baseUrl}{separator}verifyEmail={Uri.EscapeDataString(token)}";
    }

    private static string CreateEmailVerificationToken()
    {
        return Convert.ToBase64String(Guid.NewGuid().ToByteArray())
            .Replace("+", string.Empty)
            .Replace("/", string.Empty)
            .Replace("=", string.Empty);
    }
}
