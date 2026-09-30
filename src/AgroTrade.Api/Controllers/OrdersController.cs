using AgroTrade.Api.Data;
using AgroTrade.Api.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace AgroTrade.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController(AgroTradeDbContext dbContext) : ControllerBase
{
    private static readonly HashSet<string> AllowedStatuses = new(StringComparer.OrdinalIgnoreCase)
    {
        "new",
        "processing",
        "completed",
        "cancelled"
    };

    [HttpGet]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> Get(CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(ToDto));
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> GetById(int id, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(item => item.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

        return order is null ? NotFound() : Ok(ToDto(order));
    }

    [HttpGet("user/{userId:int}")]
    [Authorize]
    public async Task<IActionResult> GetByUser(int userId, CancellationToken cancellationToken)
    {
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        var isAdmin = User.IsInRole("admin") || User.IsInRole("manager");
        if (!isAdmin && currentUserId != userId.ToString())
        {
            return Forbid();
        }

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.AppUserId == userId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        return Ok(orders.Select(ToDto));
    }

    [HttpPost]
    public async Task<IActionResult> Create(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        if (request.Items.Count == 0)
        {
            return BadRequest(new { message = "Order must contain at least one product." });
        }

        if (string.IsNullOrWhiteSpace(request.Customer.FirstName) ||
            string.IsNullOrWhiteSpace(request.Customer.LastName) ||
            string.IsNullOrWhiteSpace(request.Customer.Phone))
        {
            return BadRequest(new { message = "Customer first name, last name and phone are required." });
        }

        if (request.Delivery.Method == "courier" &&
            (string.IsNullOrWhiteSpace(request.Delivery.City) || string.IsNullOrWhiteSpace(request.Delivery.Address)))
        {
            return BadRequest(new { message = "City and address are required for courier delivery." });
        }

        var productIds = request.Items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToArray();

        var products = await dbContext.Products
            .AsNoTracking()
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var order = new Order
        {
            OrderNumber = await BuildOrderNumberAsync(cancellationToken),
            AppUserId = request.UserId,
            Status = "new",
            PaymentMethod = request.PaymentMethod.Trim(),
            DeliveryMethod = string.IsNullOrWhiteSpace(request.Delivery.Method) ? "courier" : request.Delivery.Method.Trim(),
            PickupBranch = request.Delivery.PickupBranch,
            CustomerFirstName = request.Customer.FirstName.Trim(),
            CustomerLastName = request.Customer.LastName.Trim(),
            CustomerPhone = request.Customer.Phone.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(request.Customer.Email) ? null : request.Customer.Email.Trim(),
            City = request.Delivery.City?.Trim() ?? string.Empty,
            Address = request.Delivery.Address?.Trim() ?? string.Empty,
            Note = string.IsNullOrWhiteSpace(request.Delivery.Note) ? null : request.Delivery.Note.Trim(),
            DeliveryPrice = request.DeliveryPrice,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0 || item.UnitPrice < 0)
            {
                return BadRequest(new { message = "Order item quantity and price must be valid." });
            }

            products.TryGetValue(item.ProductId ?? 0, out var product);
            var unitPrice = item.UnitPrice;
            var quantity = item.Quantity;

            order.Items.Add(new OrderItem
            {
                ProductId = product?.Id ?? item.ProductId,
                ProductName = string.IsNullOrWhiteSpace(item.ProductName) ? product?.Name ?? "Product" : item.ProductName.Trim(),
                ProductSku = string.IsNullOrWhiteSpace(item.ProductSku) ? product?.Sku : item.ProductSku.Trim(),
                ProductImageUrl = string.IsNullOrWhiteSpace(item.ProductImageUrl) ? product?.ImageUrl : item.ProductImageUrl.Trim(),
                ProductCategory = string.IsNullOrWhiteSpace(item.ProductCategory) ? product?.CategoryNames : item.ProductCategory.Trim(),
                UnitPrice = unitPrice,
                Quantity = quantity,
                LineTotal = unitPrice * quantity
            });
        }

        order.Subtotal = order.Items.Sum(item => item.LineTotal);
        order.Total = order.Subtotal + order.DeliveryPrice;

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);

        return CreatedAtAction(nameof(GetById), new { id = order.Id }, ToDto(order));
    }

    [HttpPatch("{id:int}/status")]
    [Authorize(Roles = "admin,manager")]
    public async Task<IActionResult> UpdateStatus(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            return BadRequest(new { message = "Order status is required." });
        }

        var status = request.Status.Trim().ToLowerInvariant();
        if (!AllowedStatuses.Contains(status))
        {
            return BadRequest(new { message = "Order status is not supported." });
        }

        var order = await dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (order is null)
        {
            return NotFound();
        }

        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);

        return Ok(ToDto(order));
    }

    private async Task<string> BuildOrderNumberAsync(CancellationToken cancellationToken)
    {
        var nextId = await dbContext.Orders.CountAsync(cancellationToken) + 1;
        return $"AT-{DateTimeOffset.UtcNow:yyyyMMdd}-{nextId:0000}";
    }

    private static OrderDto ToDto(Order order)
    {
        return new OrderDto(
            order.Id,
            order.OrderNumber,
            order.Status,
            order.PaymentMethod,
            order.DeliveryMethod,
            order.PickupBranch,
            new OrderCustomerDto(order.CustomerFirstName, order.CustomerLastName, order.CustomerPhone, order.CustomerEmail),
            new OrderDeliveryDto(order.DeliveryMethod, order.City, order.Address, order.PickupBranch, order.Note),
            order.Subtotal,
            order.DeliveryPrice,
            order.Total,
            order.CreatedAt,
            order.UpdatedAt,
            order.Items.Select(item => new OrderItemDto(
                item.Id,
                item.ProductId,
                item.ProductName,
                item.ProductSku,
                item.ProductImageUrl,
                item.ProductCategory,
                item.UnitPrice,
                item.Quantity,
                item.LineTotal)).ToList());
    }
}

public record CreateOrderRequest(
    int? UserId,
    OrderCustomerRequest Customer,
    OrderDeliveryRequest Delivery,
    string PaymentMethod,
    decimal DeliveryPrice,
    List<CreateOrderItemRequest> Items);

public record UpdateOrderStatusRequest(string Status);

public record OrderCustomerRequest(
    string FirstName,
    string LastName,
    string Phone,
    string? Email);

public record OrderDeliveryRequest(
    string Method,
    string? City,
    string? Address,
    string? PickupBranch,
    string? Note);

public record CreateOrderItemRequest(
    int? ProductId,
    string ProductName,
    string? ProductSku,
    string? ProductImageUrl,
    string? ProductCategory,
    decimal UnitPrice,
    int Quantity);

public record OrderDto(
    int Id,
    string OrderNumber,
    string Status,
    string PaymentMethod,
    string DeliveryMethod,
    string? PickupBranch,
    OrderCustomerDto Customer,
    OrderDeliveryDto Delivery,
    decimal Subtotal,
    decimal DeliveryPrice,
    decimal Total,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    List<OrderItemDto> Items);

public record OrderCustomerDto(string FirstName, string LastName, string Phone, string? Email);

public record OrderDeliveryDto(string Method, string City, string Address, string? PickupBranch, string? Note);

public record OrderItemDto(
    int Id,
    int? ProductId,
    string ProductName,
    string? ProductSku,
    string? ProductImageUrl,
    string? ProductCategory,
    decimal UnitPrice,
    int Quantity,
    decimal LineTotal);
