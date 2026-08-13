using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("movement_history")]
public class MovementHistory
{
    [Key]
    public long Id { get; set; }

    public int SlotId { get; set; }

    [Required, MaxLength(20)]
    public string MovementType { get; set; } = string.Empty; // INBOUND | OUTBOUND

    [Required, MaxLength(20)]
    public string Result { get; set; } = "COMPLETED";

    [MaxLength(255)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    [ForeignKey(nameof(SlotId))]
    public WarehouseSlot? Slot { get; set; }
}
