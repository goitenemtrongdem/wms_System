using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using MVSDK_Net;

namespace AsrsWarehouse.Services;

/// <summary>
/// Captures a still image with the GigE Vision camera through the installed
/// MV Viewer .NET SDK (MVSDK_Net.dll/MVSDKmd.dll).
/// </summary>
public sealed class ImvGigECameraService : IQrCameraService
{
    private readonly IWebHostEnvironment _environment;
    private readonly CameraOptions _options;
    private readonly ILogger<ImvGigECameraService> _logger;
    private readonly SemaphoreSlim _cameraLock = new(1, 1);

    public ImvGigECameraService(
        IWebHostEnvironment environment,
        IOptions<CameraOptions> options,
        ILogger<ImvGigECameraService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public Task<CameraConnectionResult> ProbeAsync(CancellationToken cancellationToken = default)
        => Task.Run(Probe, cancellationToken);

    public async Task<CaptureResult> CaptureAsync(CancellationToken cancellationToken = default)
    {
        await _cameraLock.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(Capture, cancellationToken);
        }
        finally
        {
            _cameraLock.Release();
        }
    }

    private CameraConnectionResult Probe()
    {
        try
        {
            ConfigureNativeRuntime();
            var result = Enumerate(out var devices);
            if (result != IMVDefine.IMV_OK)
                return new CameraConnectionResult(false, $"MV Viewer không thể tìm camera (mã SDK {result}).");
            if (devices.nDevNum == 0)
                return new CameraConnectionResult(false, "Không tìm thấy camera GigE Vision. Kiểm tra nguồn, dây mạng và IP camera.");

            var device = GetDeviceInfo(devices, GetSelectedIndex(devices));
            return new CameraConnectionResult(true, "Camera GigE Vision đã sẵn sàng.", DescribeDevice(device));
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not probe the GigE Vision camera.");
            return new CameraConnectionResult(false, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private CaptureResult Capture()
    {
        var camera = new MyCamera();
        var opened = false;
        var grabbing = false;
        var frameReceived = false;
        var handleCreated = false;
        var frame = new IMVDefine.IMV_Frame();

        try
        {
            ConfigureNativeRuntime();
            var enumResult = Enumerate(out var devices);
            if (enumResult != IMVDefine.IMV_OK)
                return Failure($"MV Viewer không thể tìm camera (mã SDK {enumResult}).");
            if (devices.nDevNum == 0)
                return Failure("Không tìm thấy camera GigE Vision. Kiểm tra nguồn, dây mạng và IP camera.");

            var selectedIndex = GetSelectedIndex(devices);
            var createResult = string.IsNullOrWhiteSpace(_options.CameraIpAddress)
                ? camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIndex, selectedIndex)
                : camera.IMV_CreateHandle(IMVDefine.IMV_ECreateHandleMode.modeByIPAddress, 0, _options.CameraIpAddress);
            if (createResult != IMVDefine.IMV_OK)
                return Failure($"Không tạo được kết nối camera (mã SDK {createResult}).");
            handleCreated = true;

            var openResult = camera.IMV_Open();
            if (openResult != IMVDefine.IMV_OK)
                return Failure($"Không mở được camera (mã SDK {openResult}). Có thể MV Viewer đang giữ camera.");
            opened = true;

            // PLC M202 is the trigger for this WMS, so take a free-running frame after its delay.
            var triggerResult = camera.IMV_SetEnumFeatureSymbol("TriggerMode", "Off");
            if (triggerResult != IMVDefine.IMV_OK)
                return Failure($"Không đặt được TriggerMode của camera (mã SDK {triggerResult}).");

            var startResult = camera.IMV_StartGrabbing();
            if (startResult != IMVDefine.IMV_OK)
                return Failure($"Không bắt đầu lấy ảnh được (mã SDK {startResult}).");
            grabbing = true;

            var frameResult = camera.IMV_GetFrame(ref frame, (uint)_options.GrabTimeoutMilliseconds);
            if (frameResult != IMVDefine.IMV_OK)
                return Failure($"Camera không trả ảnh trong {_options.GrabTimeoutMilliseconds} ms (mã SDK {frameResult}).");
            frameReceived = true;

            var capturesDirectory = Path.Combine(_environment.WebRootPath, "captures");
            Directory.CreateDirectory(capturesDirectory);
            var fileName = $"gige-{DateTime.Now:yyyyMMdd-HHmmssfff}.jpg";
            var absolutePath = Path.Combine(capturesDirectory, fileName);
            var saveResult = SaveJpeg(camera, ref frame, absolutePath);
            if (!saveResult.Ok)
                return saveResult;

            _logger.LogInformation("GigE Vision image captured to {Path}", absolutePath);
            return new CaptureResult(true, $"/captures/{fileName}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "GigE Vision capture failed.");
            return Failure(ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            if (frameReceived)
                Try(() => camera.IMV_ReleaseFrame(ref frame));
            if (grabbing)
                Try(camera.IMV_StopGrabbing);
            if (opened)
                Try(camera.IMV_Close);
            if (handleCreated)
                Try(camera.IMV_DestroyHandle);
        }
    }

    private CaptureResult SaveJpeg(MyCamera camera, ref IMVDefine.IMV_Frame frame, string outputPath)
    {
        var capacity = checked((int)frame.frameInfo.width * (int)frame.frameInfo.height * 4);
        var destination = new byte[Math.Max(capacity, 1024)];
        var pin = GCHandle.Alloc(destination, GCHandleType.Pinned);
        try
        {
            var save = new IMVDefine.IMV_SaveImageParam
            {
                eImageType = IMVDefine.IMV_ESaveFileType.typeSaveJpeg,
                nWidth = frame.frameInfo.width,
                nHeight = frame.frameInfo.height,
                ePixelFormat = frame.frameInfo.pixelFormat,
                pSrcData = frame.pData,
                nSrcDataLen = frame.frameInfo.size,
                eBayerDemosaic = IMVDefine.IMV_EBayerDemosaic.demosaicEdgeSensing,
                nDstBufSize = (uint)destination.Length,
                pDstBuf = pin.AddrOfPinnedObject(),
                nDstDataLen = 0,
                nQuality = 95
            };
            var result = camera.IMV_SaveImage(ref save);
            if (result != IMVDefine.IMV_OK)
                return Failure($"Không lưu được ảnh camera (mã SDK {result}).");

            File.WriteAllBytes(outputPath, destination.AsSpan(0, checked((int)save.nDstDataLen)).ToArray());
            return new CaptureResult(true, null, null);
        }
        finally
        {
            pin.Free();
        }
    }

    private int Enumerate(out IMVDefine.IMV_DeviceList devices)
    {
        devices = new IMVDefine.IMV_DeviceList();
        return MyCamera.IMV_EnumDevices(ref devices, (uint)IMVDefine.IMV_EInterfaceType.interfaceTypeAll);
    }

    private int GetSelectedIndex(IMVDefine.IMV_DeviceList devices)
    {
        if (!string.IsNullOrWhiteSpace(_options.CameraIpAddress))
            return 0;
        if (_options.CameraIndex < 0 || _options.CameraIndex >= devices.nDevNum)
            throw new InvalidOperationException($"CameraIndex {_options.CameraIndex} không tồn tại. SDK chỉ tìm thấy {devices.nDevNum} camera.");
        return _options.CameraIndex;
    }

    private static IMVDefine.IMV_DeviceInfo GetDeviceInfo(IMVDefine.IMV_DeviceList devices, int index)
        => Marshal.PtrToStructure<IMVDefine.IMV_DeviceInfo>(
            devices.pDevInfo + Marshal.SizeOf<IMVDefine.IMV_DeviceInfo>() * index);

    private static string DescribeDevice(IMVDefine.IMV_DeviceInfo device)
    {
        var manufacturer = device.manufactureInfo?.Trim();
        var model = device.modelName?.Trim();
        var serial = device.serialNumber?.Trim();
        return string.Join(" · ", new[] { manufacturer, model, serial }.Where(x => !string.IsNullOrWhiteSpace(x)));
    }

    private void ConfigureNativeRuntime()
    {
        if (string.IsNullOrWhiteSpace(_options.ImvRuntimeDirectory) || !Directory.Exists(_options.ImvRuntimeDirectory))
            throw new DirectoryNotFoundException($"Không tìm thấy MV Viewer Runtime: {_options.ImvRuntimeDirectory}");
        if (!SetDllDirectory(_options.ImvRuntimeDirectory))
            throw new InvalidOperationException($"Không thể nạp DLL runtime camera từ {_options.ImvRuntimeDirectory}.");
    }

    private static CaptureResult Failure(string message) => new(false, null, message);
    private static void Try(Func<int> action) { try { _ = action(); } catch { /* best-effort cleanup */ } }

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool SetDllDirectory(string lpPathName);
}
