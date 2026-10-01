using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Categories;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetTreeAsync(CancellationToken cancellationToken);
    Task<Category> CreateAsync(CategoryRequest request, CancellationToken cancellationToken);
    Task<Category?> UpdateAsync(int id, CategoryRequest request, CancellationToken cancellationToken);
    Task<bool> DeleteAsync(int id, CancellationToken cancellationToken);
}

public class CategoryService(IAgroTradeDbContext dbContext) : ICategoryService
{
    public async Task<IReadOnlyList<CategoryDto>> GetTreeAsync(CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories
            .AsNoTracking()
            .Include(category => category.Children)
            .OrderBy(category => category.SortOrder)
            .ThenBy(category => category.Name)
            .ToListAsync(cancellationToken);

        var productCounts = await dbContext.Products
            .Where(product => product.CategoryId != null)
            .GroupBy(product => product.CategoryId!.Value)
            .Select(group => new { CategoryId = group.Key, Count = group.Count() })
            .ToDictionaryAsync(item => item.CategoryId, item => item.Count, cancellationToken);

        return categories
            .Where(category => category.ParentId is null)
            .Select(category => ToDto(category, productCounts))
            .ToList();
    }

    public async Task<Category> CreateAsync(CategoryRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);
        await ValidateParentAsync(request.ParentId, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var category = new Category
        {
            Name = request.Name.Trim(),
            Slug = await CreateUniqueSlugAsync(request.Name, null, cancellationToken),
            ParentId = request.ParentId,
            SortOrder = request.SortOrder,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<Category?> UpdateAsync(int id, CategoryRequest request, CancellationToken cancellationToken)
    {
        ValidateRequest(request);

        var category = await dbContext.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);
        if (category is null)
        {
            return null;
        }

        if (request.ParentId == id)
        {
            throw new ArgumentException("Category cannot be its own parent.");
        }

        await ValidateParentAsync(request.ParentId, cancellationToken);

        category.Name = request.Name.Trim();
        category.Slug = await CreateUniqueSlugAsync(request.Name, id, cancellationToken);
        category.ParentId = request.ParentId;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return category;
    }

    public async Task<bool> DeleteAsync(int id, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .Include(category => category.Children)
            .FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return false;
        }

        var hasProducts = await dbContext.Products.AnyAsync(product => product.CategoryId == id, cancellationToken);
        if (category.Children.Count > 0 || hasProducts)
        {
            throw new InvalidOperationException("Category has child categories or products.");
        }

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }

    private async Task ValidateParentAsync(int? parentId, CancellationToken cancellationToken)
    {
        if (parentId is not null && !await dbContext.Categories.AnyAsync(category => category.Id == parentId, cancellationToken))
        {
            throw new ArgumentException("Parent category does not exist.");
        }
    }

    private static void ValidateRequest(CategoryRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            throw new ArgumentException("Category name is required.");
        }
    }

    private static CategoryDto ToDto(Category category, Dictionary<int, int> productCounts)
    {
        return new CategoryDto(
            category.Id,
            category.ParentId,
            category.Name,
            category.Slug,
            category.SortOrder,
            productCounts.GetValueOrDefault(category.Id),
            category.Children
                .OrderBy(child => child.SortOrder)
                .ThenBy(child => child.Name)
                .Select(child => ToDto(child, productCounts))
                .ToList());
    }

    private async Task<string> CreateUniqueSlugAsync(string name, int? currentId, CancellationToken cancellationToken)
    {
        var baseSlug = ToSlug(name);
        var slug = baseSlug;
        var index = 2;

        while (await dbContext.Categories.AnyAsync(category => category.Slug == slug && category.Id != currentId, cancellationToken))
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
