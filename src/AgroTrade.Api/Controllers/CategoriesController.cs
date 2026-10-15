using AgroTrade.Api;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Categories;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
[Route("api/[controller]")]
public class CategoriesController(ICategoryService categoryService, IWebHostEnvironment environment) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await categoryService.GetTreeAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CategoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var category = await categoryService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = category.Id }, category);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, CategoryRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var category = await categoryService.UpdateAsync(id, request, cancellationToken);
            return category is null ? NotFound() : Ok(category);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken)
    {
        try
        {
            return await categoryService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPost("upload-image")]
    public async Task<IActionResult> UploadImage(IFormFile image, CancellationToken cancellationToken)
    {
        try
        {
            return Ok(new { imageUrl = await SaveCategoryImageAsync(image, cancellationToken) });
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    private async Task<string> SaveCategoryImageAsync(IFormFile image, CancellationToken cancellationToken)
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

        var uploadsDirectory = Path.Combine(environment.WebRootPath ?? Path.Combine(environment.ContentRootPath, "wwwroot"), "uploads", "categories");
        Directory.CreateDirectory(uploadsDirectory);

        var fileName = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var filePath = Path.Combine(uploadsDirectory, fileName);

        await using var stream = System.IO.File.Create(filePath);
        await image.CopyToAsync(stream, cancellationToken);

        var request = HttpContext.Request;
        return $"{request.Scheme}://{request.Host}/uploads/categories/{fileName}";
    }
}
