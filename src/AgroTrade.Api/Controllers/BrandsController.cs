using AgroTrade.Api;
using AgroTrade.Application.Services;
using AgroTrade.Contracts.Brands;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Authorize(Policy = AuthorizationPolicies.AdminOrManager)]
[Route("api/[controller]")]
public class BrandsController(IBrandService brandService) : ControllerBase
{
    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        return Ok(await brandService.GetAsync(cancellationToken));
    }

    [HttpPost]
    public async Task<IActionResult> Create(BrandRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var brand = await brandService.CreateAsync(request, cancellationToken);
            return CreatedAtAction(nameof(Get), new { id = brand.Id }, brand);
        }
        catch (ArgumentException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update(int id, BrandRequest request, CancellationToken cancellationToken)
    {
        try
        {
            var brand = await brandService.UpdateAsync(id, request, cancellationToken);
            return brand is null ? NotFound() : Ok(brand);
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
            return await brandService.DeleteAsync(id, cancellationToken) ? NoContent() : NotFound();
        }
        catch (InvalidOperationException exception)
        {
            return BadRequest(new { message = exception.Message });
        }
    }
}
