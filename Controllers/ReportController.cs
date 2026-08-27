using AsrsWarehouse.Data;
using AsrsWarehouse.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

public class ReportController : Controller
{
    private readonly WarehouseDbContext _db;
    public ReportController(WarehouseDbContext db) => _db = db;

    public async Task<IActionResult> Index(string period = "month")
    {
        var now = DateTime.Now;
        var from = period.ToLowerInvariant() switch
        {
            "week" => now.Date.AddDays(-6),
            "year" => new DateTime(now.Year, 1, 1),
            _ => new DateTime(now.Year, now.Month, 1)
        };
        var to = now;
        var expiryLimit = now.Date.AddDays(30);

        var storedItems = _db.InfeedItems.Where(x => x.Status == "STORED" && x.CurrentSlotId != null);
        var products = await storedItems
            .GroupBy(x => x.ProductName)
            .Select(group => new ProductReportRow
            {
                ProductName = group.Key,
                LotCount = group.Count(),
                Quantity = group.Sum(x => x.Quantity),
                NearestExpiry = group.Min(x => x.ExpiryDate)
            })
            .OrderByDescending(x => x.Quantity)
            .ToListAsync();

        var model = new ReportViewModel
        {
            From = from,
            To = to,
            InboundMovements = await _db.MovementHistories.CountAsync(x => x.MovementType == "INBOUND" && x.CreatedAt >= from && x.CreatedAt <= to),
            OutboundMovements = await _db.MovementHistories.CountAsync(x => x.MovementType == "OUTBOUND" && x.CreatedAt >= from && x.CreatedAt <= to),
            StoredLots = await storedItems.CountAsync(),
            StoredQuantity = await storedItems.SumAsync(x => (int?)x.Quantity) ?? 0,
            ExpiringLots = await storedItems.CountAsync(x => x.ExpiryDate.HasValue && x.ExpiryDate.Value <= expiryLimit),
            Products = products
        };
        ViewBag.Period = period.ToLowerInvariant();
        return View(model);
    }
}
