using System.Globalization;
using System.Text.Json;
using AgroTrade.Api.Data;
using AgroTrade.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Api.Services;

public class ProductImportService(AgroTradeDbContext dbContext, IWooCommerceService wooCommerceService) : IProductImportService
{
    public async Task<int> SyncProductsAsync(CancellationToken cancellationToken)
    {
        var wooCommerceProducts = await wooCommerceService.GetProductsAsync(cancellationToken);
        var wooCommerceIds = wooCommerceProducts.Select(product => product.Id).ToArray();
        var existingProducts = await dbContext.Products
            .Where(product => product.WooCommerceId.HasValue && wooCommerceIds.Contains(product.WooCommerceId.Value))
            .ToDictionaryAsync(product => product.WooCommerceId!.Value, cancellationToken);

        var syncedAt = DateTimeOffset.UtcNow;

        foreach (var wooCommerceProduct in wooCommerceProducts)
        {
            if (!existingProducts.TryGetValue(wooCommerceProduct.Id, out var product))
            {
                product = new Product
                {
                    WooCommerceId = wooCommerceProduct.Id
                };

                dbContext.Products.Add(product);
            }

            product.Name = wooCommerceProduct.Name;
            product.Slug = wooCommerceProduct.Slug;
            product.Permalink = wooCommerceProduct.Permalink;
            product.Sku = wooCommerceProduct.Sku;
            product.Price = ParsePrice(wooCommerceProduct.Price);
            product.RegularPrice = ParsePrice(wooCommerceProduct.RegularPrice);
            product.SalePrice = ParsePrice(wooCommerceProduct.SalePrice);
            product.StockStatus = wooCommerceProduct.StockStatus;
            product.StockQuantity = wooCommerceProduct.StockQuantity;
            product.Description = wooCommerceProduct.Description;
            product.ShortDescription = wooCommerceProduct.ShortDescription;
            product.ImageUrl = wooCommerceProduct.Images.FirstOrDefault()?.Src;
            product.CategoryNames = string.Join(", ", wooCommerceProduct.Categories.Select(category => category.Name));
            product.RawJson = JsonSerializer.Serialize(wooCommerceProduct);
            product.Source = "woocommerce";
            product.SyncedAt = syncedAt;
            product.CreatedAt = product.CreatedAt == default ? syncedAt : product.CreatedAt;
            product.UpdatedAt = syncedAt;
        }

        await dbContext.SaveChangesAsync(cancellationToken);

        return wooCommerceProducts.Count;
    }

    private static decimal? ParsePrice(string? price)
    {
        return decimal.TryParse(price, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
            ? value
            : null;
    }
}
