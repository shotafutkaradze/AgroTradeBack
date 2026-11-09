using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Payments;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;

namespace AgroTrade.Application.Services;

public interface IPaymentService
{
    Task<PaymentStatusDto?> GetLibertyStatusAsync(int orderId, string? accessToken, CancellationToken cancellationToken);
    Task<PaymentStatusDto?> GetBogStatusAsync(int orderId, string? accessToken, CancellationToken cancellationToken);
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
    Task<TbcInstallmentPaymentStartResponse?> StartTbcInstallmentAsync(
        int orderId,
        TbcInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken);
    Task<PaymentStatusDto?> HandleBogInstallmentCallbackAsync(
        BogInstallmentCallbackRequest request,
        CancellationToken cancellationToken);
    Task<PaymentStatusDto?> HandleTbcInstallmentCallbackAsync(
        TbcInstallmentCallbackRequest request,
        CancellationToken cancellationToken);
}

public class PaymentService(
    IAgroTradeDbContext dbContext,
    ILibertyPayRefundClient libertyPayRefundClient,
    IBogInstallmentClient bogInstallmentClient,
    ITbcInstallmentClient tbcInstallmentClient,
    IOrderEmailService orderEmailService,
    IActivityLogService activityLogService,
    ILogger<PaymentService> logger,
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

        EnsureLibertyPayConfigured();
        if (order.Total <= 0) throw new InvalidOperationException("Payment amount must be positive.");
        if (order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded || order.Status == OrderStatus.Cancelled)
            throw new InvalidOperationException("Order cannot start a Liberty payment.");
        var existing = await dbContext.Payments.FirstOrDefaultAsync(payment => payment.OrderId == order.Id &&
            payment.Provider == PaymentProvider.LibertyPay, cancellationToken);
        if (existing is null || !BogCheckoutSecurity.Verify(request.PaymentAccessToken, existing.CheckoutTokenHash))
            throw new UnauthorizedAccessException();
        if (existing.Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded or PaymentStatus.Failed or PaymentStatus.Cancelled)
            throw new InvalidOperationException("Order cannot restart this Liberty payment.");
        if (!string.IsNullOrWhiteSpace(existing.ProviderOrderId))
            return new LibertyPayPaymentStartResponse(order.Id, order.OrderNumber, existing.ProviderOrderId,
                existing.ProviderStatus ?? "CREATED", null, BuildLibertyPayUrl(order));

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
                payment.Provider == PaymentProvider.LibertyPay,
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

        if (payment.Status is PaymentStatus.Failed or PaymentStatus.Cancelled)
            throw new InvalidOperationException("Create a new checkout for a failed Liberty payment.");
        payment.Status = PaymentStatus.Pending;
        payment.Amount = order.Total;
        payment.Currency = libertyPayOptions.Currency;
        payment.ProviderOrderId = orderCode;
        payment.ProviderOrderPassword = null;
        payment.ProviderStatus = "CREATED";
        payment.UpdatedAt = now;
        payment.Version++;

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
        int orderId, BogInstallmentPaymentStartRequest request, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.Include(order => order.Items).Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (order is null) return null;
        var payment = order.Payments.SingleOrDefault(payment => payment.Provider == PaymentProvider.BogInstallment);
        if (payment is null || !BogCheckoutSecurity.Verify(request.PaymentAccessToken, payment.CheckoutTokenHash))
            throw new UnauthorizedAccessException();
        if (order.PaymentMethod is not PaymentMethod.BogInstallmentPlan and not PaymentMethod.BogPieceByPiece ||
            order.Status == OrderStatus.Cancelled || order.PaymentStatus is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException("Order cannot start a BOG payment.");
        if (request.InstallmentMonth is null or <= 0 || string.IsNullOrWhiteSpace(request.InstallmentType) || request.InstallmentType.Length > 100)
            throw new ArgumentException("Select month and discount_code in the BOG calculator.");
        var month = request.InstallmentMonth.Value;
        var type = request.InstallmentType.Trim();
        var selectedMethod = request.PaymentMethod switch
        {
            null => order.PaymentMethod,
            "bog-installment" or "bog_installment_plan" => PaymentMethod.BogInstallmentPlan,
            "bog-parts" or "bog_piece_by_piece" => PaymentMethod.BogPieceByPiece,
            _ => throw new ArgumentException("Unsupported BOG payment method.")
        };
        if (payment.CheckoutIdempotencyKey.HasValue && (payment.InstallmentMonth != month || payment.InstallmentType != type || payment.Method != selectedMethod))
            throw new InvalidOperationException("Payment has already started with different loan conditions.");
        if (!string.IsNullOrWhiteSpace(payment.ProviderOrderId))
            return new BogInstallmentPaymentStartResponse(order.Id, order.OrderNumber, payment.ProviderOrderId,
                payment.ProviderStatus ?? "created", payment.RedirectUrl ?? string.Empty);

        if (!payment.CheckoutIdempotencyKey.HasValue)
        {
            order.PaymentMethod = selectedMethod;
            payment.Method = selectedMethod;
            payment.CheckoutIdempotencyKey = Guid.NewGuid();
            payment.InstallmentMonth = month;
            payment.InstallmentType = type;
            payment.Version++;
            payment.UpdatedAt = DateTimeOffset.UtcNow;
            // Persist before the network request so retries use the same BOG Idempotency-Key.
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        var bankOrder = await bogInstallmentClient.CreateOrderAsync(order, month, type, cancellationToken, payment.CheckoutIdempotencyKey);
        payment.ProviderOrderId = bankOrder.OrderId;
        payment.ProviderStatus = bankOrder.Status;
        payment.RedirectUrl = bankOrder.RedirectUrl;
        payment.Currency = bogInstallmentOptions.Currency;
        payment.Amount = order.Total;
        payment.Status = PaymentStatus.Pending;
        payment.Version++;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("BOG checkout created for local order {OrderId} with method {Method}", order.Id, payment.Method);
        return new BogInstallmentPaymentStartResponse(order.Id, order.OrderNumber, bankOrder.OrderId, bankOrder.Status, bankOrder.RedirectUrl);
    }

    public async Task<PaymentStatusDto?> GetBogStatusAsync(int orderId, string? accessToken, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (order is null) return null;
        var payment = order.Payments.SingleOrDefault(payment => payment.Provider == PaymentProvider.BogInstallment);
        if (payment is null || !BogCheckoutSecurity.Verify(accessToken, payment.CheckoutTokenHash))
            throw new UnauthorizedAccessException();
        if (string.IsNullOrWhiteSpace(payment.ProviderOrderId))
            return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
        return await HandleBogInstallmentCallbackAsync(new BogInstallmentCallbackRequest(null,
            payment.ProviderOrderId, order.OrderNumber, null), cancellationToken);
    }

    public async Task<TbcInstallmentPaymentStartResponse?> StartTbcInstallmentAsync(
        int orderId,
        TbcInstallmentPaymentStartRequest request,
        CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == orderId, cancellationToken);
        if (order is null)
        {
            return null;
        }

        if (order.PaymentMethod != PaymentMethod.TbcInstallment)
        {
            throw new InvalidOperationException("Order payment method is not TBC installment.");
        }

        var tbcOrder = await tbcInstallmentClient.CreateOrderAsync(order, request.InstallmentMonth, cancellationToken);
        var now = DateTimeOffset.UtcNow;
        order.PaymentStatus = PaymentStatus.Pending;
        order.UpdatedAt = now;

        var payment = await dbContext.Payments
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefaultAsync(payment =>
                payment.OrderId == order.Id &&
                payment.Provider == PaymentProvider.TbcInstallment,
                cancellationToken);

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.TbcInstallment,
                Method = PaymentMethod.TbcInstallment,
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.Status = PaymentStatus.Pending;
        payment.Amount = order.Total;
        payment.Currency = "GEL";
        payment.ProviderOrderId = tbcOrder.OrderId;
        payment.ProviderStatus = tbcOrder.Status;
        payment.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "info",
            "payment",
            "tbc_installment_started",
            $"TBC განვადება დაიწყო შეკვეთაზე {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "TBC",
            payment.Status.ToValue(),
            Details: $"TBC order: {tbcOrder.OrderId}; თანხა: {order.Total:0.##} ₾"),
            cancellationToken);

        return new TbcInstallmentPaymentStartResponse(
            order.Id,
            order.OrderNumber,
            tbcOrder.OrderId,
            tbcOrder.Status,
            tbcOrder.RedirectUrl);
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
            .Include(order => order.Items)
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

        if (order.PaymentMethod != PaymentMethod.Liberty ||
            !string.Equals(request.Currency, libertyPayOptions.Currency.Trim(), StringComparison.OrdinalIgnoreCase) ||
            (request.TestMode == "1") != (libertyPayOptions.TestMode.Trim() == "1"))
            return BuildCallbackResponse("-3", "Order data mismatch", request.TransactionCode);
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

        if (payment is not null && payment.Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            return BuildCallbackResponse("1", "Duplicate transaction", request.TransactionCode);
        }

        var now = DateTimeOffset.UtcNow;
        var isCompleted = string.Equals(request.Status, "COMPLETED", StringComparison.OrdinalIgnoreCase);
        var isCheck = string.Equals(request.Status, "CHECK", StringComparison.OrdinalIgnoreCase);

        if (payment is null || payment.ProviderOrderId != request.OrderCode)
            return BuildCallbackResponse("-3", "Payment not started", request.TransactionCode);
        if (payment.Amount != order.Total || !string.Equals(payment.Currency, request.Currency, StringComparison.OrdinalIgnoreCase) ||
            (!string.IsNullOrEmpty(request.CustomData) && request.CustomData != order.Id.ToString()))
            return BuildCallbackResponse("-3", "Payment data mismatch", request.TransactionCode);
        if (isCompleted && string.IsNullOrWhiteSpace(request.TransactionCode))
            return BuildCallbackResponse("-3", "Missing transactioncode", request.TransactionCode);
        if (!isCompleted && !isCheck && request.Status is not "ERROR" and not "CANCELED")
            return BuildCallbackResponse("-3", "Unsupported status", request.TransactionCode);

        payment.ProviderStatus = request.Status;
        payment.ProviderTransactionCode = string.IsNullOrWhiteSpace(request.TransactionCode)
            ? payment.ProviderTransactionCode
            : request.TransactionCode;
        payment.UpdatedAt = now;

        if (isCompleted)
        {
            order.PaymentStatus = PaymentStatus.Paid;
            if (order.Status == OrderStatus.New) order.Status = OrderStatus.Processing;
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
            payment.Status = request.Status == "CANCELED" ? PaymentStatus.Cancelled : PaymentStatus.Failed;
            order.PaymentStatus = payment.Status;
            payment.CompletedAt = now;
        }

        payment.Version++;
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

        if (isCompleted)
        {
            await SendPaidOrderEmailAsync(order, cancellationToken);
        }

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

        if (payment.Status is not PaymentStatus.Paid and not PaymentStatus.PartiallyRefunded)
            throw new InvalidOperationException("Only confirmed payments can be refunded.");
        var refundAmount = request.Amount ?? payment.Amount - payment.RefundedAmount;
        if (refundAmount <= 0)
        {
            throw new InvalidOperationException("Refund amount must be greater than zero.");
        }

        if (payment.RefundedAmount + refundAmount > payment.Amount)
        {
            throw new InvalidOperationException("Refund amount cannot exceed payment amount.");
        }

        if (payment.ProviderRefundStatus == "REQUESTED")
            throw new InvalidOperationException("A refund is already pending. Verify its bank result before retrying.");
        payment.ProviderRefundStatus = "REQUESTED";
        payment.Version++;
        payment.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);

        var refundCode = $"RF-{Guid.NewGuid():N}";
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
            payment.ProviderRefundStatus = refundResult.StatusCode;
            payment.Version++;
            await dbContext.SaveChangesAsync(cancellationToken);
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
        payment.Version++;
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






    // Browser return URLs are informational. Only a validated callback may change payment state.
    private async Task<PaymentStatusDto?> UpdateReturnStatusAsync(string? paymentOrderCode,
        PaymentStatus paymentStatus, OrderStatus? orderStatus, string? transactionCode, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(paymentOrderCode)) return null;
        var order = await dbContext.Orders.AsNoTracking().FirstOrDefaultAsync(order =>
            order.PaymentMethod == PaymentMethod.Liberty && order.LibertyPaymentOrderCode == paymentOrderCode, cancellationToken);
        return order is null ? null : new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    public async Task<PaymentStatusDto?> GetLibertyStatusAsync(int orderId, string? accessToken, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders.AsNoTracking().Include(order => order.Payments)
            .FirstOrDefaultAsync(order => order.Id == orderId && order.PaymentMethod == PaymentMethod.Liberty, cancellationToken);
        if (order is null) return null;
        var payment = order.Payments.FirstOrDefault(payment => payment.Provider == PaymentProvider.LibertyPay);
        if (payment is null || !BogCheckoutSecurity.Verify(accessToken, payment.CheckoutTokenHash)) throw new UnauthorizedAccessException();
        return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    public async Task<PaymentStatusDto?> HandleBogInstallmentCallbackAsync(
        BogInstallmentCallbackRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.OrderId) || string.IsNullOrWhiteSpace(request.ShopOrderId)) return null;
        var order = await dbContext.Orders.Include(order => order.Payments).Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.OrderNumber == request.ShopOrderId, cancellationToken);
        if (order is null) return null;
        var payment = order.Payments.SingleOrDefault(payment => payment.Provider == PaymentProvider.BogInstallment &&
            payment.ProviderOrderId == request.OrderId);
        if (payment is null) return null;
        // The callback only triggers reconciliation. Bank receipt is the authoritative state.
        var details = await bogInstallmentClient.GetPaymentDetailsAsync(request.OrderId, cancellationToken);
        if (details.OrderId != payment.ProviderOrderId || details.ShopOrderId != order.OrderNumber ||
            details.Amount != payment.Amount || !string.Equals(details.Currency, payment.Currency, StringComparison.OrdinalIgnoreCase) ||
            ((!string.IsNullOrWhiteSpace(details.PaymentMethod) || details.Status == "completed") &&
             details.PaymentMethod != (payment.Method == PaymentMethod.BogPieceByPiece ? "bnpl" : "bog_loan")) ||
            (details.Status == "completed" && details.TransferredAmount != payment.Amount) ||
            details.RefundedAmount < 0 || details.RefundedAmount > payment.Amount ||
            (details.Status is "refunded" or "refunded_partially" && details.RefundedAmount is null) ||
            (details.Status == "refunded" && details.RefundedAmount != payment.Amount))
        {
            logger.LogWarning("BOG receipt mismatch for local order {OrderId}", order.Id);
            throw new InvalidOperationException("BOG payment details do not match the stored order.");
        }
        var nextStatus = details.Status switch
        {
            "completed" => PaymentStatus.Paid,
            "rejected" => PaymentStatus.Failed,
            "refunded" => PaymentStatus.Refunded,
            "refunded_partially" => PaymentStatus.PartiallyRefunded,
            _ => PaymentStatus.Pending
        };
        var wasPaid = payment.Status is PaymentStatus.Paid or PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded;
        var mayApply = !(wasPaid && nextStatus is PaymentStatus.Pending or PaymentStatus.Failed) &&
            !(payment.Status == PaymentStatus.Refunded && nextStatus != PaymentStatus.Refunded) &&
            !(payment.Status == PaymentStatus.PartiallyRefunded && nextStatus == PaymentStatus.Paid);
        if (!mayApply || (payment.ProviderStatus == details.Status && payment.Status == nextStatus &&
            payment.RefundedAmount == details.RefundedAmount.GetValueOrDefault()))
            return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
        var now = DateTimeOffset.UtcNow;
        payment.Status = nextStatus;
        payment.ProviderStatus = details.Status;
        payment.ProviderTransactionCode = details.TransactionId;
        payment.UpdatedAt = now;
        payment.Version++;
        if (nextStatus != PaymentStatus.Pending) payment.CompletedAt ??= now;
        if (nextStatus is PaymentStatus.Refunded or PaymentStatus.PartiallyRefunded)
        {
            payment.RefundedAmount = details.RefundedAmount.GetValueOrDefault();
            payment.RefundedAt = now;
        }
        order.PaymentStatus = nextStatus;
        if (nextStatus == PaymentStatus.Paid && order.Status == OrderStatus.New) order.Status = OrderStatus.Processing;
        order.UpdatedAt = now;
        // EF's transaction rolls back both updates if another callback wins the version check.
        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("BOG local order {OrderId} reconciled to {Status}", order.Id, details.Status);
        if (nextStatus == PaymentStatus.Paid && !wasPaid) await SendPaidOrderEmailAsync(order, cancellationToken);
        return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    public async Task<PaymentStatusDto?> HandleTbcInstallmentCallbackAsync(
        TbcInstallmentCallbackRequest request,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.ShopOrderId) && string.IsNullOrWhiteSpace(request.OrderId))
        {
            return null;
        }

        var order = await dbContext.Orders
            .Include(order => order.Payments)
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order =>
                    order.OrderNumber == request.ShopOrderId ||
                    order.Payments.Any(payment =>
                        payment.Provider == PaymentProvider.TbcInstallment &&
                        payment.ProviderOrderId == request.OrderId),
                cancellationToken);

        if (order is null)
        {
            return null;
        }

        var status = request.Status?.Trim().ToLowerInvariant();
        var paymentStatus = status switch
        {
            "success" or "approved" or "completed" or "paid" => PaymentStatus.Paid,
            "cancel" or "cancelled" or "canceled" => PaymentStatus.Cancelled,
            _ => PaymentStatus.Failed
        };
        var orderStatus = paymentStatus switch
        {
            PaymentStatus.Paid => OrderStatus.Processing,
            PaymentStatus.Cancelled => OrderStatus.Cancelled,
            PaymentStatus.Failed => OrderStatus.Cancelled,
            _ => order.Status
        };
        var now = DateTimeOffset.UtcNow;

        order.PaymentStatus = paymentStatus;
        order.Status = orderStatus;
        order.UpdatedAt = now;

        var payment = order.Payments
            .Where(payment => payment.Provider == PaymentProvider.TbcInstallment)
            .OrderByDescending(payment => payment.CreatedAt)
            .FirstOrDefault();

        if (payment is null)
        {
            payment = new Payment
            {
                OrderId = order.Id,
                Provider = PaymentProvider.TbcInstallment,
                Method = PaymentMethod.TbcInstallment,
                Amount = order.Total,
                Currency = "GEL",
                CreatedAt = now
            };
            dbContext.Payments.Add(payment);
        }

        payment.Status = paymentStatus;
        payment.ProviderOrderId = string.IsNullOrWhiteSpace(request.OrderId) ? payment.ProviderOrderId : request.OrderId;
        payment.ProviderStatus = request.Status;
        payment.ProviderTransactionCode = string.IsNullOrWhiteSpace(request.TransactionId) ? request.OrderId : request.TransactionId;
        payment.CompletedAt = paymentStatus is PaymentStatus.Paid or PaymentStatus.Failed or PaymentStatus.Cancelled
            ? now
            : null;
        payment.UpdatedAt = now;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            paymentStatus == PaymentStatus.Paid ? "success" : "warning",
            "payment",
            paymentStatus == PaymentStatus.Paid ? "tbc_installment_paid" : "tbc_installment_failed",
            paymentStatus == PaymentStatus.Paid
                ? $"TBC განვადება წარმატებულია: {order.OrderNumber}"
                : $"TBC განვადება ვერ დასრულდა: {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            "TBC",
            paymentStatus.ToValue(),
            Details: $"bankStatus={request.Status}; tbcOrder={request.OrderId}; transaction={request.TransactionId}"),
            cancellationToken);

        if (paymentStatus == PaymentStatus.Paid)
        {
            await SendPaidOrderEmailAsync(order, cancellationToken);
        }

        return new PaymentStatusDto(order.Id, order.OrderNumber, order.Status.ToValue(), order.PaymentStatus.ToValue());
    }

    private async Task SendPaidOrderEmailAsync(Order order, CancellationToken cancellationToken)
    {
        var alreadySent = await dbContext.ActivityLogs.AnyAsync(log =>
            log.OrderId == order.Id &&
            log.Event == "paid_order_email_sent",
            cancellationToken);
        if (alreadySent)
        {
            return;
        }

        try
        {
            await orderEmailService.SendOrderCreatedAsync(OrderService.ToDto(order), cancellationToken);
            await activityLogService.AddAsync(new ActivityLogEntry(
                "success",
                "order",
                "paid_order_email_sent",
                $"გადახდილი შეკვეთის email გაიგზავნა: {order.OrderNumber}",
                order.Id,
                order.OrderNumber,
                order.PaymentMethod.ToValue(),
                order.PaymentStatus.ToValue()),
                cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to send paid order email for {OrderNumber}", order.OrderNumber);
            await activityLogService.AddAsync(new ActivityLogEntry(
                "warning",
                "order",
                "paid_order_email_failed",
                $"გადახდილი შეკვეთის email ვერ გაიგზავნა: {order.OrderNumber}",
                order.Id,
                order.OrderNumber,
                order.PaymentMethod.ToValue(),
                order.PaymentStatus.ToValue(),
                Details: ex.Message),
                cancellationToken);
        }
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
        if (string.IsNullOrWhiteSpace(libertyPayOptions.Key) || request.Check?.Length != 64 ||
            string.IsNullOrWhiteSpace(request.Status) || string.IsNullOrWhiteSpace(request.Amount) ||
            string.IsNullOrWhiteSpace(request.Currency) || string.IsNullOrWhiteSpace(request.PayMethod))
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
            libertyPayOptions.Key.Trim());

        return CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(expectedCheck),
            Encoding.ASCII.GetBytes(request.Check!.ToLowerInvariant()));
    }

    private LibertyPayCallbackResponse BuildCallbackResponse(string resultCode, string resultDescription, string? transactionCode)
    {
        var check = Sha256(resultCode + resultDescription + (transactionCode ?? string.Empty) + libertyPayOptions.Key.Trim());
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
        return decimal.Round(amount * 100, 0, MidpointRounding.AwayFromZero).ToString("0", System.Globalization.CultureInfo.InvariantCulture);
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
        var normalizedRequest = System.Text.RegularExpressions.Regex.Replace(requestBody,
            @"(<(?:\w+:)?signature\b[^>]*>)[\s\S]*?(</(?:\w+:)?signature>)", "$1[REDACTED]$2",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase).ReplaceLineEndings(" ").Trim();
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


