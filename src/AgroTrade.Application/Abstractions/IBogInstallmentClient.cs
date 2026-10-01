using AgroTrade.Domain.Models;

namespace AgroTrade.Application.Abstractions;

public interface IBogInstallmentClient
{
    Task<BogInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int installmentMonth,
        string installmentType,
        CancellationToken cancellationToken);
}

public record BogInstallmentOrderResult(
    string OrderId,
    string Status,
    string RedirectUrl);
