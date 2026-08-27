using AsrsWarehouse.Data;
using AsrsWarehouse.Hubs;
using AsrsWarehouse.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsrsWarehouse.Services;

/// <summary>
/// Single owner of rack state transitions. The PLC monitor and the test sensor
/// API use the same methods, so an item cannot be linked to a slot by one path
/// but omitted by the other.
/// </summary>
public class WarehouseWorkflowService
{
    private readonly WarehouseDbContext _db;
    private readonly PlcModbusService _plc;
    private readonly IHubContext<WarehouseHub> _hub;
    private readonly StoragePolicyOptions _storagePolicy;
    private readonly ILogger<WarehouseWorkflowService> _logger;

    public WarehouseWorkflowService(
        WarehouseDbContext db,
        PlcModbusService plc,
        IHubContext<WarehouseHub> hub,
        IOptions<StoragePolicyOptions> storagePolicy,
        ILogger<WarehouseWorkflowService> logger)
    {
        _db = db;
        _plc = plc;
        _hub = hub;
        _storagePolicy = storagePolicy.Value;
        _logger = logger;
    }

    public async Task<InboundRequestResult> RequestInboundAsync(CancellationToken cancellationToken = default)
    {
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var queueItem = await _db.InboundQueueItems
            .Include(x => x.InfeedItem)
            .Where(x => x.Status == "READY")
            .OrderBy(x => x.ScannedAt)
            .FirstOrDefaultAsync(cancellationToken);

        if (queueItem?.InfeedItem is null)
            return new InboundRequestResult(false, "Chưa có dữ liệu QR trong hàng đợi. Hãy quét QR trước khi Nhập hàng.");

        var candidates = _db.WarehouseSlots.Where(x => x.Status == "EMPTY" && !x.SensorOccupied);
        var isHeavy = queueItem.InfeedItem.WeightKg.GetValueOrDefault() >= _storagePolicy.HeavyItemThresholdKg;
        var useLowerFirst = isHeavy == _storagePolicy.LargerRowNoIsLower;
        var slot = useLowerFirst
            ? await candidates.OrderByDescending(x => x.RowNo).ThenBy(x => x.ColNo).FirstOrDefaultAsync(cancellationToken)
            : await candidates.OrderBy(x => x.RowNo).ThenBy(x => x.ColNo).FirstOrDefaultAsync(cancellationToken);

        if (slot is null)
            return new InboundRequestResult(false, "Không còn ô kệ trống.");

        slot.Status = "REQUEST";
        slot.RequestType = "INBOUND";
        slot.RequestedAt = DateTime.Now;

        queueItem.Status = "REQUESTED";
        queueItem.TargetSlotId = slot.Id;
        queueItem.RequestedAt = DateTime.Now;
        queueItem.InfeedItem.Status = "INBOUND_REQUESTED";
        queueItem.InfeedItem.UpdatedAt = DateTime.Now;

        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var plcMessage = await _plc.PulseInboundCommandAsync(cancellationToken);
        var levelText = isHeavy ? "ưu tiên tầng dưới" : "ưu tiên tầng trên";
        return new InboundRequestResult(
            true,
            $"Đã tạo lệnh nhập {queueItem.InfeedItem.ItemCode} vào {slot.Name} ({levelText}). Item ID chỉ hiển thị sau khi cảm biến xác nhận pallet đã vào ô.",
            slot.Name,
            queueItem.InfeedItem.Id,
            queueItem.InfeedItem.ItemCode,
            plcMessage);
    }

    public async Task<OperationResult> StopInboundAsync(CancellationToken cancellationToken = default)
    {
        var queueItems = await _db.InboundQueueItems
            .Include(x => x.InfeedItem)
            .Include(x => x.TargetSlot)
            .Where(x => x.Status == "REQUESTED")
            .ToListAsync(cancellationToken);

        foreach (var queueItem in queueItems.Where(x => x.TargetSlot?.SensorOccupied != true))
        {
            if (queueItem.TargetSlot is not null)
            {
                queueItem.TargetSlot.Status = "EMPTY";
                queueItem.TargetSlot.RequestType = null;
                queueItem.TargetSlot.RequestedAt = null;
            }

            queueItem.Status = "READY";
            queueItem.TargetSlotId = null;
            queueItem.RequestedAt = null;
            if (queueItem.InfeedItem is not null)
            {
                queueItem.InfeedItem.Status = "SCANNED";
                queueItem.InfeedItem.UpdatedAt = DateTime.Now;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);
        return new OperationResult(true, "Đã dừng các lệnh nhập chưa có pallet vào ô. Dữ liệu QR vẫn nằm trong hàng đợi.");
    }

    public async Task<OperationResult> UpdateSlotSensorAsync(string slotName, bool occupied, CancellationToken cancellationToken = default)
    {
        var slot = await _db.WarehouseSlots.FirstOrDefaultAsync(x => x.Name == slotName, cancellationToken);
        if (slot is null)
            return new OperationResult(false, "Không tìm thấy ô kệ.");

        var oldOccupied = slot.SensorOccupied;
        var oldStatus = slot.Status;
        slot.SensorOccupied = occupied;
        slot.LastSensorUpdate = DateTime.Now;

        int? itemId = null;
        if (slot.Status != "BLOCK")
        {
            if (slot.Status == "REQUEST" && slot.RequestType == "INBOUND" && occupied)
            {
                itemId = await CompleteInboundAsync(slot, cancellationToken);
            }
            else if (slot.Status == "REQUEST" && slot.RequestType == "OUTBOUND" && !occupied)
            {
                itemId = await CompleteOutboundAsync(slot, cancellationToken);
            }
            else if (slot.Status != "REQUEST")
            {
                slot.Status = occupied ? "OCCUPIED" : "EMPTY";
            }
        }

        var changed = oldOccupied != occupied || oldStatus != slot.Status;
        if (!changed)
            return new OperationResult(true, "Không có thay đổi trạng thái.");

        _db.SensorReadings.Add(new SensorReading
        {
            SlotId = slot.Id,
            Occupied = occupied,
            InfeedItemId = itemId,
            RecordedAt = DateTime.Now
        });

        await _db.SaveChangesAsync(cancellationToken);
        await SendSlotUpdatedAsync(slot, itemId, cancellationToken);
        return new OperationResult(true, $"Đã cập nhật {slot.Name} thành {slot.Status}.");
    }

    public async Task<OutboundRequestResult> RequestOutboundByProductAsync(
        string productName,
        int requestedQuantity,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(productName) || requestedQuantity <= 0)
            return new OutboundRequestResult(false, "Hãy nhập tên mặt hàng và số lượng lớn hơn 0.");

        var normalizedName = productName.Trim();
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        var lots = await _db.InfeedItems
            .Include(x => x.CurrentSlot)
            .Where(x => x.Status == "STORED" && x.CurrentSlotId != null && x.ProductName == normalizedName)
            .OrderBy(x => x.ExpiryDate == null)
            .ThenBy(x => x.ExpiryDate)
            .ThenBy(x => x.ReceivedAt)
            .ToListAsync(cancellationToken);

        var availableQuantity = lots.Sum(x => x.Quantity);
        if (availableQuantity < requestedQuantity)
            return new OutboundRequestResult(false, $"Mặt hàng {normalizedName} chỉ còn {availableQuantity}; không đủ số lượng yêu cầu {requestedQuantity}.");

        var order = new OutboundOrder
        {
            ProductName = normalizedName,
            RequestedQuantity = requestedQuantity,
            Status = "REQUESTED",
            RequestedAt = DateTime.Now
        };
        _db.OutboundOrders.Add(order);

        var remaining = requestedQuantity;
        var physicallyAllocated = 0;
        foreach (var lot in lots)
        {
            if (remaining <= 0 || lot.CurrentSlot is null)
                break;

            var requiredFromLot = Math.Min(remaining, lot.Quantity);
            var residualQuantity = lot.Quantity - requiredFromLot;
            var physicalPickQuantity = lot.Quantity; // A partial pick brings out the complete pallet lot.

            _db.OutboundOrderLines.Add(new OutboundOrderLine
            {
                OutboundOrder = order,
                InfeedItem = lot,
                SlotId = lot.CurrentSlotId!.Value,
                QuantityPicked = physicalPickQuantity,
                ResidualQuantity = residualQuantity,
                Status = "REQUESTED"
            });

            lot.Status = "OUTBOUND_REQUESTED";
            lot.UpdatedAt = DateTime.Now;
            lot.CurrentSlot.Status = "REQUEST";
            lot.CurrentSlot.RequestType = "OUTBOUND";
            lot.CurrentSlot.RequestedAt = DateTime.Now;

            remaining -= requiredFromLot;
            physicallyAllocated += physicalPickQuantity;
        }

        order.AllocatedQuantity = physicallyAllocated;
        await _db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        var plcMessage = await _plc.PulseOutboundCommandAsync(cancellationToken);
        var excess = physicallyAllocated - requestedQuantity;
        var message = excess > 0
            ? $"Đã tạo phiếu xuất #{order.Id}: yêu cầu {requestedQuantity}, lấy ra {physicallyAllocated}. {excess} sản phẩm dư sẽ thành lô nhập lại sau khi pallet ra khỏi kệ."
            : $"Đã tạo phiếu xuất #{order.Id}: {requestedQuantity} sản phẩm theo FEFO (hạn dùng gần nhất trước).";

        return new OutboundRequestResult(true, message, order.Id, physicallyAllocated, plcMessage);
    }

    /// <summary>Legacy endpoint retained for a direct rack command.</summary>
    public async Task<OperationResult> RequestOutboundBySlotAsync(int slotId, CancellationToken cancellationToken = default)
    {
        var slot = await _db.WarehouseSlots.FindAsync([slotId], cancellationToken);
        if (slot is null || slot.Status != "OCCUPIED")
            return new OperationResult(false, "Ô kệ không tồn tại hoặc không còn hàng.");

        var item = await _db.InfeedItems.FirstOrDefaultAsync(x => x.CurrentSlotId == slot.Id, cancellationToken);
        if (item is not null)
        {
            item.Status = "OUTBOUND_REQUESTED";
            item.UpdatedAt = DateTime.Now;
        }

        slot.Status = "REQUEST";
        slot.RequestType = "OUTBOUND";
        slot.RequestedAt = DateTime.Now;
        await _db.SaveChangesAsync(cancellationToken);

        var plcMessage = await _plc.PulseOutboundCommandAsync(cancellationToken);
        return new OperationResult(true, $"Đã gửi lệnh xuất hàng từ {slot.Name}.", plcMessage);
    }

    private async Task<int?> CompleteInboundAsync(WarehouseSlot slot, CancellationToken cancellationToken)
    {
        var queueItem = await _db.InboundQueueItems
            .Include(x => x.InfeedItem)
            .FirstOrDefaultAsync(x => x.TargetSlotId == slot.Id && x.Status == "REQUESTED", cancellationToken);

        if (queueItem?.InfeedItem is null)
        {
            _logger.LogWarning("Inbound request on {Slot} had no queued item.", slot.Name);
            slot.Status = "OCCUPIED";
            slot.RequestType = null;
            slot.RequestedAt = null;
            return null;
        }

        var item = queueItem.InfeedItem;
        item.CurrentSlotId = slot.Id;
        item.Status = "STORED";
        item.UpdatedAt = DateTime.Now;
        queueItem.Status = "STORED";
        queueItem.StoredAt = DateTime.Now;
        slot.Status = "OCCUPIED";
        slot.RequestType = null;
        slot.RequestedAt = null;

        _db.MovementHistories.Add(new MovementHistory
        {
            SlotId = slot.Id,
            InfeedItemId = item.Id,
            MovementType = "INBOUND",
            Result = "COMPLETED",
            Description = $"Nhập item {item.ItemCode} vào {slot.Name}",
            CreatedAt = DateTime.Now
        });

        return item.Id;
    }

    private async Task<int?> CompleteOutboundAsync(WarehouseSlot slot, CancellationToken cancellationToken)
    {
        var line = await _db.OutboundOrderLines
            .Include(x => x.InfeedItem)
            .Include(x => x.OutboundOrder)
            .FirstOrDefaultAsync(x => x.SlotId == slot.Id && x.Status == "REQUESTED", cancellationToken);

        var item = line?.InfeedItem
            ?? await _db.InfeedItems.FirstOrDefaultAsync(x => x.CurrentSlotId == slot.Id, cancellationToken);

        slot.Status = "EMPTY";
        slot.RequestType = null;
        slot.RequestedAt = null;

        if (item is null)
            return null;

        item.CurrentSlotId = null;
        item.Status = "OUTBOUND_COMPLETED";
        item.UpdatedAt = DateTime.Now;

        if (line is not null)
        {
            line.Status = "COMPLETED";
            line.CompletedAt = DateTime.Now;
            if (line.ResidualQuantity > 0)
            {
                var residual = CreateResidualLot(item, line.ResidualQuantity);
                _db.InfeedItems.Add(residual);
                _db.InboundQueueItems.Add(new InboundQueueItem
                {
                    InfeedItem = residual,
                    Status = "READY",
                    QRValue = residual.QRCodeValue,
                    ScanSource = "SPLIT_RETURN",
                    ScannedAt = DateTime.Now
                });
                line.ResidualItem = residual;
            }

            if (line.OutboundOrder is not null)
            {
                var hasOtherRequestedLines = await _db.OutboundOrderLines
                    .AnyAsync(x => x.OutboundOrderId == line.OutboundOrderId && x.Id != line.Id && x.Status != "COMPLETED", cancellationToken);
                line.OutboundOrder.Status = hasOtherRequestedLines ? "PARTIAL" : "COMPLETED";
                line.OutboundOrder.CompletedAt = hasOtherRequestedLines ? null : DateTime.Now;
            }
        }

        _db.MovementHistories.Add(new MovementHistory
        {
            SlotId = slot.Id,
            InfeedItemId = item.Id,
            MovementType = "OUTBOUND",
            Result = "COMPLETED",
            Description = line?.ResidualQuantity > 0
                ? $"Xuất item {item.ItemCode} từ {slot.Name}; phần dư tạo lô nhập lại."
                : $"Xuất item {item.ItemCode} từ {slot.Name}",
            CreatedAt = DateTime.Now
        });

        return item.Id;
    }

    private static InfeedItem CreateResidualLot(InfeedItem original, int residualQuantity)
    {
        var suffix = $"-R{DateTime.Now:yyMMddHHmmssfff}";
        var baseCode = original.ItemCode.Length > 75 ? original.ItemCode[..75] : original.ItemCode;
        var qrPrefix = original.QRCodeValue.Length > 165 ? original.QRCodeValue[..165] : original.QRCodeValue;
        var originalDescription = original.Description?.Length > 900 ? original.Description[..900] : original.Description;
        var proportionalWeight = original.WeightKg.HasValue && original.Quantity > 0
            ? decimal.Round(original.WeightKg.Value * residualQuantity / original.Quantity, 2)
            : original.WeightKg;

        return new InfeedItem
        {
            ItemCode = baseCode + suffix,
            ProductId = original.ProductId,
            ProductName = original.ProductName,
            BatchNumber = original.BatchNumber,
            Quantity = residualQuantity,
            WeightKg = proportionalWeight,
            CompanyName = original.CompanyName,
            ManufactureDate = original.ManufactureDate,
            ExpiryDate = original.ExpiryDate,
            ReceivedBy = "SYSTEM_SPLIT",
            ReceivedAt = DateTime.Now,
            Status = "SCANNED",
            QRCodeValue = $"{qrPrefix}-RESIDUAL-{DateTime.Now:yyyyMMddHHmmssfff}",
            QRCode = original.QRCode,
            QRCodeImagePath = original.QRCodeImagePath,
            Description = $"Lô dư tách từ item ID {original.Id}. {originalDescription}",
            UpdatedAt = DateTime.Now,
            ParentItemId = original.Id
        };
    }

    private async Task SendSlotUpdatedAsync(WarehouseSlot slot, int? itemId, CancellationToken cancellationToken)
    {
        // An outbound sensor event is still linked in sensor_readings/history,
        // but the live rack must immediately clear its Item ID.
        InfeedItem? item = null;
        if (slot.Status == "OCCUPIED")
        {
            item = itemId.HasValue
                ? await _db.InfeedItems.FindAsync([itemId.Value], cancellationToken)
                : await _db.InfeedItems.FirstOrDefaultAsync(x => x.CurrentSlotId == slot.Id, cancellationToken);
        }

        await _hub.Clients.All.SendAsync("slotUpdated", new
        {
            name = slot.Name,
            status = slot.Status,
            occupied = slot.SensorOccupied,
            requestType = slot.RequestType,
            itemId = item?.Id,
            itemCode = item?.ItemCode,
            lastUpdate = slot.LastSensorUpdate?.ToString("HH:mm:ss")
        }, cancellationToken);
    }
}
