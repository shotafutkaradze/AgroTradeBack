using AgroTrade.Contracts.Auth;

namespace AgroTrade.Application.Abstractions;

public interface IAccountEmailService
{
    Task SendEmailVerificationAsync(AuthUserDto user, string verificationUrl, CancellationToken cancellationToken);
}
