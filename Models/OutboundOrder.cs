using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AsrsWarehouse.Models;

[Table("outbound_orders")]
public class OutboundOrder
{
    [Key]
    public long Id { get; set; }

    [Required, MaxLength(200)]
    public string ProductName { get; set; } = string.Empty;

    public int RequestedQuantity { get; set; }
    public int AllocatedQuantity { get; set; }

    [Required, MaxLength(30)]
    public string Status { get; set; } = "REQUESTED"; // REQUESTED | PARTIAL | COMPLETED | CANCELLED

    public DateTime RequestedAt { get; set; } = DateTime.Now;
    public DateTime? CompletedAt { get; set; }

    public ICollection<OutboundOrderLine> Lines { get; set; } = [];
}
