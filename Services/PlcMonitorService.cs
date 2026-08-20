using AsrsWarehouse.Data;
using AsrsWarehouse.Hubs;
using AsrsWarehouse.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Services;

public class PlcMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlcModbusService _plc;
    private readonly IHubContext<WarehouseHub> _hub;

    public PlcMonitorService(
        IServiceScopeFactory scopeFactory,
        PlcModbusService plc,
        IHubContext<WarehouseHub> hub)
    {
        _scopeFactory = scopeFactory;
        _plc = plc;
        _hub = hub;
    }

    protected override async Task ExecuteAsync(
        CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                bool[] sensors = _plc.ReadRackSensors();

                bool r01Occupied = sensors[0];
                bool r02Occupied = sensors[1];

                await UpdateSlot(
                    "R01",
                    r01Occupied,
                    stoppingToken
                );

                await UpdateSlot(
                    "R02",
                    r02Occupied,
                    stoppingToken
                );
            }
            catch (Exception ex)
            {
                Console.WriteLine(
                    $"PLC Modbus error: {ex.Message}"
                );
            }

            await Task.Delay(
                500,
                stoppingToken
            );
        }
    }

    private async Task UpdateSlot(
        string slotName,
        bool occupied,
        CancellationToken cancellationToken)
    {
        using var scope =
            _scopeFactory.CreateScope();

        var db =
            scope.ServiceProvider
                .GetRequiredService<WarehouseDbContext>();

        var slot =
            await db.WarehouseSlots
                .FirstOrDefaultAsync(
                    x => x.Name == slotName,
                    cancellationToken
                );

        if (slot == null)
            return;

        bool oldSensorValue =
            slot.SensorOccupied;

        string oldStatus =
            slot.Status;

        slot.SensorOccupied =
            occupied;

        slot.LastSensorUpdate =
            DateTime.Now;

        /*
         * BLOCK luôn được ưu tiên.
         * Sensor vẫn cập nhật,
         * nhưng Status không được đổi khỏi BLOCK.
         */
        if (slot.Status != "BLOCK")
        {
            /*
             * Nếu không có REQUEST,
             * sensor quyết định EMPTY/OCCUPIED.
             */
            if (slot.Status != "REQUEST")
            {
                slot.Status =
                    occupied
                        ? "OCCUPIED"
                        : "EMPTY";
            }

            /*
             * INBOUND hoàn thành
             */
            if (
                slot.Status == "REQUEST"
                && slot.RequestType == "INBOUND"
                && occupied
            )
            {
                slot.Status = "OCCUPIED";

                db.MovementHistories.Add(
                    new MovementHistory
                    {
                        SlotId = slot.Id,
                        MovementType = "INBOUND",
                        Result = "COMPLETED",
                        Description =
                            $"Nhập hàng vào {slot.Name}",
                        CreatedAt = DateTime.Now
                    }
                );

                slot.RequestType = null;
                slot.RequestedAt = null;
            }

            /*
             * OUTBOUND hoàn thành
             */
            if (
                slot.Status == "REQUEST"
                && slot.RequestType == "OUTBOUND"
                && !occupied
            )
            {
                slot.Status = "EMPTY";

                db.MovementHistories.Add(
                    new MovementHistory
                    {
                        SlotId = slot.Id,
                        MovementType = "OUTBOUND",
                        Result = "COMPLETED",
                        Description =
                            $"Xuất hàng từ {slot.Name}",
                        CreatedAt = DateTime.Now
                    }
                );

                slot.RequestType = null;
                slot.RequestedAt = null;
            }
        }

        bool changed =
            oldSensorValue != slot.SensorOccupied
            || oldStatus != slot.Status;

        if (!changed)
            return;

        db.SensorReadings.Add(
            new SensorReading
            {
                SlotId = slot.Id,
                Occupied = occupied,
                RecordedAt = DateTime.Now
            }
        );

        await db.SaveChangesAsync(
            cancellationToken
        );

        await _hub.Clients.All.SendAsync(
            "slotUpdated",
            new
            {
                name = slot.Name,
                status = slot.Status,
                occupied = slot.SensorOccupied,
                lastUpdate =
                    slot.LastSensorUpdate?
                        .ToString("HH:mm:ss")
            },
            cancellationToken
        );
    }
}