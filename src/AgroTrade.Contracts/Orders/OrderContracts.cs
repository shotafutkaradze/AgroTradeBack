namespace AgroTrade.Contracts.Orders;

public record CreateOrderRequest(
    int? UserId,
    OrderCustomerRequest Customer,
    OrderDeliveryRequest Delivery,
    string PaymentMethod,
    decimal DeliveryPrice,
    List<CreateOrderItemRequest> Items);

public record UpdateOrderStatusRequest(string Status);
public record OrderCustomerRequest(string FirstName, string LastName, string Phone, string? Email);
public record OrderDeliveryRequest(string Method, string? City, string? Address, string? PickupBranch, string? Note);

public record CreateOrderItemRequest(
    int? ProductId,
    string ProductName,
    string? ProductSku,
    string? ProductImageUrl,
    string? ProductCategory,
    decimal UnitPrice,
    int Quantity);

public record OrderDto(
    int Id,
    string OrderNumber,
    string Status,
    string PaymentMethod,
    string PaymentStatus,
    string? PaymentTransactionCode,
    string? LibertyPaymentOrderCode,
    string DeliveryMethod,
    string? PickupBranch,
    OrderCustomerDto Customer,
    OrderDeliveryDto Delivery,
    decimal Subtotal,
    decimal DeliveryPrice,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<OrderItemDto> Items);

public record OrderCustomerDto(string FirstName, string LastName, string Phone, string? Email);
public record OrderDeliveryDto(string Method, string City, string Address, string? PickupBranch, string? Note);

public record OrderItemDto(
    int Id,
    int? ProductId,
    string ProductName,
    string? ProductSku,
    string? ProductImageUrl,
    string? ProductCategory,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);

