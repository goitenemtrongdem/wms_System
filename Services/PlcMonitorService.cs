using Microsoft.Extensions.Options;

namespace AsrsWarehouse.Services;

public class PlcMonitorService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly PlcModbusService _plc;
    private readonly IQrCameraService _camera;
    private readonly HardwareStatusService _hardwareStatus;
    private readonly CameraOptions _cameraOptions;
    private readonly ILogger<PlcMonitorService> _logger;
    private bool _wasCameraTriggerOn;
    private Task? _cameraTask;

    public PlcMonitorService(
        IServiceScopeFactory scopeFactory,
        PlcModbusService plc,
        IQrCameraService camera,
        HardwareStatusService hardwareStatus,
        IOptions<CameraOptions> cameraOptions,
        ILogger<PlcMonitorService> logger)
    {
        _scopeFactory = scopeFactory;
        _plc = plc;
        _camera = camera;
        _hardwareStatus = hardwareStatus;
        _cameraOptions = cameraOptions.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _hardwareStatus.SetCameraProbe(await _camera.ProbeAsync(stoppingToken));
        var nextCameraProbe = DateTimeOffset.UtcNow.AddSeconds(10);
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var inputs = _plc.ReadInputs();
                _hardwareStatus.SetPlcInput(inputs);
                await UpdateSlotAsync("R01", inputs.R01Occupied, stoppingToken);
                await UpdateSlotAsync("R02", inputs.R02Occupied, stoppingToken);

                // A rising edge prevents one long M202 signal from creating many scans.
                if (inputs.CameraTrigger && !_wasCameraTriggerOn && (_cameraTask is null || _cameraTask.IsCompleted))
                {
                    _logger.LogInformation("M202 became 1; GigE capture will start after the configured delay.");
                    _hardwareStatus.SetWaitingForCapture(_cameraOptions.TriggerDelaySeconds);
                    _cameraTask = ProcessCameraTriggerAsync(stoppingToken);
                }
                _wasCameraTriggerOn = inputs.CameraTrigger;
            }
            catch (Exception ex)
            {
                _hardwareStatus.SetPlcFailure(ex);
                _logger.LogWarning(ex, "PLC Modbus poll failed.");
            }

            if (DateTimeOffset.UtcNow >= nextCameraProbe)
            {
                _hardwareStatus.SetCameraProbe(await _camera.ProbeAsync(stoppingToken));
                nextCameraProbe = DateTimeOffset.UtcNow.AddSeconds(10);
            }

            await Task.Delay(500, stoppingToken);
        }
    }

    private async Task UpdateSlotAsync(string slotName, bool occupied, CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var workflow = scope.ServiceProvider.GetRequiredService<WarehouseWorkflowService>();
        await workflow.UpdateSlotSensorAsync(slotName, occupied, cancellationToken);
    }

    private async Task ProcessCameraTriggerAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var scanner = scope.ServiceProvider.GetRequiredService<InboundScanService>();
            var result = await scanner.CaptureAfterPlcTriggerAsync(cancellationToken);
            _hardwareStatus.SetCaptureResult(new CaptureResult(result.Ok, null, result.Ok ? null : result.Message));
            if (result.Ok)
                _logger.LogInformation("GigE/QR inbound scan complete: {Message}", result.Message);
            else
                _logger.LogWarning("GigE/QR inbound scan did not complete: {Message}", result.Message);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Normal shutdown.
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled error in the M202 camera workflow.");
        }
    }
}
