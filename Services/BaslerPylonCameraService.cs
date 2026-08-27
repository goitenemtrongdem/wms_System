using System.Reflection;
using Microsoft.Extensions.Options;

namespace AsrsWarehouse.Services;

public interface IBaslerCameraService
{
    Task<CaptureResult> CaptureAsync(CancellationToken cancellationToken = default);
}

/// <summary>
/// Captures one frame through the official Basler pylon .NET runtime. Reflection
/// makes the WMS buildable on workstations which do not have the camera SDK;
/// install pylon Runtime on the camera PC to enable this service.
/// </summary>
public class BaslerPylonCameraService : IBaslerCameraService
{
    private readonly IWebHostEnvironment _environment;
    private readonly CameraOptions _options;
    private readonly ILogger<BaslerPylonCameraService> _logger;

    public BaslerPylonCameraService(
        IWebHostEnvironment environment,
        IOptions<CameraOptions> options,
        ILogger<BaslerPylonCameraService> logger)
    {
        _environment = environment;
        _options = options.Value;
        _logger = logger;
    }

    public Task<CaptureResult> CaptureAsync(CancellationToken cancellationToken = default)
        => Task.Run(() => Capture(), cancellationToken);

    private CaptureResult Capture()
    {
        object? camera = null;
        object? grabResult = null;
        Type? pylonType = null;

        try
        {
            var assemblyPath = FindPylonAssembly();
            if (assemblyPath is null)
                return new CaptureResult(false, null, "Không tìm thấy Basler.Pylon.dll. Hãy cài Basler pylon Runtime hoặc đặt Camera:PylonAssemblyPath.");

            var assembly = Assembly.LoadFrom(assemblyPath);
            pylonType = assembly.GetType("Basler.Pylon.Pylon");
            pylonType?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);

            var cameraType = assembly.GetType("Basler.Pylon.Camera")
                ?? throw new InvalidOperationException("Không tìm thấy lớp Basler.Pylon.Camera.");

            camera = Activator.CreateInstance(cameraType)
                ?? throw new InvalidOperationException("Không thể khởi tạo camera Basler.");
            Invoke(camera, "Open");

            var streamGrabber = cameraType.GetProperty("StreamGrabber")?.GetValue(camera)
                ?? throw new InvalidOperationException("Không lấy được StreamGrabber của camera.");

            Invoke(streamGrabber, "Start");

            var timeoutHandlingType = assembly.GetType("Basler.Pylon.TimeoutHandling")
                ?? throw new InvalidOperationException("Không tìm thấy Basler.Pylon.TimeoutHandling.");
            var throwException = Enum.Parse(timeoutHandlingType, "ThrowException");
            grabResult = Invoke(streamGrabber, "RetrieveResult", _options.GrabTimeoutMilliseconds, throwException)
                ?? throw new InvalidOperationException("Camera không trả về ảnh.");

            var succeeded = grabResult.GetType().GetProperty("GrabSucceeded")?.GetValue(grabResult) as bool?;
            if (succeeded != true)
                return new CaptureResult(false, null, "Camera chụp ảnh không thành công.");

            var capturesDirectory = Path.Combine(_environment.WebRootPath, "captures");
            Directory.CreateDirectory(capturesDirectory);
            var fileName = $"basler-{DateTime.Now:yyyyMMdd-HHmmss}.png";
            var absolutePath = Path.Combine(capturesDirectory, fileName);

            var imageFileFormatType = assembly.GetType("Basler.Pylon.ImageFileFormat")
                ?? throw new InvalidOperationException("Không tìm thấy Basler.Pylon.ImageFileFormat.");
            var png = Enum.Parse(imageFileFormatType, "Png");
            Invoke(grabResult, "Save", png, absolutePath);

            _logger.LogInformation("Basler image captured to {Path}", absolutePath);
            return new CaptureResult(true, $"/captures/{fileName}", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Basler capture failed.");
            return new CaptureResult(false, null, ex.InnerException?.Message ?? ex.Message);
        }
        finally
        {
            DisposeQuietly(grabResult);
            if (camera is not null)
            {
                TryInvoke(camera, "Close");
                DisposeQuietly(camera);
            }
            pylonType?.GetMethod("Terminate", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
        }
    }

    private string? FindPylonAssembly()
    {
        if (!string.IsNullOrWhiteSpace(_options.PylonAssemblyPath) && File.Exists(_options.PylonAssemblyPath))
            return _options.PylonAssemblyPath;

        var roots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Basler"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Basler")
        };

        return roots
            .Where(Directory.Exists)
            .SelectMany(root => Directory.EnumerateFiles(root, "Basler.Pylon.dll", SearchOption.AllDirectories))
            .FirstOrDefault();
    }

    private static object? Invoke(object target, string name, params object[] arguments)
    {
        var method = target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
            .FirstOrDefault(x => x.Name == name && x.GetParameters().Length == arguments.Length)
            ?? throw new MissingMethodException(target.GetType().FullName, name);
        return method.Invoke(target, arguments);
    }

    private static void TryInvoke(object target, string name)
    {
        try { Invoke(target, name); } catch { /* Best-effort cleanup. */ }
    }

    private static void DisposeQuietly(object? value)
    {
        try { (value as IDisposable)?.Dispose(); } catch { /* Best-effort cleanup. */ }
    }
}
