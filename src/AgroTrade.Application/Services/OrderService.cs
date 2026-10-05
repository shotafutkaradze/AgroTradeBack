using AgroTrade.Application.Abstractions;
using AgroTrade.Contracts.Orders;
using AgroTrade.Domain.Enums;
using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AgroTrade.Application.Services;

public interface IOrderService
{
    Task<IReadOnlyList<OrderDto>> GetAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken);
    Task<OrderDto?> GetByIdAsync(int id, CancellationToken cancellationToken);
    Task<IReadOnlyList<OrderDto>> GetByUserAsync(int userId, CancellationToken cancellationToken);
    Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken);
    Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken);
}

public class OrderService(
    IAgroTradeDbContext dbContext,
    IActivityLogService activityLogService,
    IOrderEmailService orderEmailService,
    ILogger<OrderService> logger) : IOrderService
{
    public async Task<IReadOnlyList<OrderDto>> GetAsync(DateTimeOffset? from, DateTimeOffset? to, CancellationToken cancellationToken)
    {
        var periodTo = (to ?? DateTimeOffset.UtcNow).ToUniversalTime();
        var periodFrom = (from ?? periodTo.AddMonths(-1)).ToUniversalTime();

        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.CreatedAt >= periodFrom && order.CreatedAt <= periodTo)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderDto?> GetByIdAsync(int id, CancellationToken cancellationToken)
    {
        var order = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

        return order is null ? null : ToDto(order);
    }

    public async Task<IReadOnlyList<OrderDto>> GetByUserAsync(int userId, CancellationToken cancellationToken)
    {
        var orders = await dbContext.Orders
            .AsNoTracking()
            .Include(order => order.Items)
            .Where(order => order.AppUserId == userId)
            .OrderByDescending(order => order.CreatedAt)
            .ToListAsync(cancellationToken);

        return orders.Select(ToDto).ToList();
    }

    public async Task<OrderDto> CreateAsync(CreateOrderRequest request, CancellationToken cancellationToken)
    {
        ValidateCreateRequest(request);

        var productIds = request.Items
            .Where(item => item.ProductId.HasValue)
            .Select(item => item.ProductId!.Value)
            .Distinct()
            .ToArray();

        var products = await dbContext.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var paymentMethod = request.PaymentMethod.ToPaymentMethod();
        var deliveryMethod = request.Delivery.Method.ToDeliveryMethod();
        var order = new Order
        {
            OrderNumber = await BuildOrderNumberAsync(cancellationToken),
            AppUserId = request.UserId,
            Status = OrderStatus.New,
            PaymentMethod = paymentMethod,
            PaymentStatus = paymentMethod is PaymentMethod.Liberty or PaymentMethod.BogInstallmentPlan
                ? PaymentStatus.Pending
                : PaymentStatus.Unpaid,
            DeliveryMethod = deliveryMethod,
            PickupBranch = request.Delivery.PickupBranch,
            CustomerFirstName = request.Customer.FirstName.Trim(),
            CustomerLastName = request.Customer.LastName.Trim(),
            CustomerPhone = request.Customer.Phone.Trim(),
            CustomerEmail = string.IsNullOrWhiteSpace(request.Customer.Email) ? null : request.Customer.Email.Trim(),
            City = request.Delivery.City?.Trim() ?? string.Empty,
            Address = request.Delivery.Address?.Trim() ?? string.Empty,
            Note = string.IsNullOrWhiteSpace(request.Delivery.Note) ? null : request.Delivery.Note.Trim(),
            DeliveryPrice = 0,
            CreatedAt = now,
            UpdatedAt = now
        };

        foreach (var item in request.Items)
        {
            if (item.Quantity <= 0 || item.UnitPrice < 0)
            {
                throw new ArgumentException("Order item quantity and price must be valid.");
            }

            products.TryGetValue(item.ProductId ?? 0, out var product);
            if (product?.StockQuantity is not null)
            {
                if (product.StockQuantity < item.Quantity)
                {
                    throw new ArgumentException($"Not enough stock for {product.Name}.");
                }

                product.StockQuantity -= item.Quantity;
                product.StockStatus = product.StockQuantity > 0
                    ? StockStatus.InStock
                    : StockStatus.OutOfStock;
                product.UpdatedAt = now;
            }

            order.Items.Add(new OrderItem
            {
                ProductId = product?.Id ?? item.ProductId,
                ProductName = string.IsNullOrWhiteSpace(item.ProductName) ? product?.Name ?? "Product" : item.ProductName.Trim(),
                ProductSku = string.IsNullOrWhiteSpace(item.ProductSku) ? product?.Sku : item.ProductSku.Trim(),
                ProductImageUrl = string.IsNullOrWhiteSpace(item.ProductImageUrl) ? product?.ImageUrl : item.ProductImageUrl.Trim(),
                ProductCategory = string.IsNullOrWhiteSpace(item.ProductCategory) ? product?.CategoryNames : item.ProductCategory.Trim(),
                UnitPrice = item.UnitPrice,
                Quantity = item.Quantity,
                LineTotal = item.UnitPrice * item.Quantity
            });
        }

        order.Subtotal = order.Items.Sum(item => item.LineTotal);
        order.DeliveryPrice = CalculateDeliveryPrice(order.Subtotal, order.DeliveryMethod, order.City, order.Address);
        order.Total = order.Subtotal + order.DeliveryPrice;

        if (paymentMethod is PaymentMethod.Liberty or PaymentMethod.BogInstallmentPlan)
        {
            order.Payments.Add(new Payment
            {
                Provider = paymentMethod == PaymentMethod.Liberty
                    ? PaymentProvider.LibertyPay
                    : PaymentProvider.BogInstallment,
                Method = paymentMethod,
                Status = PaymentStatus.Pending,
                Amount = order.Total,
                Currency = "GEL",
                CreatedAt = now,
                UpdatedAt = now
            });
        }

        dbContext.Orders.Add(order);
        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "info",
            "order",
            "order_created",
            $"შეიქმნა შეკვეთა {order.OrderNumber}",
            order.Id,
            order.OrderNumber,
            order.PaymentMethod.ToValue(),
            order.PaymentStatus.ToValue(),
            Details: $"თანხა: {order.Total:0.##} ₾; მომხმარებელი: {order.CustomerFirstName} {order.CustomerLastName}"),
            cancellationToken);

        var orderDto = ToDto(order);
        if (order.PaymentStatus != PaymentStatus.Pending)
        {
            try
            {
                await orderEmailService.SendOrderCreatedAsync(orderDto, cancellationToken);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to send order email for {OrderNumber}", order.OrderNumber);
                await activityLogService.AddAsync(new ActivityLogEntry(
                    "warning",
                    "order",
                    "order_email_failed",
                    $"შეკვეთის email ვერ გაიგზავნა: {order.OrderNumber}",
                    order.Id,
                    order.OrderNumber,
                    order.PaymentMethod.ToValue(),
                    order.PaymentStatus.ToValue(),
                    Details: ex.Message),
                    cancellationToken);
            }
        }

        return orderDto;
    }

    public async Task<OrderDto?> UpdateStatusAsync(int id, UpdateOrderStatusRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.Status))
        {
            throw new ArgumentException("Order status is required.");
        }

        if (!request.Status.TryToOrderStatus(out var status))
        {
            throw new ArgumentException("Order status is not supported.");
        }

        var order = await dbContext.Orders
            .Include(order => order.Items)
            .FirstOrDefaultAsync(order => order.Id == id, cancellationToken);

        if (order is null)
        {
            return null;
        }

        var oldStatus = order.Status;
        order.Status = status;
        order.UpdatedAt = DateTimeOffset.UtcNow;

        await dbContext.SaveChangesAsync(cancellationToken);
        await activityLogService.AddAsync(new ActivityLogEntry(
            "info",
            "order",
            "order_status_changed",
            $"შეკვეთის სტატუსი შეიცვალა: {oldStatus.ToValue()} -> {status.ToValue()}",
            order.Id,
            order.OrderNumber,
            order.PaymentMethod.ToValue(),
            order.PaymentStatus.ToValue()),
            cancellationToken);

        return ToDto(order);
    }

    private static void ValidateCreateRequest(CreateOrderRequest request)
    {
        if (request.Items.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one product.");
        }

        if (string.IsNullOrWhiteSpace(request.Customer.FirstName) ||
            string.IsNullOrWhiteSpace(request.Customer.LastName) ||
            string.IsNullOrWhiteSpace(request.Customer.Phone))
        {
            throw new ArgumentException("Customer first name, last name and phone are required.");
        }

        if (request.Delivery.Method.ToDeliveryMethod() == DeliveryMethod.Courier &&
            (string.IsNullOrWhiteSpace(request.Delivery.City) || string.IsNullOrWhiteSpace(request.Delivery.Address)))
        {
            throw new ArgumentException("City and address are required for courier delivery.");
        }
    }

    private async Task<string> BuildOrderNumberAsync(CancellationToken cancellationToken)
    {
        var nextId = await dbContext.Orders.CountAsync(cancellationToken) + 1;
        return $"AT-{DateTimeOffset.UtcNow:yyyyMMdd}-{nextId:0000}";
    }

    private static decimal CalculateDeliveryPrice(decimal subtotal, DeliveryMethod deliveryMethod, string? city, string? address)
    {
        if (deliveryMethod == DeliveryMethod.Pickup || subtotal > 300)
        {
            return 0;
        }

        var destination = $"{city} {address}".Trim();
        if (destination.Contains("თბილის", StringComparison.OrdinalIgnoreCase))
        {
            return 6;
        }

        if (destination.Contains("სოფ", StringComparison.OrdinalIgnoreCase))
        {
            return 15;
        }

        return 10;
    }

    public static OrderDto ToDto(Order order)
    {
        return new OrderDto(
            order.Id,
            order.OrderNumber,
            order.Status.ToValue(),
            order.PaymentMethod.ToValue(),
            order.PaymentStatus.ToValue(),
            order.PaymentTransactionCode,
            order.LibertyPaymentOrderCode,
            order.DeliveryMethod.ToValue(),
            order.PickupBranch,
            new OrderCustomerDto(order.CustomerFirstName, order.CustomerLastName, order.CustomerPhone, order.CustomerEmail),
            new OrderDeliveryDto(order.DeliveryMethod.ToValue(), order.City, order.Address, order.PickupBranch, order.Note),
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

