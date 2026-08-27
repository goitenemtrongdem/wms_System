using AsrsWarehouse.Models;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Data;

public class WarehouseDbContext : DbContext
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options) { }

    public DbSet<WarehouseSlot> WarehouseSlots => Set<WarehouseSlot>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<MovementHistory> MovementHistories => Set<MovementHistory>();
    public DbSet<InfeedItem> InfeedItems => Set<InfeedItem>();
    public DbSet<InboundQueueItem> InboundQueueItems => Set<InboundQueueItem>();
    public DbSet<OutboundOrder> OutboundOrders => Set<OutboundOrder>();
    public DbSet<OutboundOrderLine> OutboundOrderLines => Set<OutboundOrderLine>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<WarehouseSlot>()
            .HasOne(x => x.StoredItem)
            .WithOne(x => x.CurrentSlot)
            .HasForeignKey<InfeedItem>(x => x.CurrentSlotId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<InfeedItem>()
            .HasIndex(x => x.CurrentSlotId)
            .IsUnique()
            .HasFilter("[CurrentSlotId] IS NOT NULL");

        modelBuilder.Entity<InfeedItem>()
            .Property(x => x.WeightKg)
            .HasPrecision(10, 2);

        modelBuilder.Entity<InboundQueueItem>()
            .HasIndex(x => new { x.Status, x.ScannedAt });

        modelBuilder.Entity<InboundQueueItem>()
            .HasOne(x => x.InfeedItem)
            .WithMany(x => x.InboundQueueItems)
            .HasForeignKey(x => x.InfeedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OutboundOrderLine>()
            .HasOne(x => x.InfeedItem)
            .WithMany(x => x.OutboundOrderLines)
            .HasForeignKey(x => x.InfeedItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OutboundOrderLine>()
            .HasOne(x => x.ResidualItem)
            .WithMany()
            .HasForeignKey(x => x.ResidualItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<OutboundOrderLine>()
            .HasIndex(x => new { x.Status, x.SlotId });
    }
}
