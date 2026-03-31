using Microsoft.EntityFrameworkCore;
using Pos.Orders.Core.Entities;

namespace Pos.Orders.Core.Data;

public sealed class OrdersDbContext(DbContextOptions<OrdersDbContext> options) : DbContext(options)
{
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderLine> OrderLines => Set<OrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("orders");

        modelBuilder.Entity<Order>(e =>
        {
            e.HasKey(o => o.Id);
            e.Property(o => o.Id).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(o => o.Status).HasMaxLength(20).IsRequired();
            e.Property(o => o.TableNumber).HasMaxLength(20);
            e.Property(o => o.Total).HasPrecision(18, 2);
            e.HasMany(o => o.Lines).WithOne().HasForeignKey(l => l.OrderId);
        });

        modelBuilder.Entity<OrderLine>(e =>
        {
            e.HasKey(l => l.Id);
            e.Property(l => l.Id).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(l => l.OrderId).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(l => l.ProductId).HasMaxLength(50).IsRequired();
            e.Property(l => l.ProductName).HasMaxLength(300).IsRequired();
            e.Property(l => l.UnitPrice).HasPrecision(18, 2);
            e.Ignore(l => l.LineTotal);
        });
    }
}
