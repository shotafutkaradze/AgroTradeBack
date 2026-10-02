using AgroTrade.Domain.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Application.Abstractions;

public interface IAgroTradeDbContext
{
    DbSet<Product> Products { get; }
    DbSet<Brand> Brands { get; }
    DbSet<Category> Categories { get; }
    DbSet<Order> Orders { get; }
    DbSet<OrderItem> OrderItems { get; }
    DbSet<Payment> Payments { get; }
    DbSet<AppUser> AppUsers { get; }
    DbSet<ActivityLog> ActivityLogs { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
