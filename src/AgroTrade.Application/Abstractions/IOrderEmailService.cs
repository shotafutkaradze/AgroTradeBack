using AgroTrade.Contracts.Orders;

namespace AgroTrade.Application.Abstractions;

public interface IOrderEmailService
{
    Task SendOrderCreatedAsync(OrderDto order, CancellationToken cancellationToken);
}
