using AgroTrade.Contracts.Auth;

namespace AgroTrade.Application.Abstractions;

public interface IAccountEmailService
{
    Task SendPasswordResetAsync(AuthUserDto user, string resetUrl, CancellationToken cancellationToken);
    Task SendEmailVerificationAsync(AuthUserDto user, string verificationUrl, CancellationToken cancellationToken);
}
