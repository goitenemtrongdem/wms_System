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

    public int? InfeedItemId { get; set; }

    [ForeignKey(nameof(SlotId))]
    public WarehouseSlot? Slot { get; set; }

    [ForeignKey(nameof(InfeedItemId))]
    public InfeedItem? InfeedItem { get; set; }
}
