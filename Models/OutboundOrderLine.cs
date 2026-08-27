using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("outbound_order_lines")]
public class OutboundOrderLine
{
    [Key]
    public long Id { get; set; }

    public long OutboundOrderId { get; set; }
    public int InfeedItemId { get; set; }
    public int SlotId { get; set; }

    /// <summary>Total physical lot quantity taken from the rack.</summary>
    public int QuantityPicked { get; set; }

    /// <summary>Quantity that is put back into a new lot after a partial pick.</summary>
    public int ResidualQuantity { get; set; }
    public int? ResidualItemId { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "REQUESTED";

    public DateTime? CompletedAt { get; set; }

    [ForeignKey(nameof(OutboundOrderId))]
    public OutboundOrder? OutboundOrder { get; set; }

    [ForeignKey(nameof(InfeedItemId))]
    public InfeedItem? InfeedItem { get; set; }

    [ForeignKey(nameof(SlotId))]
    public WarehouseSlot? Slot { get; set; }

    [ForeignKey(nameof(ResidualItemId))]
    public InfeedItem? ResidualItem { get; set; }
}
