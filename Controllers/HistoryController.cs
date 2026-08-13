using AsrsWarehouse.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

public class HistoryController : Controller
{
    private readonly WarehouseDbContext _db;
    public HistoryController(WarehouseDbContext db) => _db = db;

    public async Task<IActionResult> Index()
    {
        var history = await _db.MovementHistories
            .Include(x => x.Slot)
            .OrderByDescending(x => x.CreatedAt)
            .ToListAsync();
        return View(history);
    }
}
