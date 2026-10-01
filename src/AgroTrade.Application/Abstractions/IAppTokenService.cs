using AgroTrade.Domain.Models;

namespace AgroTrade.Application.Abstractions;

public interface IAppTokenService
{
    string CreateToken(AppUser user);
}
