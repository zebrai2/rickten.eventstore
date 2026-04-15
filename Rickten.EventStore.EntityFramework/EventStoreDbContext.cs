using Microsoft.EntityFrameworkCore;
using Rickten.EventStore.EntityFramework.Entities;

namespace Rickten.EventStore.EntityFramework;

/// <summary>
/// Database context for the event store.
/// </summary>
public sealed class EventStoreDbContext : DbContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventStoreDbContext"/> class.
    /// </summary>
    /// <param name="options">The options for this context.</param>
    public EventStoreDbContext(DbContextOptions<EventStoreDbContext> options)
        : base(options)
    {
    }

    /// <summary>
    /// Gets or sets the events table.
    /// </summary>
    public DbSet<EventEntity> Events { get; set; } = null!;

    /// <summary>
    /// Gets or sets the snapshots table.
    /// </summary>
    public DbSet<SnapshotEntity> Snapshots { get; set; } = null!;

    /// <summary>
    /// Gets or sets the projections table.
    /// </summary>
    public DbSet<ProjectionEntity> Projections { get; set; } = null!;

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure EventEntity
        modelBuilder.Entity<EventEntity>(entity =>
        {
            entity.ToTable("Events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
                .ValueGeneratedOnAdd();

            entity.Property(e => e.StreamType)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.StreamIdentifier)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.EventType)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.EventData)
                .IsRequired();

            entity.Property(e => e.Metadata)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()"); // SQL Server default, override for other providers

            // Unique constraint for optimistic concurrency
            entity.HasIndex(e => new { e.StreamType, e.StreamIdentifier, e.Version })
                .IsUnique()
                .HasDatabaseName("IX_Events_Stream_Version");

            // Index for global position queries (using Id)
            entity.HasIndex(e => e.Id)
                .HasDatabaseName("IX_Events_GlobalPosition");

            // Index for stream queries
            entity.HasIndex(e => new { e.StreamType, e.StreamIdentifier })
                .HasDatabaseName("IX_Events_Stream");
        });

        // Configure SnapshotEntity
        modelBuilder.Entity<SnapshotEntity>(entity =>
        {
            entity.ToTable("Snapshots");
            entity.HasKey(e => new { e.StreamType, e.StreamIdentifier });

            entity.Property(e => e.StreamType)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.StreamIdentifier)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.Version)
                .IsRequired();

            entity.Property(e => e.State)
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");
        });

        // Configure ProjectionEntity
        modelBuilder.Entity<ProjectionEntity>(entity =>
        {
            entity.ToTable("Projections");
            entity.HasKey(e => e.ProjectionKey);

            entity.Property(e => e.ProjectionKey)
                .IsRequired()
                .HasMaxLength(255);

            entity.Property(e => e.GlobalPosition)
                .IsRequired();

            entity.Property(e => e.State)
                .IsRequired();

            entity.Property(e => e.UpdatedAt)
                .IsRequired()
                .HasDefaultValueSql("GETUTCDATE()");
        });
    }
}
