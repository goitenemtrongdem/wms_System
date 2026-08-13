using AsrsWarehouse.Data;
using AsrsWarehouse.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

[ApiController]
[Route("api/sensors")]
public class SensorsController : ControllerBase
{
    private readonly WarehouseDbContext _db;
    public SensorsController(WarehouseDbContext db) => _db = db;

    public record SensorUpdate(string SlotName, bool Occupied);

    [HttpPost("update")]
    public async Task<IActionResult> Update([FromBody] SensorUpdate input)
    {
        var slot = await _db.WarehouseSlots.FirstOrDefaultAsync(x => x.Name == input.SlotName);
        if (slot is null) return NotFound(new { ok = false, message = "Không tìm thấy ô kệ." });

        _db.SensorReadings.Add(new SensorReading {
            SlotId = slot.Id,
            Occupied = input.Occupied,
            RecordedAt = DateTime.Now
        });

        slot.SensorOccupied = input.Occupied;
        slot.LastSensorUpdate = DateTime.Now;

        if (slot.Status == "REQUEST" && slot.RequestType == "INBOUND" && input.Occupied)
        {
            slot.Status = "OCCUPIED";
            _db.MovementHistories.Add(new MovementHistory {
                SlotId = slot.Id,
                MovementType = "INBOUND",
                Result = "COMPLETED",
                Description = $"Nhập hàng vào {slot.Name}",
                CreatedAt = DateTime.Now
            });
            slot.RequestType = null;
            slot.RequestedAt = null;
        }
        else if (slot.Status == "REQUEST" && slot.RequestType == "OUTBOUND" && !input.Occupied)
        {
            slot.Status = "EMPTY";
            _db.MovementHistories.Add(new MovementHistory {
                SlotId = slot.Id,
                MovementType = "OUTBOUND",
                Result = "COMPLETED",
                Description = $"Xuất hàng từ {slot.Name}",
                CreatedAt = DateTime.Now
            });
            slot.RequestType = null;
            slot.RequestedAt = null;
        }
        else if (slot.Status != "REQUEST")
        {
            slot.Status = input.Occupied ? "OCCUPIED" : "EMPTY";
        }

        await _db.SaveChangesAsync();
        return Ok(new { ok = true, slot = slot.Name, status = slot.Status });
    }
}
