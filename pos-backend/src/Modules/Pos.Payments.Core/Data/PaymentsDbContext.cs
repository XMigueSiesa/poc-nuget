using Microsoft.EntityFrameworkCore;
using Pos.Payments.Core.Entities;

namespace Pos.Payments.Core.Data;

public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("payments");

        modelBuilder.Entity<Payment>(e =>
        {
            e.HasKey(p => p.Id);
            e.Property(p => p.Id).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(p => p.OrderId).HasConversion(v => v.ToString(), v => Ulid.Parse(v));
            e.Property(p => p.Method).HasMaxLength(30).IsRequired();
            e.Property(p => p.Amount).HasPrecision(18, 2);
            e.Property(p => p.Status).HasMaxLength(20).IsRequired();
            e.Property(p => p.TransactionId).HasMaxLength(100);
            e.HasIndex(p => p.OrderId);
            e.HasIndex(p => p.TransactionId).IsUnique().HasFilter("\"TransactionId\" IS NOT NULL");
        });
    }
}
