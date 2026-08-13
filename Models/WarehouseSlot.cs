using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("WAREHOUSE_SLOTS")]
public class WarehouseSlot
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(20)]
    public string Name { get; set; } = string.Empty;

    public int RowNo { get; set; }
    public int ColNo { get; set; }

    [Required, MaxLength(20)]
    public string Status { get; set; } = "EMPTY"; // EMPTY | OCCUPIED | REQUEST

    public bool SensorOccupied { get; set; }
    public DateTime? LastSensorUpdate { get; set; }

    [MaxLength(20)]
    public string? RequestType { get; set; } // INBOUND | OUTBOUND

    public DateTime? RequestedAt { get; set; }
}
