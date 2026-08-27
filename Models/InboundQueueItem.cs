using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

/// <summary>
/// Durable temporary storage between a QR scan and the physical arrival of a
/// pallet in a rack. It deliberately survives application restarts.
/// </summary>
[Table("inbound_queue")]
public class InboundQueueItem
{
    [Key]
    public long Id { get; set; }

    public int InfeedItemId { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "READY"; // READY | REQUESTED | STORED | CANCELLED | SCAN_FAILED

    [Required, MaxLength(500)]
    public string QRValue { get; set; } = string.Empty;

    [Required, MaxLength(30)]
    public string ScanSource { get; set; } = "CAMERA"; // CAMERA | MANUAL | SPLIT_RETURN

    public DateTime ScannedAt { get; set; } = DateTime.Now;

    [MaxLength(500)]
    public string? CaptureImagePath { get; set; }

    public int? TargetSlotId { get; set; }
    public DateTime? RequestedAt { get; set; }
    public DateTime? StoredAt { get; set; }

    [MaxLength(500)]
    public string? FailureReason { get; set; }

    [ForeignKey(nameof(InfeedItemId))]
    public InfeedItem? InfeedItem { get; set; }

    [ForeignKey(nameof(TargetSlotId))]
    public WarehouseSlot? TargetSlot { get; set; }
}
