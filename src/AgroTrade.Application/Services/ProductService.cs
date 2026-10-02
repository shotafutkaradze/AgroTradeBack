using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Products;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Services;

public interface IProductService
{
    Task<IReadOnlyList<ProductDto>> GetAsync(string? search, bool includeHidden, CancellationToken cancellationToken);
    Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductDto?> UpdateAsync(int id, CreateProductRequest request, CancellationToken cancellationToken);
    Task<ProductDto?> UpdateVisibilityAsync(int id, bool isHidden, CancellationToken cancellationToken);
}

public class ProductService(IAgroTradeDbContext dbContext) : IProductService
{
    public async Task<IReadOnlyList<ProductDto>> GetAsync(string? search, bool includeHidden, CancellationToken cancellationToken)
    {
        IQueryable<Product> query = dbContext.Products.AsNoTracking()
            .Include(product => product.Brand)
            .Include(product => product.Category);

        if (!includeHidden)
        {
            query = query.Where(product => !product.IsHidden);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(normalizedSearch) ||
                (product.Sku != null && product.Sku.ToLower().Contains(normalizedSearch)) ||
                (product.Brand != null && product.Brand.Name.ToLower().Contains(normalizedSearch)) ||
                (product.CategoryNames != null && product.CategoryNames.ToLower().Contains(normalizedSearch)));
        }

        var products = await query
            .OrderByDescending(product => product.UpdatedAt)
            .ThenBy(product => product.Name)
            .ToListAsync(cancellationToken);

        return products.Select(ToDto).ToList();
    }

    public async Task<ProductDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.Brand)
            .Include(product => product.Category)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

        return product is null ? null : ToDto(product);
    }

    public async Task<ProductDto> CreateAsync(CreateProductRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = string.IsNullOrWhiteSpace(request.Slug) ? ToSlug(request.Name) : ToSlug(request.Slug),
            Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim(),
            Price = request.Price,
            RegularPrice = request.RegularPrice,
            SalePrice = request.SalePrice,
            StockStatus = request.StockStatus.ToStockStatusOrNull() ?? StockStatus.InStock,
            StockQuantity = request.StockQuantity,
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            ImageUrl = request.ImageUrl,
            BrandId = request.BrandId,
            CategoryId = request.CategoryId,
            CategoryNames = request.CategoryNames,
            SpecificationsJson = request.SpecificationsJson,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(product.Id, cancellationToken) ?? ToDto(product);
    }

    public async Task<ProductDto?> UpdateAsync(int id, CreateProductRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var product = await dbContext.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.Name = request.Name.Trim();
        product.Slug = string.IsNullOrWhiteSpace(request.Slug) ? ToSlug(request.Name) : ToSlug(request.Slug);
        product.Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();
        product.Price = request.Price;
        product.RegularPrice = request.RegularPrice;
        product.SalePrice = request.SalePrice;
        product.StockStatus = request.StockStatus.ToStockStatusOrNull() ?? StockStatus.InStock;
        product.StockQuantity = request.StockQuantity;
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        product.ImageUrl = request.ImageUrl;
        product.BrandId = request.BrandId;
        product.CategoryId = request.CategoryId;
        product.CategoryNames = request.CategoryNames;
        product.SpecificationsJson = request.SpecificationsJson;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(product.Id, cancellationToken) ?? ToDto(product);
    }

    public async Task<ProductDto?> UpdateVisibilityAsync(int id, bool isHidden, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FirstOrDefaultAsync(product => product.Id == id, cancellationToken);
        if (product is null)
        {
            return null;
        }

        product.IsHidden = isHidden;
        product.UpdatedAt = DateTimeOffset.UtcNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await GetByIdAsync(product.Id, cancellationToken) ?? ToDto(product);
    }

    private static void Validate(CreateProductRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Product name is required.");
        }

        if (request.Price < 0 || request.RegularPrice < 0 || request.SalePrice < 0 || request.StockQuantity < 0)
        {
            throw new ArgumentException("Price and stock values cannot be negative.");
        }
    }

    private static string ToSlug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        return string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
    }

    private static ProductDto ToDto(Product product)
    {
        return new ProductDto(
            product.Id,
            product.Name,
            product.Slug,
            product.Permalink,
            product.Sku,
            product.Price,
            product.RegularPrice,
            product.SalePrice,
            product.StockStatus?.ToValue(),
            product.StockQuantity,
            product.Description,
            product.ShortDescription,
            product.ImageUrl,
            product.BrandId,
            product.Brand is null ? null : new ProductBrandDto(product.Brand.Id, product.Brand.Name),
            product.CategoryId,
            product.CategoryNames,
            product.Category is null ? null : new ProductCategoryDto(product.Category.Id, product.Category.Name, product.Category.ParentId),
            product.SpecificationsJson,
            product.IsHidden,
            product.CreatedAt,
            product.UpdatedAt);
    }
}
