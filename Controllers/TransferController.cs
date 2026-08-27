using AsrsWarehouse.Data;
using AsrsWarehouse.Models;
using AsrsWarehouse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AsrsWarehouse.Controllers;

public class TransferController : Controller
{
    private readonly WarehouseDbContext _db;
    private readonly WarehouseWorkflowService _workflow;

    public TransferController(WarehouseDbContext db, WarehouseWorkflowService workflow)
    {
        _db = db;
        _workflow = workflow;
    }

    public async Task<IActionResult> Index(string? search)
    {
        var stored = _db.InfeedItems
            .Include(x => x.CurrentSlot)
            .Where(x => x.Status == "STORED" && x.CurrentSlotId != null);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var value = search.Trim();
            stored = stored.Where(x => x.ProductName.Contains(value) || x.ItemCode.Contains(value) || x.CurrentSlot!.Name.Contains(value));
        }

        var model = new TransferViewModel
        {
            Search = search,
            StoredItems = await stored
                .OrderBy(x => x.ExpiryDate == null).ThenBy(x => x.ExpiryDate)
                .ThenBy(x => x.ProductName)
                .ToListAsync(),
            UnlinkedOccupiedSlots = await _db.WarehouseSlots
                .Where(x => x.Status == "OCCUPIED" && !_db.InfeedItems.Any(i => i.CurrentSlotId == x.Id))
                .OrderBy(x => x.RowNo).ThenBy(x => x.ColNo)
                .ToListAsync(),
            RecentOrders = await _db.OutboundOrders
                .Include(x => x.Lines)
                .OrderByDescending(x => x.RequestedAt)
                .Take(8)
                .ToListAsync()
        };
        return View(model);
    }

    public record OutboundRequest(string? ProductName, int Quantity);

    [HttpPost]
    public async Task<IActionResult> CreateOutboundOrder([FromBody] OutboundRequest input, CancellationToken cancellationToken)
        => Json(await _workflow.RequestOutboundByProductAsync(input.ProductName ?? string.Empty, input.Quantity, cancellationToken));

    /// <summary>Backwards-compatible direct output command used by the original UI/API.</summary>
    [HttpPost]
    public async Task<IActionResult> RequestOutbound(int slotId, CancellationToken cancellationToken)
        => Json(await _workflow.RequestOutboundBySlotAsync(slotId, cancellationToken));
}
