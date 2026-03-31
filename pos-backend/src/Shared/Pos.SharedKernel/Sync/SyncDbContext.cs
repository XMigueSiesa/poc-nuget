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
                .HasConversion(
                    v => v.ToString(),
                    v => Ulid.Parse(v))
                .HasMaxLength(26);

            entity.Property(e => e.EntityType)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.EntityId)
                .IsRequired()
                .HasMaxLength(100);

            entity.Property(e => e.Payload)
                .IsRequired()
                .HasColumnType("text");

            entity.Property(e => e.CreatedAt)
                .IsRequired();

            entity.Property(e => e.SyncedAt);

            entity.HasIndex(e => e.SyncedAt)
                .HasFilter("\"SyncedAt\" IS NULL");
        });
    }
}
