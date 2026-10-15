using AgroTrade.Domain.Models;

namespace AgroTrade.Application.Abstractions;

public interface ITbcInstallmentClient
{
    Task<TbcInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int? installmentMonth,
        CancellationToken cancellationToken);
}

public record TbcInstallmentOrderResult(
    string OrderId,
    string Status,
    string RedirectUrl);
