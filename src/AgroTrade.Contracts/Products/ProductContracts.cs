namespace AgroTrade.Contracts.Products;

public record CreateProductRequest(
    string Name,
    string? Slug,
    string? Sku,
    decimal? Price,
    decimal? RegularPrice,
    decimal? SalePrice,
    string? StockStatus,
    int? StockQuantity,
    string? Description,
    string? ShortDescription,
    string? ImageUrl,
    int? BrandId,
    string? CategoryNames,
    int? CategoryId,
    string? SpecificationsJson);

public record ProductCategoryDto(int Id, string Name, int? ParentId);
public record ProductBrandDto(int Id, string Name);

public record ProductDto(
    int Id,
    string Name,
    string Slug,
    string? Permalink,
    string? Sku,
    decimal? Price,
    decimal? RegularPrice,
    decimal? SalePrice,
    string? StockStatus,
    int? StockQuantity,
    string? Description,
    string? ShortDescription,
    string? ImageUrl,
    int? BrandId,
    ProductBrandDto? Brand,
    int? CategoryId,
    string? CategoryNames,
    ProductCategoryDto? Category,
    string? SpecificationsJson,
    bool IsHidden,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public record UpdateProductVisibilityRequest(bool IsHidden);
