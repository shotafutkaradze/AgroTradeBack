using AgroTrade.Domain.Models;
using AgroTrade.Application.Abstractions;
using AgroTrade.Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Infrastructure.Data;

public class AgroTradeDbContext(DbContextOptions<AgroTradeDbContext> options) : DbContext(options), IAgroTradeDbContext
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.Property(product => product.Name).HasMaxLength(500);
            entity.Property(product => product.Slug).HasMaxLength(500);
            entity.Property(product => product.Sku).HasMaxLength(200);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.Property(product => product.RegularPrice).HasPrecision(18, 2);
            entity.Property(product => product.SalePrice).HasPrecision(18, 2);
            entity.Property(product => product.StockStatus)
                .HasConversion(
                    value => value == null ? null : value.Value.ToValue(),
                    value => value.ToStockStatusOrNull())
                .HasMaxLength(100);
            entity.Property(product => product.ImageUrl).HasMaxLength(1000);
            entity.Property(product => product.IsHidden).HasDefaultValue(false);

            entity.HasOne(product => product.Brand)
                .WithMany(brand => brand.Products)
                .HasForeignKey(product => product.BrandId)
                .OnDelete(DeleteBehavior.SetNull);

            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.HasIndex(brand => brand.Slug).IsUnique();
            entity.Property(brand => brand.Name).HasMaxLength(300);
            entity.Property(brand => brand.Slug).HasMaxLength(320);
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasIndex(category => category.Slug).IsUnique();
            entity.Property(category => category.Name).HasMaxLength(300);
            entity.Property(category => category.Slug).HasMaxLength(320);

            entity.HasOne(category => category.Parent)
                .WithMany(category => category.Children)
                .HasForeignKey(category => category.ParentId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Order>(entity =>
        {
            entity.HasIndex(order => order.OrderNumber).IsUnique();
            entity.Property(order => order.OrderNumber).HasMaxLength(40);
            entity.Property(order => order.Status)
                .HasConversion(value => value.ToValue(), value => value.ToOrderStatus())
                .HasMaxLength(50);
            entity.Property(order => order.PaymentMethod)
                .HasConversion(value => value.ToValue(), value => value.ToPaymentMethod())
                .HasMaxLength(100);
            entity.Property(order => order.PaymentStatus)
                .HasConversion(value => value.ToValue(), value => value.ToPaymentStatus())
                .HasMaxLength(50);
            entity.Property(order => order.PaymentTransactionCode).HasMaxLength(100);
            entity.Property(order => order.LibertyPaymentOrderCode).HasMaxLength(100);
            entity.Property(order => order.LibertyPayOrderPassword).HasMaxLength(200);
            entity.Property(order => order.DeliveryMethod)
                .HasConversion(value => value.ToValue(), value => value.ToDeliveryMethod())
                .HasMaxLength(50);
            entity.Property(order => order.PickupBranch).HasMaxLength(100);
            entity.Property(order => order.CustomerFirstName).HasMaxLength(200);
            entity.Property(order => order.CustomerLastName).HasMaxLength(200);
            entity.Property(order => order.CustomerPhone).HasMaxLength(100);
            entity.Property(order => order.CustomerEmail).HasMaxLength(300);
            entity.Property(order => order.City).HasMaxLength(200);
            entity.Property(order => order.Address).HasMaxLength(500);
            entity.Property(order => order.Subtotal).HasPrecision(18, 2);
            entity.Property(order => order.DeliveryPrice).HasPrecision(18, 2);
            entity.Property(order => order.Total).HasPrecision(18, 2);

            entity.HasOne(order => order.AppUser)
                .WithMany()
                .HasForeignKey(order => order.AppUserId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Payment>(entity =>
        {
            entity.Property(payment => payment.Provider)
                .HasConversion(value => value.ToValue(), value => value.ToPaymentProvider())
                .HasMaxLength(50);
            entity.Property(payment => payment.Method)
                .HasConversion(value => value.ToValue(), value => value.ToPaymentMethod())
                .HasMaxLength(100);
            entity.Property(payment => payment.Status)
                .HasConversion(value => value.ToValue(), value => value.ToPaymentStatus())
                .HasMaxLength(50);
            entity.Property(payment => payment.Amount).HasPrecision(18, 2);
            entity.Property(payment => payment.Currency).HasMaxLength(3);
            entity.Property(payment => payment.ProviderOrderId).HasMaxLength(100);
            entity.Property(payment => payment.ProviderOrderPassword).HasMaxLength(200);
            entity.Property(payment => payment.ProviderStatus).HasMaxLength(100);
            entity.Property(payment => payment.ProviderTransactionCode).HasMaxLength(100);
            entity.Property(payment => payment.FailureReason).HasMaxLength(500);
            entity.Property(payment => payment.RefundedAmount).HasPrecision(18, 2);
            entity.Property(payment => payment.RefundReason).HasMaxLength(500);
            entity.Property(payment => payment.ProviderRefundStatus).HasMaxLength(100);

            entity.HasIndex(payment => payment.ProviderOrderId);

            entity.HasOne(payment => payment.Order)
                .WithMany(order => order.Payments)
                .HasForeignKey(payment => payment.OrderId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<OrderItem>(entity =>
        {
            entity.Property(item => item.ProductName).HasMaxLength(500);
            entity.Property(item => item.ProductSku).HasMaxLength(200);
            entity.Property(item => item.ProductImageUrl).HasMaxLength(1000);
            entity.Property(item => item.ProductCategory).HasMaxLength(300);
            entity.Property(item => item.UnitPrice).HasPrecision(18, 2);
            entity.Property(item => item.LineTotal).HasPrecision(18, 2);

            entity.HasOne(item => item.Order)
                .WithMany(order => order.Items)
                .HasForeignKey(item => item.OrderId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(item => item.Product)
                .WithMany()
                .HasForeignKey(item => item.ProductId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.HasIndex(user => user.Email).IsUnique();
            entity.Property(user => user.FirstName).HasMaxLength(120);
            entity.Property(user => user.LastName).HasMaxLength(120);
            entity.Property(user => user.Email).HasMaxLength(240);
            entity.Property(user => user.Phone).HasMaxLength(100);
            entity.Property(user => user.City).HasMaxLength(120);
            entity.Property(user => user.Region).HasMaxLength(160);
            entity.Property(user => user.Street).HasMaxLength(180);
            entity.Property(user => user.Building).HasMaxLength(120);
            entity.Property(user => user.AddressNote).HasMaxLength(300);
            entity.Property(user => user.PasswordHash).HasMaxLength(500);
            entity.HasIndex(user => user.EmailVerificationToken);
            entity.Property(user => user.EmailVerificationToken).HasMaxLength(120);
            entity.Property(user => user.Role)
                .HasConversion(value => value.ToValue(), value => value.ToUserRole())
                .HasMaxLength(40);
        });

        modelBuilder.Entity<ActivityLog>(entity =>
        {
            entity.HasIndex(log => log.CreatedAt);
            entity.HasIndex(log => log.OrderNumber);
            entity.Property(log => log.Severity).HasMaxLength(40);
            entity.Property(log => log.Category).HasMaxLength(80);
            entity.Property(log => log.Event).HasMaxLength(120);
            entity.Property(log => log.Message).HasMaxLength(500);
            entity.Property(log => log.OrderNumber).HasMaxLength(80);
            entity.Property(log => log.Provider).HasMaxLength(80);
            entity.Property(log => log.Status).HasMaxLength(80);
            entity.Property(log => log.Actor).HasMaxLength(160);
            entity.Property(log => log.Details).HasMaxLength(4000);
        });
    }
}

