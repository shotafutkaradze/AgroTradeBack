using AgroTrade.Api.Data;
using AgroTrade.Api.Models;
using AgroTrade.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Text.Json;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(
    AgroTradeDbContext dbContext,
    IProductImportService productImportService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get(
        [FromQuery] string? search,
        [FromQuery] string? source,
        CancellationToken cancellationToken)
    {
        IQueryable<Product> query = dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var normalizedSearch = search.Trim().ToLower();
            query = query.Where(product =>
                product.Name.ToLower().Contains(normalizedSearch) ||
                (product.Sku != null && product.Sku.ToLower().Contains(normalizedSearch)) ||
                (product.CategoryNames != null && product.CategoryNames.ToLower().Contains(normalizedSearch)));
        }

        if (!string.IsNullOrWhiteSpace(source))
        {
            query = query.Where(product => product.Source == source.Trim().ToLower());
        }

        var products = await query
            .OrderByDescending(product => product.UpdatedAt)
            .ThenBy(product => product.Name)
            .ToListAsync(cancellationToken);

        return Ok(products.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .AsNoTracking()
            .Include(product => product.Category)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

        return product is null ? NotFound() : Ok(ToDto(product));
    }

    [HttpPost]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Product name is required." });
        }

        if (request.Price < 0 || request.RegularPrice < 0 || request.SalePrice < 0 || request.StockQuantity < 0)
        {
            return BadRequest(new { message = "Price and stock values cannot be negative." });
        }

        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Name = request.Name.Trim(),
            Slug = string.IsNullOrWhiteSpace(request.Slug) ? ToSlug(request.Name) : ToSlug(request.Slug),
            Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim(),
            Price = request.Price,
            RegularPrice = request.RegularPrice,
            SalePrice = request.SalePrice,
            StockStatus = string.IsNullOrWhiteSpace(request.StockStatus) ? "instock" : request.StockStatus.Trim().ToLower(),
            StockQuantity = request.StockQuantity,
            Description = request.Description,
            ShortDescription = request.ShortDescription,
            ImageUrl = request.ImageUrl,
            CategoryId = request.CategoryId,
            CategoryNames = request.CategoryNames,
            SpecificationsJson = request.SpecificationsJson,
            RawJson = "{}",
            Source = "local",
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);

        await dbContext.Entry(product).Reference(item => item.Category).LoadAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = product.Id }, ToDto(product));
    }

    [HttpPost("form")]
    [Authorize(Roles = "admin,manager")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> CreateFromForm([FromForm] CreateProductFormRequest request, CancellationToken cancellationToken)
    {
        var imageUrl = await BuildProductImageUrlAsync(request, null, cancellationToken);

        var createRequest = new CreateProductRequest(
            request.Name,
            request.Slug,
            request.Sku,
            ParseDecimal(request.Price),
            ParseDecimal(request.RegularPrice),
            ParseDecimal(request.SalePrice),
            request.StockStatus,
            ParseInt(request.StockQuantity),
            request.Description,
            request.ShortDescription,
            imageUrl,
            request.CategoryNames,
            request.CategoryId,
            request.SpecificationsJson);

        return await Create(createRequest, cancellationToken);
    }

    [HttpPut("{id:int}/form")]
    [Authorize(Roles = "admin,manager")]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UpdateFromForm(int id, [FromForm] CreateProductFormRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Name))
        {
            return BadRequest(new { message = "Product name is required." });
        }

        var product = await dbContext.Products
            .Include(product => product.Category)
            .FirstOrDefaultAsync(product => product.Id == id, cancellationToken);

        if (product is null)
        {
            return NotFound();
        }

        product.Name = request.Name.Trim();
        product.Slug = string.IsNullOrWhiteSpace(request.Slug) ? ToSlug(request.Name) : ToSlug(request.Slug);
        product.Sku = string.IsNullOrWhiteSpace(request.Sku) ? null : request.Sku.Trim();
        product.Price = ParseDecimal(request.Price);
        product.RegularPrice = ParseDecimal(request.RegularPrice);
        product.SalePrice = ParseDecimal(request.SalePrice);
        product.StockStatus = string.IsNullOrWhiteSpace(request.StockStatus) ? "instock" : request.StockStatus.Trim().ToLower();
        product.StockQuantity = ParseInt(request.StockQuantity);
        product.Description = request.Description;
        product.ShortDescription = request.ShortDescription;
        product.ImageUrl = await BuildProductImageUrlAsync(request, product.ImageUrl, cancellationToken);
        product.CategoryId = request.CategoryId;
        product.CategoryNames = request.CategoryNames;
        product.SpecificationsJson = request.SpecificationsJson;
        product.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(product).Reference(item => item.Category).LoadAsync(cancellationToken);

        return Ok(ToDto(product));
    }

    [HttpPost("sync")]
    public async Task<IActionResult> Sync(CancellationToken cancellationToken)
    {
        var count = await productImportService.SyncProductsAsync(cancellationToken);

        return Ok(new
        {
            imported = count
        });
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
            product.WooCommerceId,
            product.Name,
            product.Slug,
            product.Permalink,
            product.Sku,
            product.Price,
            product.RegularPrice,
            product.SalePrice,
            product.StockStatus,
            product.StockQuantity,
            product.Description,
            product.ShortDescription,
            product.ImageUrl,
            product.CategoryId,
            product.CategoryNames,
            product.Category is null ? null : new ProductCategoryDto(product.Category.Id, product.Category.Name, product.Category.ParentId),
            product.SpecificationsJson,
            product.Source,
            product.SyncedAt,
            product.CreatedAt,
            product.UpdatedAt);
    }

    private static decimal? ParseDecimal(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalizedValue = value.Trim().Replace(',', '.');
        return decimal.TryParse(normalizedValue, NumberStyles.Number, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var result)
            ? result
            : null;
    }

    private async Task<string?> BuildProductImageUrlAsync(
        CreateProductFormRequest request,
        string? currentImageUrl,
        CancellationToken cancellationToken)
    {
        var hasExplicitImageList = request.ImageUrlsJson is not null;
        var imageUrls = ParseImageUrls(request.ImageUrlsJson);

        if (imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(request.ImageUrl))
        {
            imageUrls.Add(request.ImageUrl.Trim());
        }

        if (!hasExplicitImageList && imageUrls.Count == 0 && !string.IsNullOrWhiteSpace(currentImageUrl))
        {
            imageUrls.AddRange(ParseStoredImageUrls(currentImageUrl));
        }

        if (request.Image is not null)
        {
            imageUrls.Add(await SaveProductImageAsync(request.Image, cancellationToken));
        }

        foreach (var image in request.Images.Take(Math.Max(0, 3 - imageUrls.Count)))
        {
            imageUrls.Add(await SaveProductImageAsync(image, cancellationToken));
        }

        var normalizedUrls = imageUrls
            .Where(url => !string.IsNullOrWhiteSpace(url))
            .Select(url => url.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();

        return normalizedUrls.Count == 0 ? null : string.Join("|", normalizedUrls);
    }

    private static List<string> ParseImageUrls(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return [];
        }

        try
        {
            return JsonSerializer.Deserialize<List<string>>(value) ?? [];
        }
        catch
        {
            return ParseStoredImageUrls(value);
        }
    }

    private static List<string> ParseStoredImageUrls(string value)
    {
        return value
            .Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .ToList();
    }

    private async Task<string> SaveProductImageAsync(IFormFile image, CancellationToken cancellationToken)
    {
        var allowedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".jpg",
            ".jpeg",
            ".png",
            ".webp",
            ".gif"
        };
        var extension = Path.GetExtension(image.FileName);

        if (image.Length == 0 || !allowedExtensions.Contains(extension))
        {
            throw new InvalidOperationException("Unsupported image file.");
        }

        var uploadsDirectory = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "products");
        Directory.CreateDirectory(uploadsDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsDirectory, fileName);

        await using var stream = System.IO.File.Create(filePath);
        await image.CopyToAsync(stream, cancellationToken);

        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}/uploads/products/{fileName}";
    }
}

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
    string? CategoryNames,
    int? CategoryId,
    string? SpecificationsJson);

public record ProductCategoryDto(int Id, string Name, int? ParentId);

public record ProductDto(
    int Id,
    long? WooCommerceId,
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
    int? CategoryId,
    string? CategoryNames,
    ProductCategoryDto? Category,
    string? SpecificationsJson,
    string Source,
    DateTimeOffset? SyncedAt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public class CreateProductFormRequest
{
    public string Name { get; set; } = string.Empty;
    public string? Slug { get; set; }
    public string? Sku { get; set; }
    public string? Price { get; set; }
    public string? RegularPrice { get; set; }
    public string? SalePrice { get; set; }
    public string? StockStatus { get; set; }
    public string? StockQuantity { get; set; }
    public string? Description { get; set; }
    public string? ShortDescription { get; set; }
    public string? ImageUrl { get; set; }
    public string? ImageUrlsJson { get; set; }
    public string? CategoryNames { get; set; }
    public int? CategoryId { get; set; }
    public string? SpecificationsJson { get; set; }
    public IFormFile? Image { get; set; }
    public List<IFormFile> Images { get; set; } = [];
}
