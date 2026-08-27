using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("movement_history")]
public class MovementHistory
{
    [Key]
    public long Id { get; set; }

    public int SlotId { get; set; }

    [Required, MaxLength(40)]
    public string MovementType { get; set; } = string.Empty; // INBOUND | OUTBOUND

    [Required, MaxLength(40)]
    public string Result { get; set; } = "COMPLETED";

    [MaxLength(510)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public int? InfeedItemId { get; set; }

    [ForeignKey(nameof(SlotId))]
    public WarehouseSlot? Slot { get; set; }

    [ForeignKey(nameof(InfeedItemId))]
    public InfeedItem? InfeedItem { get; set; }
}
