using AgroTrade.Domain.Enums;

namespace AgroTrade.Domain.Models;

public class Payment
{
    public int Id { get; set; }
    public int OrderId { get; set; }
    public Order? Order { get; set; }
    public PaymentProvider Provider { get; set; } = PaymentProvider.LibertyPay;
    public PaymentMethod Method { get; set; } = PaymentMethod.Liberty;
    public PaymentStatus Status { get; set; } = PaymentStatus.Pending;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }
    public string? ProviderOrderPassword { get; set; }
    public string? ProviderStatus { get; set; }
    public string? ProviderTransactionCode { get; set; }
    public string? FailureReason { get; set; }
    public decimal RefundedAmount { get; set; }
    public string? RefundReason { get; set; }
    public string? ProviderRefundStatus { get; set; }
    public DateTimeOffset? RefundedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
}

