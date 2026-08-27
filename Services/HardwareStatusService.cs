namespace AsrsWarehouse.Services;

/// <summary>
/// In-memory diagnostics for the live PLC/camera chain. This does not write to
/// the warehouse database; it represents the current hardware state only.
/// </summary>
public sealed class HardwareStatusService
{
    private readonly object _gate = new();
    private HardwareStatusSnapshot _snapshot = new(
        PlcConnected: false,
        M202: null,
        PlcMessage: "Chưa nhận được dữ liệu PLC.",
        PlcUpdatedAt: null,
        CameraConnected: false,
        CameraMessage: "Đang kiểm tra camera GigE.",
        CameraDevice: null,
        CameraUpdatedAt: null,
        TriggerState: "Chưa có tín hiệu M202.");

    public HardwareStatusSnapshot GetSnapshot()
    {
        lock (_gate)
            return _snapshot;
    }

    public void SetPlcInput(PlcInputState inputs)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                PlcConnected = true,
                M202 = inputs.CameraTrigger,
                PlcMessage = "Đã kết nối Modbus TCP.",
                PlcUpdatedAt = DateTimeOffset.Now,
                TriggerState = inputs.CameraTrigger ? "M202 = 1: đã nhận trigger camera." : "M202 = 0: đang chờ trigger."
            };
        }
    }

    public void SetPlcFailure(Exception exception)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                PlcConnected = false,
                M202 = null,
                PlcMessage = $"Không đọc được PLC: {exception.Message}",
                PlcUpdatedAt = DateTimeOffset.Now,
                TriggerState = "Chưa thể nhận M202 vì Modbus TCP chưa kết nối."
            };
        }
    }

    public void SetCameraProbe(CameraConnectionResult result)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                CameraConnected = result.Connected,
                CameraMessage = result.Message,
                CameraDevice = result.DeviceName,
                CameraUpdatedAt = DateTimeOffset.Now
            };
        }
    }

    public void SetWaitingForCapture(int delaySeconds)
    {
        lock (_gate)
            _snapshot = _snapshot with { TriggerState = $"M202 = 1: chờ {delaySeconds} giây trước khi chụp ảnh." };
    }

    public void SetCaptureResult(CaptureResult result)
    {
        lock (_gate)
        {
            _snapshot = _snapshot with
            {
                CameraConnected = result.Ok,
                CameraMessage = result.Ok ? "Camera đã chụp ảnh, đang đọc QR." : result.Error ?? "Camera không chụp được ảnh.",
                CameraUpdatedAt = DateTimeOffset.Now,
                TriggerState = result.Ok ? "Đã chụp ảnh từ trigger M202." : "Trigger M202 đã nhận nhưng camera không chụp được."
            };
        }
    }
}

public record HardwareStatusSnapshot(
    bool PlcConnected,
    bool? M202,
    string PlcMessage,
    DateTimeOffset? PlcUpdatedAt,
    bool CameraConnected,
    string CameraMessage,
    string? CameraDevice,
    DateTimeOffset? CameraUpdatedAt,
    string TriggerState);
