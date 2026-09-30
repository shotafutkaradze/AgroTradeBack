using AgroTrade.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace AgroTrade.Api.Data;

public class AgroTradeDbContext(DbContextOptions<AgroTradeDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();
    public DbSet<AppUser> AppUsers => Set<AppUser>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasIndex(product => product.WooCommerceId)
                .IsUnique()
                .HasFilter("\"WooCommerceId\" IS NOT NULL");

            entity.Property(product => product.Name).HasMaxLength(500);
            entity.Property(product => product.Slug).HasMaxLength(500);
            entity.Property(product => product.Sku).HasMaxLength(200);
            entity.Property(product => product.Price).HasPrecision(18, 2);
            entity.Property(product => product.RegularPrice).HasPrecision(18, 2);
            entity.Property(product => product.SalePrice).HasPrecision(18, 2);
            entity.Property(product => product.StockStatus).HasMaxLength(100);
            entity.Property(product => product.ImageUrl).HasMaxLength(1000);
            entity.Property(product => product.Source).HasMaxLength(50);

            entity.HasOne(product => product.Category)
                .WithMany(category => category.Products)
                .HasForeignKey(product => product.CategoryId)
                .OnDelete(DeleteBehavior.SetNull);
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
            entity.Property(order => order.Status).HasMaxLength(50);
            entity.Property(order => order.PaymentMethod).HasMaxLength(100);
            entity.Property(order => order.DeliveryMethod).HasMaxLength(50);
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
            entity.Property(user => user.PasswordHash).HasMaxLength(500);
            entity.Property(user => user.Role).HasMaxLength(40);
        });
    }
}
