using AsrsWarehouse.Models;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Data;

public class WarehouseDbContext : DbContext
{
    public WarehouseDbContext(DbContextOptions<WarehouseDbContext> options) : base(options) { }

    public DbSet<WarehouseSlot> WarehouseSlots => Set<WarehouseSlot>();
    public DbSet<SensorReading> SensorReadings => Set<SensorReading>();
    public DbSet<MovementHistory> MovementHistories => Set<MovementHistory>();
}
