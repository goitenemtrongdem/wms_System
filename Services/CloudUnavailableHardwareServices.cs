#if CLOUD_BUILD
namespace AsrsWarehouse.Services;

/// <summary>
/// Railway hosts the web application on Linux and cannot access the warehouse's
/// Windows-only camera SDK or its private factory network. The hardware adapter
/// must run on a Windows edge machine inside the warehouse.
/// </summary>
public sealed class CloudUnavailableCameraService : IQrCameraService
{
    private const string Message = "Camera GigE chỉ hoạt động trên máy edge Windows trong kho; Railway không thể truy cập thiết bị nội bộ.";

    public Task<CaptureResult> CaptureAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new CaptureResult(false, null, Message));

    public Task<CameraConnectionResult> ProbeAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(new CameraConnectionResult(false, Message));
}

public sealed class CloudUnavailableQrCodeReaderService : IQrCodeReaderService
{
    public string? Decode(string imagePath) => null;
}
#endif
