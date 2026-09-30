namespace AgroTrade.Api.Models;

public class Order
{
    public int Id { get; set; }
    public int? AppUserId { get; set; }
    public AppUser? AppUser { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public string Status { get; set; } = "new";
    public string PaymentMethod { get; set; } = string.Empty;
    public string DeliveryMethod { get; set; } = string.Empty;
    public string? PickupBranch { get; set; }
    public string CustomerFirstName { get; set; } = string.Empty;
    public string CustomerLastName { get; set; } = string.Empty;
    public string CustomerPhone { get; set; } = string.Empty;
    public string? CustomerEmail { get; set; }
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public string? Note { get; set; }
    public decimal Subtotal { get; set; }
    public decimal DeliveryPrice { get; set; }
    public decimal Total { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public List<OrderItem> Items { get; set; } = [];
}
