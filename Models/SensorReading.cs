using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("sensor_readings")]
public class SensorReading
{
    [Key]
    public long Id { get; set; }

    public int SlotId { get; set; }
    public bool Occupied { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(SlotId))]
    public WarehouseSlot? Slot { get; set; }
}
