using AgroTrade.Domain.Models;

namespace AgroTrade.Application.Abstractions;

public interface IBogInstallmentClient
{
    AgroTrade.Contracts.Payments.BogInstallmentCallbackRequest? VerifyCallback(byte[] body, string signature);
    Task<AgroTrade.Contracts.Payments.BogInstallmentCallbackRequest> GetPaymentDetailsAsync(string orderId, CancellationToken cancellationToken);

    Task<BogInstallmentOrderResult> CreateOrderAsync(
        Order order,
        int installmentMonth,
        string installmentType,
        CancellationToken cancellationToken,
        Guid? idempotencyKey = null);
}

public record BogInstallmentOrderResult(
    string OrderId,
    string Status,
    string RedirectUrl);
