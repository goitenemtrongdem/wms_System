using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

/// <summary>
/// Pallet/lot read from an infeed QR code. This maps to the pre-existing
/// INFEED_ITEMS table in ASRS_Warehouse.
/// </summary>
[Table("INFEED_ITEMS")]
public class InfeedItem
{
    [Key]
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string ItemCode { get; set; } = string.Empty;

    [Required, MaxLength(100)]
    public string ProductId { get; set; } = string.Empty;

    [Required, MaxLength(400)]
    public string ProductName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string BatchNumber { get; set; } = string.Empty;

    public int Quantity { get; set; }
    public decimal? WeightKg { get; set; }

    [Required, MaxLength(400)]
    public string CompanyName { get; set; } = string.Empty;

    [Column(TypeName = "date")]
    public DateTime? ManufactureDate { get; set; }

    [Column(TypeName = "date")]
    public DateTime? ExpiryDate { get; set; }

    [MaxLength(200)]
    public string? ReceivedBy { get; set; }
    public DateTime ReceivedAt { get; set; } = DateTime.Now;

    [Required, MaxLength(60)]
    public string Status { get; set; } = "RECEIVED";

    public int? CurrentSlotId { get; set; }

    [Required, MaxLength(200)]
    public string QRCodeValue { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Description { get; set; }
    public DateTime UpdatedAt { get; set; } = DateTime.Now;

    [MaxLength(1000)]
    public string? QRCode { get; set; }

    [MaxLength(1000)]
    public string? QRCodeImagePath { get; set; }

    public int? ParentItemId { get; set; }

    [ForeignKey(nameof(CurrentSlotId))]
    public WarehouseSlot? CurrentSlot { get; set; }

    [ForeignKey(nameof(ParentItemId))]
    public InfeedItem? ParentItem { get; set; }

    public ICollection<InboundQueueItem> InboundQueueItems { get; set; } = [];
    public ICollection<OutboundOrderLine> OutboundOrderLines { get; set; } = [];
}
