namespace AgroTrade.Contracts.Payments;

public record PaymentStatusDto(int Id, string OrderNumber, string Status, string PaymentStatus);

public record RefundPaymentRequest(decimal? Amount, string? Reason);

public record RefundPaymentResponse(
    int OrderId,
    string OrderNumber,
    string OrderStatus,
    string PaymentStatus,
    decimal RefundedAmount,
    decimal PaymentAmount,
    string ProviderStatus);

public record LibertyPayPaymentStartRequest(LibertyBrowserRequest? Browser);

public record LibertyBrowserRequest(
    string? DeviceId,
    string? AcceptHeader,
    string? Ip,
    string? TimeZoneOffset,
    string? Platform,
    bool? JsEnabled,
    int? ColorDepth,
    int? ScreenWidth,
    int? ScreenHeight,
    string? UserAgent,
    string? Language,
    bool? JavaEnabled);

public record LibertyPayPaymentStartResponse(
    int OrderId,
    string OrderNumber,
    string PaymentOrderCode,
    string ProviderStatus,
    string? Cvv2AuthStatus,
    string PaymentUrl);

public record LibertyPayCallbackRequest(
    string? Status,
    string? TransactionCode,
    string? Date,
    string? Amount,
    string? Currency,
    string? OrderCode,
    string? PayMethod,
    string? CustomData,
    string? PayedAmount,
    string? TestMode,
    string? Check);

public record LibertyPayCallbackResponse(string Xml, string ResultCode, string ResultDescription);

public record BogInstallmentPaymentStartRequest(int? InstallmentMonth, string? InstallmentType);

public record BogInstallmentPaymentStartResponse(
    int OrderId,
    string OrderNumber,
    string ProviderOrderId,
    string ProviderStatus,
    string PaymentUrl);

public record BogInstallmentCallbackRequest(
    string? Status,
    string? OrderId,
    string? ShopOrderId,
    string? PaymentMethod);

