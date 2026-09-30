using System.Text.Json.Serialization;

namespace AgroTrade.Api.Services;

public class WooCommerceProduct
{
    public long Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Slug { get; set; } = string.Empty;
    public string? Permalink { get; set; }
    public string? Sku { get; set; }
    public string? Price { get; set; }

    [JsonPropertyName("regular_price")]
    public string? RegularPrice { get; set; }

    [JsonPropertyName("sale_price")]
    public string? SalePrice { get; set; }

    [JsonPropertyName("stock_status")]
    public string? StockStatus { get; set; }

    [JsonPropertyName("stock_quantity")]
    public int? StockQuantity { get; set; }

    public string? Description { get; set; }

    [JsonPropertyName("short_description")]
    public string? ShortDescription { get; set; }

    public List<WooCommerceImage> Images { get; set; } = [];
    public List<WooCommerceCategory> Categories { get; set; } = [];
}

public class WooCommerceImage
{
    public string? Src { get; set; }
}

public class WooCommerceCategory
{
    public string Name { get; set; } = string.Empty;
}
