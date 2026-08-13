using AsrsWarehouse.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

public class TransferController : Controller
{
    private readonly WarehouseDbContext _db;
    public TransferController(WarehouseDbContext db) => _db = db;

    public async Task<IActionResult> Index(string? search)
    {
        var query = _db.WarehouseSlots.Where(x => x.Status == "OCCUPIED");
        if (!string.IsNullOrWhiteSpace(search))
            query = query.Where(x => x.Name.Contains(search));

        ViewBag.Search = search;
        return View(await query.OrderBy(x => x.RowNo).ThenBy(x => x.ColNo).ToListAsync());
    }

    [HttpPost]
    public async Task<IActionResult> RequestOutbound(int slotId)
    {
        var slot = await _db.WarehouseSlots.FindAsync(slotId);
        if (slot is null || slot.Status != "OCCUPIED")
            return Json(new { ok = false, message = "Ô kệ không tồn tại hoặc không còn hàng." });

        slot.Status = "REQUEST";
        slot.RequestType = "OUTBOUND";
        slot.RequestedAt = DateTime.Now;
        await _db.SaveChangesAsync();

        return Json(new { ok = true, slot = slot.Name, message = $"Đã gửi lệnh xuất hàng từ {slot.Name}." });
    }
}
