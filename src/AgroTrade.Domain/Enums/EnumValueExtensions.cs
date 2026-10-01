namespace AgroTrade.Domain.Enums;

public static class EnumValueExtensions
{
    public static string ToValue(this UserRole value) => value switch
    {
        UserRole.Admin => "admin",
        UserRole.Manager => "manager",
        _ => "user"
    };

    public static string ToValue(this OrderStatus value) => value switch
    {
        OrderStatus.Processing => "processing",
        OrderStatus.Completed => "completed",
        OrderStatus.Cancelled => "cancelled",
        _ => "new"
    };

    public static string ToValue(this PaymentMethod value) => value switch
    {
        PaymentMethod.Liberty => "liberty",
        PaymentMethod.BogPieceByPiece => "bog_piece_by_piece",
        PaymentMethod.BogInstallmentPlan => "bog_installment_plan",
        _ => "cash"
    };

    public static string ToValue(this PaymentProvider value) => value switch
    {
        PaymentProvider.BogInstallment => "bog_installment",
        _ => "liberty_pay"
    };

    public static string ToValue(this PaymentStatus value) => value switch
    {
        PaymentStatus.Pending => "pending",
        PaymentStatus.PendingCallback => "pending_callback",
        PaymentStatus.Paid => "paid",
        PaymentStatus.Failed => "failed",
        PaymentStatus.Cancelled => "cancelled",
        PaymentStatus.Refunded => "refunded",
        PaymentStatus.PartiallyRefunded => "partially_refunded",
        _ => "unpaid"
    };

    public static string ToValue(this DeliveryMethod value) => value switch
    {
        DeliveryMethod.Pickup => "pickup",
        _ => "courier"
    };

    public static string ToValue(this StockStatus value) => value switch
    {
        StockStatus.OutOfStock => "outofstock",
        StockStatus.OnBackorder => "onbackorder",
        _ => "instock"
    };

    public static UserRole ToUserRole(this string? value) => Normalize(value) switch
    {
        "admin" => UserRole.Admin,
        "manager" => UserRole.Manager,
        _ => UserRole.User
    };

    public static OrderStatus ToOrderStatus(this string? value) => Normalize(value) switch
    {
        "processing" => OrderStatus.Processing,
        "completed" => OrderStatus.Completed,
        "cancelled" => OrderStatus.Cancelled,
        "canceled" => OrderStatus.Cancelled,
        _ => OrderStatus.New
    };

    public static bool TryToOrderStatus(this string? value, out OrderStatus status)
    {
        status = Normalize(value) switch
        {
            "new" => OrderStatus.New,
            "processing" => OrderStatus.Processing,
            "completed" => OrderStatus.Completed,
            "cancelled" => OrderStatus.Cancelled,
            "canceled" => OrderStatus.Cancelled,
            _ => default
        };

        return !string.IsNullOrWhiteSpace(value) &&
            Normalize(value) is "new" or "processing" or "completed" or "cancelled" or "canceled";
    }

    public static PaymentMethod ToPaymentMethod(this string? value) => Normalize(value) switch
    {
        "liberty" => PaymentMethod.Liberty,
        "bog_piece_by_piece" => PaymentMethod.BogPieceByPiece,
        "bog-parts" => PaymentMethod.BogPieceByPiece,
        "bog_installment_plan" => PaymentMethod.BogInstallmentPlan,
        "bog-installment" => PaymentMethod.BogInstallmentPlan,
        _ => PaymentMethod.Cash
    };

    public static PaymentProvider ToPaymentProvider(this string? value) => Normalize(value) switch
    {
        "bog_installment" => PaymentProvider.BogInstallment,
        _ => PaymentProvider.LibertyPay
    };

    public static PaymentStatus ToPaymentStatus(this string? value) => Normalize(value) switch
    {
        "pending" => PaymentStatus.Pending,
        "pending_callback" => PaymentStatus.PendingCallback,
        "paid" => PaymentStatus.Paid,
        "failed" => PaymentStatus.Failed,
        "cancelled" => PaymentStatus.Cancelled,
        "canceled" => PaymentStatus.Cancelled,
        "refunded" => PaymentStatus.Refunded,
        "partially_refunded" => PaymentStatus.PartiallyRefunded,
        _ => PaymentStatus.Unpaid
    };

    public static DeliveryMethod ToDeliveryMethod(this string? value) => Normalize(value) switch
    {
        "pickup" => DeliveryMethod.Pickup,
        _ => DeliveryMethod.Courier
    };

    public static StockStatus? ToStockStatusOrNull(this string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return Normalize(value) switch
        {
            "outofstock" => StockStatus.OutOfStock,
            "onbackorder" => StockStatus.OnBackorder,
            _ => StockStatus.InStock
        };
    }

    private static string Normalize(string? value) => value?.Trim().ToLowerInvariant() ?? string.Empty;
}

