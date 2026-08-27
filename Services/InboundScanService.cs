using System.Text.Json;
using AsrsWarehouse.Data;
using AsrsWarehouse.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AsrsWarehouse.Services;

public class InboundScanService
{
    private readonly WarehouseDbContext _db;
    private readonly IQrCameraService _camera;
    private readonly IQrCodeReaderService _qrReader;
    private readonly CameraOptions _options;
    private readonly ILogger<InboundScanService> _logger;

    public InboundScanService(
        WarehouseDbContext db,
        IQrCameraService camera,
        IQrCodeReaderService qrReader,
        IOptions<CameraOptions> options,
        ILogger<InboundScanService> logger)
    {
        _db = db;
        _camera = camera;
        _qrReader = qrReader;
        _options = options.Value;
        _logger = logger;
    }

   public async Task<OperationResult> CaptureAfterPlcTriggerAsync(CancellationToken cancellationToken)
{
    await Task.Delay(TimeSpan.FromSeconds(_options.TriggerDelaySeconds), cancellationToken);
    var capture = await _camera.CaptureAsync(cancellationToken);
        if (!capture.Ok || capture.RelativePath is null)
            return new OperationResult(false, capture.Error ?? "Không thể chụp ảnh camera GigE.");

        var imagePath = Path.Combine(
            Directory.GetCurrentDirectory(), "wwwroot", capture.RelativePath.TrimStart('/').Replace('/', Path.DirectorySeparatorChar));
        var qrValue = _qrReader.Decode(imagePath);
        if (string.IsNullOrWhiteSpace(qrValue))
            return new OperationResult(false, "Không đọc được QR code trong ảnh camera GigE.");

        return await StageQrAsync(qrValue, "CAMERA", capture.RelativePath, cancellationToken);
    }

    public Task<OperationResult> StageManualQrAsync(string qrValue, CancellationToken cancellationToken = default)
        => StageQrAsync(qrValue, "MANUAL", null, cancellationToken);

    private async Task<OperationResult> StageQrAsync(string rawQrValue, string source, string? capturePath, CancellationToken cancellationToken)
    {
        var qrValue = rawQrValue.Trim();
        var item = await _db.InfeedItems
            .FirstOrDefaultAsync(x => x.QRCodeValue == qrValue || x.QRCode == qrValue || x.ItemCode == qrValue, cancellationToken);

        if (item is null)
            item = TryCreateItemFromJson(qrValue);

        if (item is null)
            return new OperationResult(false, "QR chưa có dữ liệu tương ứng trong INFEED_ITEMS. Hãy tạo mặt hàng trước hoặc dùng QR JSON đầy đủ.");

        if (item.CurrentSlotId.HasValue || item.Status is "STORED" or "OUTBOUND_REQUESTED")
            return new OperationResult(false, $"Item {item.ItemCode} đang nằm trong ô kệ hoặc đang chờ xuất.");

        if (item.Id == 0)
            _db.InfeedItems.Add(item);

        var duplicate = await _db.InboundQueueItems
            .AnyAsync(x => x.InfeedItemId == item.Id && (x.Status == "READY" || x.Status == "REQUESTED"), cancellationToken);
        if (duplicate)
            return new OperationResult(false, $"Item {item.ItemCode} đã có trong hàng đợi nhập.");

        item.Status = "SCANNED";
        item.UpdatedAt = DateTime.Now;
        _db.InboundQueueItems.Add(new InboundQueueItem
        {
            InfeedItem = item,
            Status = "READY",
            QRValue = qrValue,
            ScanSource = source,
            ScannedAt = DateTime.Now,
            CaptureImagePath = capturePath
        });

        await _db.SaveChangesAsync(cancellationToken);
        _logger.LogInformation("QR staged for inbound: {ItemCode} via {Source}", item.ItemCode, source);
        return new OperationResult(true, $"Đã quét {item.ItemCode} ({item.ProductName}) và đưa vào hàng đợi nhập.");
    }

    private static InfeedItem? TryCreateItemFromJson(string qrValue)
    {
        try
        {
            using var document = JsonDocument.Parse(qrValue);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;

            var root = document.RootElement;
            var itemCode = GetString(root, "itemCode", "ItemCode", "code");
            var productId = GetString(root, "productId", "ProductId");
            var productName = GetString(root, "productName", "ProductName", "name");
            var batchNumber = GetString(root, "batchNumber", "BatchNumber", "batch");
            var companyName = GetString(root, "companyName", "CompanyName", "company");

            if (string.IsNullOrWhiteSpace(itemCode) || string.IsNullOrWhiteSpace(productId)
                || string.IsNullOrWhiteSpace(productName) || string.IsNullOrWhiteSpace(batchNumber)
                || string.IsNullOrWhiteSpace(companyName))
                return null;

            return new InfeedItem
            {
                ItemCode = itemCode,
                ProductId = productId,
                ProductName = productName,
                BatchNumber = batchNumber,
                CompanyName = companyName,
                Quantity = GetInt(root, "quantity", "Quantity") ?? 1,
                WeightKg = GetDecimal(root, "weightKg", "WeightKg", "weight"),
                ManufactureDate = GetDate(root, "manufactureDate", "ManufactureDate"),
                ExpiryDate = GetDate(root, "expiryDate", "ExpiryDate"),
                Description = GetString(root, "description", "Description"),
                QRCodeValue = qrValue,
                QRCode = qrValue,
                Status = "SCANNED",
                ReceivedAt = DateTime.Now,
                UpdatedAt = DateTime.Now
            };
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? GetString(JsonElement element, params string[] names)
        => names.Select(name => element.TryGetProperty(name, out var value) ? value.GetString() : null)
            .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private static int? GetInt(JsonElement element, params string[] names)
        => names.Select(name => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : (int?)null)
            .FirstOrDefault(value => value.HasValue);

    private static decimal? GetDecimal(JsonElement element, params string[] names)
        => names.Select(name => element.TryGetProperty(name, out var value) && value.TryGetDecimal(out var number) ? number : (decimal?)null)
            .FirstOrDefault(value => value.HasValue);

    private static DateTime? GetDate(JsonElement element, params string[] names)
        => names.Select(name => element.TryGetProperty(name, out var value) && value.TryGetDateTime(out var date) ? date : (DateTime?)null)
            .FirstOrDefault(value => value.HasValue);
}
