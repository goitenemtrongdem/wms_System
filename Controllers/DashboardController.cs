using AsrsWarehouse.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

public class DashboardController : Controller
{
    private readonly WarehouseDbContext _db;
    public DashboardController(WarehouseDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var slots = await _db.WarehouseSlots.OrderBy(x => x.RowNo).ThenBy(x => x.ColNo).ToListAsync();
        return View(slots);
    }

    [HttpPost]
    public async Task<IActionResult> RequestInbound()
    {
        var slot = await _db.WarehouseSlots
            .Where(x => x.Status == "EMPTY")
            .OrderBy(x => x.RowNo).ThenBy(x => x.ColNo)
            .FirstOrDefaultAsync();

        if (slot is null)
            return Json(new { ok = false, message = "Không còn ô kệ trống." });

        slot.Status = "REQUEST";
        slot.RequestType = "INBOUND";
        slot.RequestedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Json(new { ok = true, slot = slot.Name, message = $"AS/RS nhập hàng vào {slot.Name}." });
    }

    [HttpPost]
    public async Task<IActionResult> StopInbound()
    {
        var slots = await _db.WarehouseSlots
            .Where(x => x.Status == "REQUEST" && x.RequestType == "INBOUND")
            .ToListAsync();

        foreach (var slot in slots)
        {
            slot.Status = slot.SensorOccupied ? "OCCUPIED" : "EMPTY";
            slot.RequestType = null;
            slot.RequestedAt = null;
        }
        await _db.SaveChangesAsync();
        return Json(new { ok = true, message = "Đã dừng lệnh nhập hàng." });
    }

    [HttpGet]
    public async Task<IActionResult> Slots()
    {
        var data = await _db.WarehouseSlots
            .OrderBy(x => x.RowNo).ThenBy(x => x.ColNo)
            .Select(x => new {
                x.Id, x.Name, x.RowNo, x.ColNo, x.Status,
                x.SensorOccupied, x.LastSensorUpdate, x.RequestType, x.RequestedAt
            }).ToListAsync();
        return Json(data);
    }
}
