using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Payments;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AgroTrade.Application.Services;

public interface IPaymentService
{
    Task<LibertyPayPaymentStartResponse?> StartLibertyPaymentAsync(
        int orderId,
        LibertyPayPaymentStartRequest request,
        CancellationToken cancellationToken);

    Task<PaymentStatusDto?> HandleLibertyApproveAsync(
        string? paymentOrderCode,
        string? transactionCode,
        CancellationToken cancellationToken);
    Task<PaymentStatusDto?> HandleLibertyDeclineAsync(string? paymentOrderCode, CancellationToken cancellationToken);
    Task<PaymentStatusDto?> HandleLibertyCancelAsync(string? paymentOrderCode, CancellationToken cancellationToken);
    Task<LibertyPayCallbackResponse> HandleLibertyCallbackAsync(
        LibertyPayCallbackRequest request,
        CancellationToken cancellationToken);
    Task<RefundPaymentResponse?> RefundLibertyPaymentAsync(
        int orderId,
        RefundPaymentRequest request,
        CancellationToken cancellationToken);
    Task<BogInstallmentPaymentStartResponse?> StartBogInstallmentAsync(
        int orderId,
        BogInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken);
    Task<PaymentStatusDto?> HandleBogInstallmentCallbackAsync(
        BogInstallmentCallbackRequest request,
        CancellationToken cancellationToken);
}

public class PaymentService(
    IAgroTradeDbContext dbContext,
    ILibertyPayRefundClient libertyPayRefundClient,
    IBogInstallmentClient bogInstallmentClient,
    IActivityLogService activityLogService,
    IOptions<LibertyPayOptions> libertyPayOptions,
    IOptions<BogInstallmentOptions> bogInstallmentOptions) : IPaymentService
{
    private readonly LibertyPayOptions libertyPayOptions = libertyPayOptions.Value;
    private readonly BogInstallmentOptions bogInstallmentOptions = bogInstallmentOptions.Value;

    public async Task<LibertyPayPaymentStartResponse?> StartLibertyPaymentAsync(
        int orderId,
        LibertyPayPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.PaymentMethod != PaymentMethod.Liberty)
        {
            throw new InvalidOperationException("Order payment method is not Liberty Pay.");
        }

        var now = DateTimeOffset.UtcNow;
        var orderCode = order.OrderNumber;
        order.LibertyPaymentOrderCode = orderCode;
        order.LibertyPayOrderPassword = null;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;

        var payment = await dbContext.Payments
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(payment =>
                payment.OrderId == order.Id &&
                payment.Provider == PaymentProvider.LibertyPay &&
                payment.ProviderOrderId == null,
                cancellationToken);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.LibertyPay,
                Method = PaymentMethod.Liberty,
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.Status = PaymentStatus.Pending;
        payment.Amount = order.Total;
        payment.Currency = libertyPayOptions.Currency;
        payment.ProviderOrderId = orderCode;
        payment.ProviderOrderPassword = null;
        payment.ProviderStatus = "CREATED";
        payment.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "info",
            "payment",
            "liberty_payment_started",
            $"Liberty გადახდა დაიწყო შეკვეთაზე {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "Liberty",
            payment.Status.ToValue(),
            Details: $"თანხა: {order.Total:0.##} ₾"),
            cancellationToken);

        var paymentUrl = BuildLibertyPayUrl(order);
        return new LibertyPayPaymentStartResponse(
            order.Id,
            order.OrderNumber,
            orderCode,
            "CREATED",
            null,
            paymentUrl);
    }

    public Task<PaymentStatusDto?> HandleLibertyApproveAsync(
        string? paymentOrderCode,
        string? transactionCode,
        CancellationToken cancellationToken)
    {
        return UpdateReturnStatusAsync(
            paymentOrderCode,
            PaymentStatus.Paid,
            OrderStatus.Processing,
            transactionCode,
            cancellationToken);
    }

    public async Task<BogInstallmentPaymentStartResponse?> StartBogInstallmentAsync(
        int orderId,
        BogInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.PaymentMethod != PaymentMethod.BogInstallmentPlan)
        {
            throw new InvalidOperationException("Order payment method is not BOG installment.");
        }

        var month = request.InstallmentMonth.GetValueOrDefault(bogInstallmentOptions.DefaultInstallmentMonth);
        var type = string.IsNullOrWhiteSpace(request.InstallmentType)
            ? bogInstallmentOptions.DefaultInstallmentType
            : request.InstallmentType.Trim();

        var bogOrder = await bogInstallmentClient.CreateOrderAsync(order, month, type, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;

        var payment = await dbContext.Payments
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(payment =>
                payment.OrderId == order.Id &&
                payment.Provider == PaymentProvider.BogInstallment,
                cancellationToken);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.BogInstallment,
                Method = PaymentMethod.BogInstallmentPlan,
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.Status = PaymentStatus.Pending;
        payment.Amount = order.Total;
        payment.Currency = bogInstallmentOptions.Currency;
        payment.ProviderOrderId = bogOrder.OrderId;
        payment.ProviderStatus = bogOrder.Status;
        payment.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "info",
            "payment",
            "bog_installment_started",
            $"BOG განვადება დაიწყო შეკვეთაზე {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "BOG",
            payment.Status.ToValue(),
            Details: $"BOG order: {bogOrder.OrderId}; თანხა: {order.Total:0.##} ₾"),
            cancellationToken);

        return new BogInstallmentPaymentStartResponse(
            order.Id,
            order.OrderNumber,
            bogOrder.OrderId,
            bogOrder.Status,
            bogOrder.RedirectUrl);
    }

    public Task<PaymentStatusDto?> HandleLibertyDeclineAsync(string? paymentOrderCode, CancellationToken cancellationToken)
    {
        return UpdateReturnStatusAsync(paymentOrderCode, PaymentStatus.Failed, null, null, cancellationToken);
    }

    public Task<PaymentStatusDto?> HandleLibertyCancelAsync(string? paymentOrderCode, CancellationToken cancellationToken)
    {
        return UpdateReturnStatusAsync(paymentOrderCode, PaymentStatus.Cancelled, OrderStatus.Cancelled, null, cancellationToken);
    }

    public async Task<LibertyPayCallbackResponse> HandleLibertyCallbackAsync(
        LibertyPayCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (!IsCallbackCheckValid(request))
        {
            await AddPaymentIssueLogAsync(
                "liberty_callback_invalid_check",
                "Liberty callback ვერ გადამოწმდა",
                request.OrderCode,
                "Liberty",
                request.Status,
                $"transactioncode={request.TransactionCode}; amount={request.Amount}; paymethod={request.PayMethod}",
                cancellationToken);
            return BuildCallbackResponse("-3", "Invalid check", request.TransactionCode);
        }

        if (string.IsNullOrWhiteSpace(request.OrderCode))
        {
            await AddPaymentIssueLogAsync(
                "liberty_callback_missing_order",
                "Liberty callback მოვიდა შეკვეთის ნომრის გარეშე",
                null,
                "Liberty",
                request.Status,
                $"transactioncode={request.TransactionCode}; amount={request.Amount}",
                cancellationToken);
            return BuildCallbackResponse("-3", "Missing ordercode", request.TransactionCode);
        }

        var order = await dbContext.Orders
            .Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.OrderNumber == request.OrderCode, cancellationToken);

        if (order is null)
        {
            await AddPaymentIssueLogAsync(
                "liberty_callback_order_not_found",
                $"Liberty callback-ზე შეკვეთა ვერ მოიძებნა: {request.OrderCode}",
                request.OrderCode,
                "Liberty",
                request.Status,
                $"transactioncode={request.TransactionCode}; amount={request.Amount}",
                cancellationToken);
            return BuildCallbackResponse("-2", "Order not found", request.TransactionCode);
        }

        var expectedAmount = ToTetri(order.Total);
        if (!string.Equals(request.Amount, expectedAmount, StringComparison.Ordinal))
        {
            await activityLogService.AddAsync(new ActivityLogEntry(
                "error",
                "payment",
                "liberty_callback_invalid_amount",
                $"Liberty callback-ზე თანხა არ ემთხვევა შეკვეთას {order.OrderNumber}",
                order.Id,
                order.OrderNumber,
                "Liberty",
                request.Status,
                Details: $"მოვიდა: {request.Amount}; მოსალოდნელი: {expectedAmount}; transactioncode={request.TransactionCode}"),
                cancellationToken);
            return BuildCallbackResponse("-3", "Invalid amount", request.TransactionCode);
        }

        var payment = order.Payments
            .Where(payment => payment.Provider == PaymentProvider.LibertyPay)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefault();

        if (payment is not null && payment.Status == PaymentStatus.Paid)
        {
            return BuildCallbackResponse("1", "Duplicate transaction", request.TransactionCode);
        }

        var now = DateTimeOffset.UtcNow;
        var isCompleted = string.Equals(request.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase)
            || string.Equals(request.Status, "COMPLETE", StringComparison.OrdinalIgnoreCase);
        var isCheck = string.Equals(request.Status, "CHECK", StringComparison.OrdinalIgnoreCase);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.LibertyPay,
                Method = PaymentMethod.Liberty,
                Amount = order.Total,
                Currency = libertyPayOptions.Currency,
                ProviderOrderId = order.OrderNumber,
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.ProviderStatus = request.Status;
        payment.ProviderTransactionCode = string.IsNullOrWhiteSpace(request.TransactionCode)
            ? payment.ProviderTransactionCode
            : request.TransactionCode;
        payment.UpdatedAt = now;

        if (isCompleted)
        {
            order.PaymentStatus = PaymentStatus.Paid;
            order.Status = OrderStatus.Processing;
            order.PaymentTransactionCode = request.TransactionCode;
            order.UpdatedAt = now;
            payment.Status = PaymentStatus.Paid;
            payment.CompletedAt = now;
        }
        else if (isCheck)
        {
            order.PaymentStatus = PaymentStatus.PendingCallback;
            order.UpdatedAt = now;
            payment.Status = PaymentStatus.PendingCallback;
        }
        else
        {
            order.PaymentStatus = PaymentStatus.Failed;
            order.UpdatedAt = now;
            payment.Status = PaymentStatus.Failed;
            payment.CompletedAt = now;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            isCompleted ? "success" : isCheck ? "info" : "error",
            "payment",
            isCompleted ? "liberty_payment_paid" : isCheck ? "liberty_payment_check" : "liberty_payment_failed",
            isCompleted
                ? $"Liberty გადახდა წარმატებულია: {order.OrderNumber}"
                : isCheck
                    ? $"Liberty გადახდა დადასტურებას ელოდება: {order.OrderNumber}"
                    : $"Liberty გადახდა ჩავარდა: {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "Liberty",
            payment.Status.ToValue(),
            Details: $"bankStatus={request.Status}; transactioncode={request.TransactionCode}; amount={request.Amount}; paymethod={request.PayMethod}"),
            cancellationToken);

        return BuildCallbackResponse("0", "Ok", request.TransactionCode);
    }

    public async Task<RefundPaymentResponse?> RefundLibertyPaymentAsync(
        int orderId,
        RefundPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var payment = order.Payments
            .Where(payment => payment.Provider == PaymentProvider.LibertyPay && !string.IsNullOrWhiteSpace(payment.ProviderOrderId))
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefault();

        if (payment is null)
        {
            throw new InvalidOperationException("Order does not have a Liberty payment to refund.");
        }

        if (string.IsNullOrWhiteSpace(payment.ProviderTransactionCode) &&
            !string.IsNullOrWhiteSpace(order.PaymentTransactionCode))
        {
            payment.ProviderTransactionCode = order.PaymentTransactionCode;
        }

        var refundAmount = request.Amount ?? payment.Amount - payment.RefundedAmount;
        if (refundAmount <= 0)
        {
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        }

        if (payment.RefundedAmount + refundAmount > payment.Amount)
        {
            throw new InvalidOperationException("Refund amount cannot exceed payment amount.");
        }

        var refundCode = $"RF-{order.OrderNumber}-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
        var refundDescription = TrimToLength(
            string.IsNullOrWhiteSpace(request.Reason) ? $"Refund for {order.OrderNumber}" : request.Reason.Trim(),
            100);
        LibertyPayRefundResult refundResult;
        try
        {
            refundResult = await libertyPayRefundClient.RefundAsync(
                order.OrderNumber,
                refundCode,
                refundDescription,
                int.Parse(ToTetri(refundAmount)),
                cancellationToken);
        }
        catch (Exception exception)
        {
            var exceptionDetails = exception is LibertyPayRefundException refundException
                ? BuildRefundLogDetails(
                    refundAmount,
                    refundCode,
                    order.OrderNumber,
                    refundException.RequestUrl,
                    refundException.RequestBody,
                    refundException.ResponseBody,
                    exception.Message)
                : $"error={exception.Message}";
            await activityLogService.AddAsync(new ActivityLogEntry(
                "error",
                "payment",
                "liberty_refund_error",
                $"Liberty refund ვერ შესრულდა: {order.OrderNumber}",
                order.Id,
                order.OrderNumber,
                "Liberty",
                payment.Status.ToValue(),
                Details: TrimToLength(exceptionDetails, 4000)),
                cancellationToken);
            throw;
        }

        if (!refundResult.IsSuccess)
        {
            var detail = string.IsNullOrWhiteSpace(refundResult.RawResponse)
                ? string.Empty
                : $" Response: {TrimToLength(refundResult.RawResponse.ReplaceLineEndings(" "), 500)}";
            await activityLogService.AddAsync(new ActivityLogEntry(
                "error",
                "payment",
                "liberty_refund_failed",
                $"Liberty refund უარყოფილია: {order.OrderNumber}",
                order.Id,
                order.OrderNumber,
                "Liberty",
                refundResult.StatusCode,
                Details: TrimToLength(BuildRefundLogDetails(
                    refundAmount,
                    refundCode,
                    order.OrderNumber,
                    refundResult.RequestUrl,
                    refundResult.RequestBody,
                    refundResult.RawResponse,
                    $"status={refundResult.StatusCode}") + detail, 4000)),
                cancellationToken);
            throw new InvalidOperationException($"Liberty refund failed with status {refundResult.StatusCode}.{detail}");
        }

        var now = DateTimeOffset.UtcNow;
        payment.RefundedAmount += refundAmount;
        payment.RefundReason = string.IsNullOrWhiteSpace(request.Reason) ? payment.RefundReason : request.Reason.Trim();
        payment.ProviderRefundStatus = refundResult.StatusCode;
        payment.ProviderTransactionCode = string.IsNullOrWhiteSpace(refundResult.TransactionCode)
            ? payment.ProviderTransactionCode
            : refundResult.TransactionCode;
        payment.RefundedAt = now;
        payment.UpdatedAt = now;

        var paymentStatus = payment.RefundedAmount >= payment.Amount
            ? PaymentStatus.Refunded
            : PaymentStatus.PartiallyRefunded;

        payment.Status = paymentStatus;
        order.PaymentStatus = paymentStatus;
        if (paymentStatus == PaymentStatus.Refunded)
        {
            order.Status = OrderStatus.Cancelled;
        }
        order.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "success",
            "payment",
            "liberty_refund_success",
            $"Liberty refund წარმატებულია: {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "Liberty",
            payment.ProviderRefundStatus,
            Details: TrimToLength(BuildRefundLogDetails(
                refundAmount,
                refundCode,
                order.OrderNumber,
                refundResult.RequestUrl,
                refundResult.RequestBody,
                refundResult.RawResponse,
                $"დაბრუნდა: {refundAmount:0.##} ₾; სულ დაბრუნებული: {payment.RefundedAmount:0.##} ₾"), 4000)),
            cancellationToken);

        return new RefundPaymentResponse(
            order.Id,
            order.OrderNumber,
            order.Status.ToValue(),
            order.PaymentStatus.ToValue(),
            payment.RefundedAmount,
            payment.Amount,
            payment.ProviderRefundStatus);
    }






    private async Task<PaymentStatusDto?> UpdateReturnStatusAsync(
        string? paymentOrderCode,
        PaymentStatus paymentStatus,
        OrderStatus? orderStatus,
        string? transactionCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentOrderCode))
        {
            await activityLogService.AddAsync(new ActivityLogEntry(
                "error",
                "payment",
                "liberty_return_missing_order",
                "Liberty დაბრუნდა შეკვეთის კოდის გარეშე",
                Provider: "Liberty",
                Status: paymentStatus.ToValue(),
                Details: string.IsNullOrWhiteSpace(transactionCode) ? null : $"transactioncode={transactionCode.Trim()}"),
                cancellationToken);
            return null;
        }

        var order = await dbContext.Orders
            .FirstOrDefaultAsync(order => order.LibertyPaymentOrderCode == paymentOrderCode, cancellationToken);

        if (order is null)
        {
            await activityLogService.AddAsync(new ActivityLogEntry(
                "error",
                "payment",
                "liberty_return_order_not_found",
                $"Liberty დაბრუნდა, მაგრამ შეკვეთა ვერ მოიძებნა: {paymentOrderCode}",
                OrderNumber: paymentOrderCode,
                Provider: "Liberty",
                Status: paymentStatus.ToValue(),
                Details: string.IsNullOrWhiteSpace(transactionCode) ? null : $"transactioncode={transactionCode.Trim()}"),
                cancellationToken);
            return null;
        }

        order.PaymentStatus = paymentStatus;
        if (paymentStatus == PaymentStatus.Paid && !string.IsNullOrWhiteSpace(transactionCode))
        {
            order.PaymentTransactionCode = transactionCode.Trim();
        }
        if (orderStatus is not null)
        {
            order.Status = orderStatus.Value;
        }
        order.UpdatedAt = DateTimeOffset.UtcNow;

        var payment = await dbContext.Payments
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(payment => payment.ProviderOrderId == paymentOrderCode, cancellationToken);

        if (payment is not null)
        {
            payment.Status = paymentStatus;
            if (paymentStatus == PaymentStatus.Paid && !string.IsNullOrWhiteSpace(transactionCode))
            {
                payment.ProviderTransactionCode = transactionCode.Trim();
            }
            payment.UpdatedAt = order.UpdatedAt;
            payment.CompletedAt = paymentStatus is PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Cancelled
                ? order.UpdatedAt
                : null;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            paymentStatus == PaymentStatus.Paid ? "success" : "warning",
            "payment",
            paymentStatus == PaymentStatus.Paid
                ? "liberty_return_paid"
                : paymentStatus == PaymentStatus.Cancelled
                    ? "liberty_return_cancelled"
                    : "liberty_return_failed",
            paymentStatus == PaymentStatus.Paid
                ? $"Liberty დაბრუნება წარმატებულია: {order.OrderNumber}"
                : paymentStatus == PaymentStatus.Cancelled
                    ? $"Liberty გადახდა გაუქმდა მომხმარებლისგან: {order.OrderNumber}"
                    : $"Liberty გადახდა დაბრუნდა შეცდომით: {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "Liberty",
            paymentStatus.ToValue(),
            Details: string.IsNullOrWhiteSpace(transactionCode) ? null : $"transactioncode={transactionCode.Trim()}"),
            cancellationToken);

        return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    public async Task<PaymentStatusDto?> HandleBogInstallmentCallbackAsync(
        BogInstallmentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ShopOrderId))
        {
            return null;
        }

        var order = await dbContext.Orders
            .Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.OrderNumber == request.ShopOrderId, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var status = request.Status?.Trim().ToLowerInvariant();
        var paymentStatus = status switch
        {
            "success" => PaymentStatus.Paid,
            "reverse_success" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Failed
        };
        var orderStatus = status switch
        {
            "success" => OrderStatus.Processing,
            "reverse_success" => OrderStatus.Cancelled,
            "error" => OrderStatus.Cancelled,
            _ => order.Status
        };
        var now = DateTimeOffset.UtcNow;

        order.PaymentStatus = paymentStatus;
        order.Status = orderStatus;
        order.UpdatedAt = now;

        var payment = order.Payments
            .Where(payment => payment.Provider == PaymentProvider.BogInstallment)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefault();

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.BogInstallment,
                Method = PaymentMethod.BogInstallmentPlan,
                Amount = order.Total,
                Currency = bogInstallmentOptions.Currency,
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.Status = paymentStatus;
        payment.ProviderOrderId = string.IsNullOrWhiteSpace(request.OrderId) ? payment.ProviderOrderId : request.OrderId;
        payment.ProviderStatus = request.Status;
        payment.ProviderTransactionCode = request.OrderId;
        payment.CompletedAt = paymentStatus is PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Cancelled
            ? now
            : null;
        payment.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            paymentStatus == PaymentStatus.Paid ? "success" : "warning",
            "payment",
            paymentStatus == PaymentStatus.Paid ? "bog_installment_paid" : "bog_installment_failed",
            paymentStatus == PaymentStatus.Paid
                ? $"BOG განვადება წარმატებულია: {order.OrderNumber}"
                : $"BOG განვადება ვერ დასრულდა: {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "BOG",
            paymentStatus.ToValue(),
            Details: $"bankStatus={request.Status}; bogOrder={request.OrderId}"),
            cancellationToken);

        return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    private async Task AddPaymentIssueLogAsync(
        string eventName,
        string message,
        string? orderNumber,
        string provider,
        string? status,
        string? details,
        CancellationToken cancellationToken)
    {
        await activityLogService.AddAsync(new ActivityLogEntry(
            "error",
            "payment",
            eventName,
            message,
            OrderNumber: orderNumber,
            Provider: provider,
            Status: status,
            Details: details),
            cancellationToken);
    }

    private string BuildLibertyPayUrl(Order order)
    {
        EnsureLibertyPayConfigured();

        var merchant = libertyPayOptions.Merchant.Trim();
        var key = libertyPayOptions.Key.Trim();
        var currency = libertyPayOptions.Currency.Trim();
        var language = libertyPayOptions.Language.Trim();
        var testMode = libertyPayOptions.TestMode.Trim();
        var amount = ToTetri(order.Total);
        var description = TrimToLength($"AgroTrade order {order.OrderNumber}", 100);
        var clientName = TrimToLength($"{order.CustomerFirstName} {order.CustomerLastName}".Trim(), 100);
        var customData = order.Id.ToString();
        var isPreauth = string.Empty;
        var check = Sha256(
            key +
            merchant +
            order.OrderNumber +
            amount +
            currency +
            description +
            clientName +
            customData +
            language +
            testMode +
            isPreauth);

        var values = new Dictionary<string, string?>
        {
            ["merchant"] = merchant,
            ["ordercode"] = order.OrderNumber,
            ["amount"] = amount,
            ["currency"] = currency,
            ["description"] = description,
            ["clientname"] = clientName,
            ["customdata"] = customData,
            ["lng"] = language,
            ["testmode"] = testMode,
            ["check"] = check
        };

        var publicApiBaseUrl = libertyPayOptions.PublicApiBaseUrl.Trim().TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(publicApiBaseUrl))
        {
            values["successurl"] = $"{publicApiBaseUrl}/api/liberty-pay/approve?";
            values["errorurl"] = $"{publicApiBaseUrl}/api/liberty-pay/decline?";
            values["cancelurl"] = $"{publicApiBaseUrl}/api/liberty-pay/cancel?";
            values["callbackurl"] = $"{publicApiBaseUrl}/api/liberty-pay/callback";
        }

        var separator = libertyPayOptions.PayUrl.Contains('?') ? '&' : '?';
        var query = string.Join("&", values
            .Where(item => item.Value is not null)
            .Select(item => $"{Uri.EscapeDataString(item.Key)}={Uri.EscapeDataString(item.Value ?? string.Empty)}"));

        return $"{libertyPayOptions.PayUrl}{separator}{query}";
    }

    private bool IsCallbackCheckValid(LibertyPayCallbackRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Check))
        {
            return false;
        }

        var payedAmount = string.Equals(request.PayMethod, "PAYTERM", StringComparison.OrdinalIgnoreCase)
            ? request.PayedAmount ?? string.Empty
            : string.Empty;

        var expectedCheck = Sha256(
            (request.Status ?? string.Empty) +
            (request.TransactionCode ?? string.Empty) +
            (request.Amount ?? string.Empty) +
            (request.Currency ?? string.Empty) +
            (request.OrderCode ?? string.Empty) +
            (request.PayMethod ?? string.Empty) +
            payedAmount +
            (request.CustomData ?? string.Empty) +
            (request.TestMode ?? string.Empty) +
            libertyPayOptions.Key);

        return string.Equals(expectedCheck, request.Check, StringComparison.OrdinalIgnoreCase);
    }

    private LibertyPayCallbackResponse BuildCallbackResponse(string resultCode, string resultDescription, string? transactionCode)
    {
        var check = Sha256(resultCode + resultDescription + (transactionCode ?? string.Empty) + libertyPayOptions.Key);
        var xml = $"""
            <result>
              <resultcode>{System.Security.SecurityElement.Escape(resultCode)}</resultcode>
              <resultdesc>{System.Security.SecurityElement.Escape(resultDescription)}</resultdesc>
              <check>{check}</check>
              <data></data>
            </result>
            """;

        return new LibertyPayCallbackResponse(xml, resultCode, resultDescription);
    }

    private void EnsureLibertyPayConfigured()
    {
        if (string.IsNullOrWhiteSpace(libertyPayOptions.PayUrl) ||
            string.IsNullOrWhiteSpace(libertyPayOptions.Merchant) ||
            string.IsNullOrWhiteSpace(libertyPayOptions.Key))
        {
            throw new InvalidOperationException("Liberty PAY URL, merchant and key must be configured.");
        }
    }

    private static string ToTetri(decimal amount)
    {
        return decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero).ToString("0");
    }

    private static string TrimToLength(string value, int maxLength)
    {
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string BuildRefundLogDetails(
        decimal refundAmount,
        string refundCode,
        string orderNumber,
        string requestUrl,
        string requestBody,
        string? responseBody,
        string? note = null)
    {
        var normalizedRequest = requestBody.ReplaceLineEndings(" ").Trim();
        var normalizedResponse = string.IsNullOrWhiteSpace(responseBody)
            ? null
            : responseBody.ReplaceLineEndings(" ").Trim();

        var builder = new StringBuilder()
            .Append("orderNumber=").Append(orderNumber)
            .Append("; refundCode=").Append(refundCode)
            .Append("; amountGel=").Append(refundAmount.ToString("0.##"))
            .Append("; amountTetri=").Append(ToTetri(refundAmount))
            .Append("; requestUrl=").Append(requestUrl)
            .Append("; requestBody=").Append(normalizedRequest);

        if (!string.IsNullOrWhiteSpace(normalizedResponse))
        {
            builder.Append("; responseBody=").Append(normalizedResponse);
        }

        if (!string.IsNullOrWhiteSpace(note))
        {
            builder.Append("; note=").Append(note.ReplaceLineEndings(" ").Trim());
        }

        return builder.ToString();
    }

    private static string Sha256(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

}


