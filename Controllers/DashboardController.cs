using AsrsWarehouse.Data;
using AsrsWarehouse.Models;
using AsrsWarehouse.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsrsWarehouse.Controllers;

public class DashboardController : Controller
{
    private readonly WarehouseDbContext _db;
    private readonly WarehouseWorkflowService _workflow;
    private readonly InboundScanService _scanner;
    private readonly HardwareStatusService _hardwareStatus;
    private readonly CameraOptions _cameraOptions;
    private readonly IQrCameraService _camera;
    private readonly PlcModbusService _plc;

    public DashboardController(
        WarehouseDbContext db,
        WarehouseWorkflowService workflow,
        InboundScanService scanner,
        HardwareStatusService hardwareStatus,
        IOptions<CameraOptions> cameraOptions,
        IQrCameraService camera,
        PlcModbusService plc)
    {
        _db = db;
        _workflow = workflow;
        _scanner = scanner;
        _hardwareStatus = hardwareStatus;
        _cameraOptions = cameraOptions.Value;
        _camera = camera;
        _plc = plc;
    }

    public async Task<IActionResult> Index()
    {
        var slots = await _db.WarehouseSlots
            .Include(x => x.StoredItem)
            .OrderBy(x => x.RowNo).ThenBy(x => x.ColNo)
            .ToListAsync();
        var nextInbound = await _db.InboundQueueItems
            .Include(x => x.InfeedItem)
            .Where(x => x.Status == "READY")
            .OrderBy(x => x.ScannedAt)
            .FirstOrDefaultAsync();
        var readyCount = await _db.InboundQueueItems.CountAsync(x => x.Status == "READY");

        return View(new DashboardViewModel
        {
            Slots = slots,
            NextInbound = nextInbound,
            ReadyInboundCount = readyCount,
            HardwareStatus = _hardwareStatus.GetSnapshot(),
            TriggerDelaySeconds = _cameraOptions.TriggerDelaySeconds
        });
    }

    [HttpPost]
    public async Task<IActionResult> RequestInbound(CancellationToken cancellationToken)
        => Json(await _workflow.RequestInboundAsync(cancellationToken));

    [HttpPost]
    public async Task<IActionResult> StopInbound(CancellationToken cancellationToken)
        => Json(await _workflow.StopInboundAsync(cancellationToken));

    /// <summary>
    /// Commissioning-only PLC motion request. It intentionally bypasses all inbound
    /// queue, slot, item and movement-history workflows.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> RunInfeedMotionTest(CancellationToken cancellationToken)
        => Json(await _plc.StartInfeedMotionTestAsync(cancellationToken));

    public record ManualQrRequest(string? QrValue);

    /// <summary>Fallback for commissioning and a manual scanner. The PLC M202 workflow uses the same staging service.</summary>
    [HttpPost]
    public async Task<IActionResult> ScanQr([FromBody] ManualQrRequest input, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(input.QrValue))
            return Json(new { ok = false, message = "Hãy nhập mã QR hoặc ItemCode." });

        return Json(await _scanner.StageManualQrAsync(input.QrValue, cancellationToken));
    }

    [HttpGet]
    public async Task<IActionResult> Slots()
    {
        var data = await _db.WarehouseSlots
            .Include(x => x.StoredItem)
            .OrderBy(x => x.RowNo).ThenBy(x => x.ColNo)
            .Select(x => new
            {
                x.Id, x.Name, x.RowNo, x.ColNo, x.Status,
                x.SensorOccupied, x.LastSensorUpdate, x.RequestType, x.RequestedAt,
                ItemId = x.StoredItem == null ? (int?)null : x.StoredItem.Id,
                ItemCode = x.StoredItem == null ? null : x.StoredItem.ItemCode,
                ProductName = x.StoredItem == null ? null : x.StoredItem.ProductName
            }).ToListAsync();
        return Json(data);
    }

    [HttpGet]
    public IActionResult HardwareStatus()
        => Json(_hardwareStatus.GetSnapshot());

    /// <summary>Commissioning check: captures an image only; it never queues stock.</summary>
    [HttpPost]
    public async Task<IActionResult> TestCamera(CancellationToken cancellationToken)
    {
        var capture = await _camera.CaptureAsync(cancellationToken);
        _hardwareStatus.SetCaptureResult(capture);
        return Json(new
        {
            ok = capture.Ok,
            message = capture.Ok ? "Camera đã chụp ảnh thử thành công." : capture.Error,
            imagePath = capture.RelativePath
        });
    }
}
