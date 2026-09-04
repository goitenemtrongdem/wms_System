namespace AsrsWarehouse.Services;

public class PlcOptions
{
    public string IpAddress { get; set; } = "192.168.3.250";
    public int Port { get; set; } = 502;
    public byte SlaveId { get; set; } = 1;

    // Mitsubishi M200/M201/M202 are Modbus coils 8392/8393/8394.
    public ushort RackSensorStartCoil { get; set; } = 8392;
    public ushort CameraTriggerCoil { get; set; } = 8394;

    // Set these only after confirming the PLC address map. Null keeps writes safe.
    public ushort? InboundRequestCoil { get; set; }
    public ushort? OutboundRequestCoil { get; set; }

    /// <summary>
    /// Dedicated, one-shot commissioning request. The WMS writes M210 (coil 8402)
    /// for CommandPulseMilliseconds after storing the requested pulse count in a
    /// pair of holding registers mapped to PLC D registers.
    /// </summary>
    public ushort? InfeedMotionTestRequestCoil { get; set; }
    public ushort? InfeedMotionTestPulseCountHoldingRegister { get; set; }
    public uint InfeedMotionTestPulseCount { get; set; } = 50_000;
    public int CommandPulseMilliseconds { get; set; } = 500;
}

public class CameraOptions
{
    /// <summary>Required delay between M202 rising and image capture.</summary>
    public int TriggerDelaySeconds { get; set; } = 60;
    public int GrabTimeoutMilliseconds { get; set; } = 10000;

    /// <summary>Index used when more than one GigE camera is discovered.</summary>
    public int CameraIndex { get; set; }

    /// <summary>Optional fixed address of the GigE camera. Leave blank to use CameraIndex.</summary>
    public string? CameraIpAddress { get; set; }

    /// <summary>MV Viewer native 64-bit runtime directory installed on this PC.</summary>
    public string ImvRuntimeDirectory { get; set; } = @"D:\mvView\MV Viewer\Runtime\x64";

    /// <summary>Retained for the existing optional Basler service; GigE is the active driver.</summary>
    public string? PylonAssemblyPath { get; set; }
}

public class StoragePolicyOptions
{
    /// <summary>At or over this weight, choose a lower shelf before an upper shelf.</summary>
    public decimal HeavyItemThresholdKg { get; set; } = 20m;

    /// <summary>True when a larger RowNo is physically lower (the default rack map).</summary>
    public bool LargerRowNoIsLower { get; set; } = true;
}

public record PlcInputState(bool R01Occupied, bool R02Occupied, bool CameraTrigger);
public record OperationResult(bool Ok, string Message, string? PlcMessage = null);
public record InboundRequestResult(bool Ok, string Message, string? SlotName = null, int? ItemId = null, string? ItemCode = null, string? PlcMessage = null);
public record OutboundRequestResult(bool Ok, string Message, long? OrderId = null, int? AllocatedQuantity = null, string? PlcMessage = null);
public record CaptureResult(bool Ok, string? RelativePath, string? Error);
public record CameraConnectionResult(bool Connected, string Message, string? DeviceName = null);
