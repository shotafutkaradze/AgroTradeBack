using System.Globalization;
using System.Text.Json;
using AgroTrade.Api;
using AgroTrade.Application.Abstractions;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Products;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProductsController(
    IProductService productService,
    IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> Get([FromQuery] string? search, CancellationToken cancellationToken)
    {
        return Ok(await productService.GetAsync(search, cancellationToken));
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var product = await productService.GetByIdAsync(id, cancellationToken);
        return product is null ? NotFound() : Ok(product);
    }

    [HttpPost]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
    public async Task<IActionResult> Create(CreateProductRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var product = await productService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(GetById), new { id = product.Id }, product);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("form")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> CreateFromForm([FromForm] CreateProductFormRequest request, CancellationToken cancellationToken)
    {
        var imageUrl = await BuildProductImageUrlAsync(request, null, cancellationToken);
        return await Create(ToCreateProductRequest(request, imageUrl), cancellationToken);
    }

    [HttpPut("{id:int}/form")]
    [Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
    [RequestSizeLimit(10_000_000)]
    public async Task<IActionResult> UpdateFromForm(int id, [FromForm] CreateProductFormRequest request, CancellationToken cancellationToken)
    {
        var currentProduct = await productService.GetByIdAsync(id, cancellationToken);
        if (currentProduct is null)
        {
            return NotFound();
        }

        try
        {
            var imageUrl = await BuildProductImageUrlAsync(request, currentProduct.ImageUrl, cancellationToken);
            var product = await productService.UpdateAsync(id, ToCreateProductRequest(request, imageUrl), cancellationToken);
            return product is null ? NotFound() : Ok(product);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private static CreateProductRequest ToCreateProductRequest(CreateProductFormRequest request, string? imageUrl)
    {
        return new CreateProductRequest(
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
            ParseInt(request.BrandId),
            request.CategoryNames,
            request.CategoryId,
            request.SpecificationsJson);
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

    private async Task<string?> BuildProductImageUrlAsync(CreateProductFormRequest request, string? currentImageUrl, CancellationToken cancellationToken)
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
    public string? BrandId { get; set; }
    public string? CategoryNames { get; set; }
    public int? CategoryId { get; set; }
    public string? SpecificationsJson { get; set; }
    public IFormFile? Image { get; set; }
    public List<IFormFile> Images { get; set; } = [];
}
