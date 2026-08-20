using System.Net.Sockets;
using NModbus;

namespace AsrsWarehouse.Services;

public class PlcModbusService
{
    private const string PlcIp = "192.168.3.250";
    private const int PlcPort = 502;

    // Theo mapping GX Works3:
    // M0   -> Coil 8192
    // M200 -> Coil 8392
    // M201 -> Coil 8393
    private const ushort M200Address = 8392;

    public bool[] ReadRackSensors()
    {
        using var client = new TcpClient();

        client.Connect(PlcIp, PlcPort);

        var factory = new ModbusFactory();

        var master = factory.CreateMaster(client);

        byte slaveId = 1;

        // đọc liên tiếp M200 và M201
        bool[] values = master.ReadCoils(
            slaveId,
            M200Address,
            2
        );

        return values;
    }
}