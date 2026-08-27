namespace AsrsWarehouse.Services;

public interface IQrCameraService
{
    Task<CaptureResult> CaptureAsync(CancellationToken cancellationToken = default);
    Task<CameraConnectionResult> ProbeAsync(CancellationToken cancellationToken = default);
}
