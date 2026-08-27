using System.Net.Sockets;
using Microsoft.Extensions.Options;
using NModbus;

namespace AsrsWarehouse.Services;

/// <summary>
/// Keeps all Mitsubishi/Modbus addresses in appsettings. M202 is read as the
/// camera trigger; writes are deliberately disabled until their PLC coils are
/// explicitly configured.
/// </summary>
public class PlcModbusService
{
    private readonly PlcOptions _options;
    private readonly ILogger<PlcModbusService> _logger;
    private readonly SemaphoreSlim _infeedMotionTestCommandGate = new(1, 1);

    public PlcModbusService(IOptions<PlcOptions> options, ILogger<PlcModbusService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public PlcInputState ReadInputs()
    {
        using var client = new TcpClient();
        client.Connect(_options.IpAddress, _options.Port);

        var master = new ModbusFactory().CreateMaster(client);
        // Normal mapping is M200/M201/M202, so read all three in one request.
        // Keeping CameraTriggerCoil independent also supports a changed PLC map.
        var contiguousCameraCoil = (ushort)(_options.RackSensorStartCoil + 2);
        var allValues = master.ReadCoils(
            _options.SlaveId,
            _options.RackSensorStartCoil,
            _options.CameraTriggerCoil == contiguousCameraCoil ? (ushort)3 : (ushort)2);
        var cameraTrigger = _options.CameraTriggerCoil == contiguousCameraCoil
            ? allValues[2]
            : master.ReadCoils(_options.SlaveId, _options.CameraTriggerCoil, 1)[0];

        return new PlcInputState(
            R01Occupied: allValues.Length > 0 && allValues[0],
            R02Occupied: allValues.Length > 1 && allValues[1],
            CameraTrigger: cameraTrigger);
    }

    public async Task<string?> PulseInboundCommandAsync(CancellationToken cancellationToken = default)
        => await PulseAsync(_options.InboundRequestCoil, "nhập hàng", cancellationToken);

    public async Task<string?> PulseOutboundCommandAsync(CancellationToken cancellationToken = default)
        => await PulseAsync(_options.OutboundRequestCoil, "xuất kho", cancellationToken);

    /// <summary>
    /// Sends only a one-shot Modbus request. Motion distance and speed are deliberately
    /// fixed in PLC ladder logic so a web client cannot alter a physical move.
    /// </summary>
    public async Task<OperationResult> StartInfeedMotionTestAsync(CancellationToken cancellationToken = default)
    {
        if (!await _infeedMotionTestCommandGate.WaitAsync(0, cancellationToken))
            return new OperationResult(false, "Lệnh test PLC đang được gửi; không gửi lặp lại.");

        try
        {
            var plcMessage = await PulseAsync(
                _options.InfeedMotionTestRequestCoil,
                "test nhập hàng 50.000 xung",
                cancellationToken);

            return plcMessage is null
                ? new OperationResult(true, "Đã gửi lệnh test tới PLC. PLC sẽ chạy 50.000 xung ở tốc độ đặt trong ladder.")
                : new OperationResult(false, "PLC chưa nhận được lệnh test.", plcMessage);
        }
        finally
        {
            _infeedMotionTestCommandGate.Release();
        }
    }

    private async Task<string?> PulseAsync(ushort? coil, string operation, CancellationToken cancellationToken)
    {
        if (!coil.HasValue)
        {
            const string message = "Chưa gửi PLC: cần cấu hình coil lệnh trong appsettings.json.";
            _logger.LogWarning("{Operation} not sent because no output coil is configured.", operation);
            return message;
        }

        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_options.IpAddress, _options.Port, cancellationToken);
            var master = new ModbusFactory().CreateMaster(client);
            var coilIsOn = false;
            string? resetError = null;

            try
            {
                master.WriteSingleCoil(_options.SlaveId, coil.Value, true);
                coilIsOn = true;
                // Once a physical command has been raised, always complete its pulse.
                // A disconnected browser must never leave a PLC request coil ON.
                await Task.Delay(_options.CommandPulseMilliseconds, CancellationToken.None);
            }
            finally
            {
                if (coilIsOn)
                {
                    try
                    {
                        master.WriteSingleCoil(_options.SlaveId, coil.Value, false);
                    }
                    catch (Exception resetException)
                    {
                        _logger.LogError(resetException, "Could not reset PLC {Operation} command coil.", operation);
                        resetError = $"PLC command coil could not be reset: {resetException.Message}";
                    }
                }
            }

            return resetError;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Could not send PLC {Operation} command.", operation);
            return $"Không gửi được PLC: {ex.Message}";
        }
    }
}
