using Microsoft.EntityFrameworkCore;

namespace Pos.SharedKernel.Sync;

public sealed class SyncDbContext(DbContextOptions<SyncDbContext> options) : DbContext(options)
{
    public DbSet<SyncOutboxEntry> OutboxEntries => Set<SyncOutboxEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("sync");

        modelBuilder.Entity<SyncOutboxEntry>(entity =>
        {
            entity.ToTable("outbox_entries");

            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                .HasColumnName("id")
                .HasConversion(
                    v => v.ToString(),
                    v => Ulid.Parse(v))
                .HasMaxLength(26);

            entity.Property(e => e.EntityType)
                .HasColumnName("entity_type")
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EntityId)
                .HasColumnName("entity_id")
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Payload)
                .HasColumnName("payload")
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.SyncedAt)
                .HasColumnName("synced_at");

            entity.HasIndex(e => e.SyncedAt)
                .HasFilter("\"synced_at\" IS NULL");
        });
    }
}
