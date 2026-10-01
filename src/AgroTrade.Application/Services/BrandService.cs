using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Brands;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Services;

public interface IBrandService
{
    Task<IReadOnlyList<BrandDto>> GetAsync(CancellationToken cancellationToken);
    Task<Brand> CreateAsync(BrandRequest request, CancellationToken cancellationToken);
    Task<Brand?> UpdateAsync(int id, BrandRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class BrandService(IAgroTradeDbContext dbContext) : IBrandService
{
    public async Task<IReadOnlyList<BrandDto>> GetAsync(CancellationToken cancellationToken)
    {
        var brands = await dbContext.Brands
            .AsNoTracking()
            .OrderBy(brand => brand.SortOrder)
            .ThenBy(brand => brand.Name)
            .ToListAsync(cancellationToken);

        var counts = await dbContext.Products
            .Where(product => product.BrandId != null)
            .GroupBy(product => product.BrandId!.Value)
            .Select(group => new { BrandId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.BrandId, item => item.Count, cancellationToken);

        return brands.Select(brand => new BrandDto(
            brand.Id,
            brand.Name,
            brand.Slug,
            brand.SortOrder,
            counts.GetValueOrDefault(brand.Id))).ToList();
    }

    public async Task<Brand> CreateAsync(BrandRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var now = DateTimeOffset.UtcNow;
        var brand = new Brand
        {
            Name = request.Name.Trim(),
            Slug = await CreateUniqueSlugAsync(request.Name, null, cancellationToken),
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Brands.Add(brand);
        await dbContext.SaveChangesAsync(cancellationToken);
        return brand;
    }

    public async Task<Brand?> UpdateAsync(int id, BrandRequest request, CancellationToken cancellationToken)
    {
        Validate(request);

        var brand = await dbContext.Brands.FirstOrDefaultAsync(brand => brand.Id == id, cancellationToken);
        if (brand is null)
        {
            return null;
        }

        brand.Name = request.Name.Trim();
        brand.Slug = await CreateUniqueSlugAsync(request.Name, id, cancellationToken);
        brand.SortOrder = request.SortOrder;
        brand.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return brand;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var brand = await dbContext.Brands.FirstOrDefaultAsync(brand => brand.Id == id, cancellationToken);
        if (brand is null)
        {
            return false;
        }

        if (await dbContext.Products.AnyAsync(product => product.BrandId == id, cancellationToken))
        {
            throw new InvalidOperationException("Brand has products.");
        }

        dbContext.Brands.Remove(brand);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static void Validate(BrandRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Brand name is required.");
        }
    }

    private async Task<string> CreateUniqueSlugAsync(string name, int? currentId, CancellationToken cancellationToken)
    {
        var baseSlug = ToSlug(name);
        var slug = baseSlug;
        var index = 2;

        while (await dbContext.Brands.AnyAsync(brand => brand.Slug == slug && brand.Id != currentId, cancellationToken))
        {
            slug = $"{baseSlug}-{index++}";
        }

        return slug;
    }

    private static string ToSlug(string value)
    {
        var chars = value
            .Trim()
            .ToLowerInvariant()
            .Select(character => char.IsLetterOrDigit(character) ? character : '-')
            .ToArray();

        var slug = string.Join('-', new string(chars).Split('-', StringSplitOptions.RemoveEmptyEntries));
        return string.IsNullOrWhiteSpace(slug) ? Guid.NewGuid().ToString("N")[..8] : slug;
    }
}
