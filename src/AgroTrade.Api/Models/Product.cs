namespace AgroTrade.Api.Models;

public class Product
{
    public int Id { get; set; }
    public long? WooCommerceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Permalink { get; set; }
    public string? Sku { get; set; }
    public decimal? Price { get; set; }
    public decimal? RegularPrice { get; set; }
    public decimal? SalePrice { get; set; }
    public string? StockStatus { get; set; }
    public int? StockQuantity { get; set; }
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ImageUrl { get; set; }
    public int? CategoryId { get; set; }
    public Category? Category { get; set; }
    public string? CategoryNames { get; set; }
    public string? SpecificationsJson { get; set; }
    public string RawJson { get; set; } = string.Empty;
    public string Source { get; set; } = "local";
    public DateTimeOffset? SyncedAt { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}
