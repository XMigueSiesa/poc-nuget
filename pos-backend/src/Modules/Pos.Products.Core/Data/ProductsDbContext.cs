using Microsoft.EntityFrameworkCore;
using Pos.Products.Core.Entities;

namespace Pos.Products.Core.Data;

public sealed class ProductsDbContext(DbContextOptions<ProductsDbContext> options) : DbContext(options)
{
    public DbSet<Product> Products => Set<Product>();
    public DbSet<Category> Categories => Set<Category>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("products");

        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(c => c.Name).HasMaxLength(200).IsRequired();
            e.Property(c => c.Description).HasMaxLength(500);
        });

        modelBuilder.Entity<Product>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(p => p.Name).HasMaxLength(300).IsRequired();
            e.Property(p => p.Description).HasMaxLength(1000);
            e.Property(p => p.CategoryId).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(p => p.Price).HasPrecision(18, 2);
            e.HasOne(p => p.Category).WithMany(c => c.Products).HasForeignKey(p => p.CategoryId);
            e.HasIndex(p => p.CategoryId);
            e.HasIndex(p => p.IsActive);
        });
    }
}
