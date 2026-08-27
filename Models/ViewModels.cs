using AsrsWarehouse.Services;

namespace AsrsWarehouse.Models;

public class DashboardViewModel
{
    public List<WarehouseSlot> Slots { get; init; } = [];
    public int ReadyInboundCount { get; init; }
    public InboundQueueItem? NextInbound { get; init; }
    public HardwareStatusSnapshot HardwareStatus { get; init; } = new(false, null, "Đang kiểm tra PLC.", null, false, "Đang kiểm tra camera.", null, null, "Chưa có dữ liệu.");
    public int TriggerDelaySeconds { get; init; }
}

public class TransferViewModel
{
    public List<InfeedItem> StoredItems { get; init; } = [];
    public List<WarehouseSlot> UnlinkedOccupiedSlots { get; init; } = [];
    public List<OutboundOrder> RecentOrders { get; init; } = [];
    public string? Search { get; init; }
}

public class ReportViewModel
{
    public DateTime From { get; init; }
    public DateTime To { get; init; }
    public int InboundMovements { get; init; }
    public int OutboundMovements { get; init; }
    public int StoredLots { get; init; }
    public int StoredQuantity { get; init; }
    public int ExpiringLots { get; init; }
    public List<ProductReportRow> Products { get; init; } = [];
}

public class ProductReportRow
{
    public string ProductName { get; init; } = string.Empty;
    public int LotCount { get; init; }
    public int Quantity { get; init; }
    public DateTime? NearestExpiry { get; init; }
}
