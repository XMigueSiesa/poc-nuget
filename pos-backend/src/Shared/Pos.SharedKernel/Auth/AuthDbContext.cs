using Microsoft.EntityFrameworkCore;

namespace Pos.SharedKernel.Auth;

public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<StoreCredential> StoreCredentials => Set<StoreCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("auth");

        modelBuilder.Entity<StoreCredential>(entity =>
        {
            entity.ToTable("store_credentials");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasConversion(
                    v => v.ToString(),
                    v => Ulid.Parse(v))
                .HasMaxLength(26);

            entity.Property(e => e.StoreId)
                .HasColumnName("store_id")
                .IsRequired()
                .HasMaxLength(50);

            entity.Property(e => e.ClientId)
                .HasColumnName("client_id")
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.ClientSecretHash)
                .HasColumnName("client_secret_hash")
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.StoreName)
                .HasColumnName("store_name")
                .IsRequired()
                .HasMaxLength(200);

            entity.Property(e => e.IsActive)
                .HasColumnName("is_active")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(e => e.ClientId)
                .IsUnique();

            entity.HasIndex(e => e.StoreId)
                .IsUnique();
        });
    }
}
