namespace AgroTrade.Domain.Enums;

public enum UserRole
{
    User,
    Admin,
    Manager
}

public enum OrderStatus
{
    New,
    Processing,
    Completed,
    Cancelled
}

public enum PaymentMethod
{
    Cash,
    Liberty,
    BogPieceByPiece,
    BogInstallmentPlan,
    
}

public enum PaymentStatus
{
    Unpaid,
    Pending,
    PendingCallback,
    Paid,
    Failed,
    Cancelled,
    Refunded,
    PartiallyRefunded
}

public enum PaymentProvider
{
    LibertyPay,
    BogInstallment
}

public enum DeliveryMethod
{
    Courier,
    Pickup
}

public enum StockStatus
{
    InStock,
    OutOfStock,
    OnBackorder
}

