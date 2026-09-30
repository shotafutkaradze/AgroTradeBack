using AgroTrade.Api.Data;
using AgroTrade.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Authorize(Roles = "admin,manager")]
[Route("api/[controller]")]
public class CategoriesController(AgroTradeDbContext dbContext) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
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

        var tree = categories
            .Where(category => category.ParentId is null)
            .Select(category => ToDto(category, productCounts))
            .ToList();

        return Ok(tree);
    }

    [HttpPost]
    public async Task<IActionResult> Create(CategoryRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Category name is required." });
        }

        if (request.ParentId is not null && !await dbContext.Categories.AnyAsync(category => category.Id == request.ParentId, cancellationToken))
        {
            return BadRequest(new { message = "Parent category does not exist." });
        }

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

        return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CategoryRequest request, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories.FirstOrDefaultAsync(category => category.Id == id, cancellationToken);
        if (category is null)
        {
            return NotFound();
        }

        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Category name is required." });
        }

        if (request.ParentId == id)
        {
            return BadRequest(new { message = "Category cannot be its own parent." });
        }

        if (request.ParentId is not null && !await dbContext.Categories.AnyAsync(item => item.Id == request.ParentId, cancellationToken))
        {
            return BadRequest(new { message = "Parent category does not exist." });
        }

        category.Name = request.Name.Trim();
        category.Slug = await CreateUniqueSlugAsync(request.Name, id, cancellationToken);
        category.ParentId = request.ParentId;
        category.SortOrder = request.SortOrder;
        category.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        return Ok(category);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        var category = await dbContext.Categories
            .Include(category => category.Children)
            .FirstOrDefaultAsync(category => category.Id == id, cancellationToken);

        if (category is null)
        {
            return NotFound();
        }

        var hasProducts = await dbContext.Products.AnyAsync(product => product.CategoryId == id, cancellationToken);
        if (category.Children.Count > 0 || hasProducts)
        {
            return BadRequest(new { message = "Category has child categories or products." });
        }

        dbContext.Categories.Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        return NoContent();
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

public record CategoryRequest(string Name, int? ParentId, int SortOrder);
public record CategoryDto(int Id, int? ParentId, string Name, string Slug, int SortOrder, int ProductCount, List<CategoryDto> Children);
