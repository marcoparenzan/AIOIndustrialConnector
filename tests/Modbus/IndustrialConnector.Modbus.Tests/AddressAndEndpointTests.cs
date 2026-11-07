// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;

namespace AioIndustrialConnector.Modbus.Tests;

public class AddressAndEndpointTests
{
    [Theory]
    [InlineData("hr:0", ModbusTable.HoldingRegister, 0)]
    [InlineData("HR:10", ModbusTable.HoldingRegister, 10)]
    [InlineData("holding:0x10", ModbusTable.HoldingRegister, 16)]
    [InlineData("4x:5", ModbusTable.HoldingRegister, 5)]
    [InlineData("ir:3", ModbusTable.InputRegister, 3)]
    [InlineData("input:65535", ModbusTable.InputRegister, 65535)]
    [InlineData("coil:7", ModbusTable.Coil, 7)]
    [InlineData("di:2", ModbusTable.DiscreteInput, 2)]
    [InlineData("40001", ModbusTable.HoldingRegister, 0)]
    [InlineData("49999", ModbusTable.HoldingRegister, 9998)]
    [InlineData("400001", ModbusTable.HoldingRegister, 0)]
    [InlineData("465536", ModbusTable.HoldingRegister, 65535)]
    [InlineData("30010", ModbusTable.InputRegister, 9)]
    [InlineData("10001", ModbusTable.DiscreteInput, 0)]
    [InlineData("00005", ModbusTable.Coil, 4)]
    public void Parses_supported_address_forms(string text, ModbusTable table, int offset)
    {
        Assert.Equal(new ModbusAddress(table, (ushort)offset), ModbusAddress.Parse(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("hr:")]
    [InlineData("hr:-1")]
    [InlineData("hr:65536")]
    [InlineData("xx:1")]
    [InlineData("40000")]
    [InlineData("20001")]
    [InlineData("1234")]
    [InlineData("/api/sensor")]
    public void Rejects_invalid_addresses(string text)
    {
        Assert.False(ModbusAddress.TryParse(text, out _, out string? error));
        Assert.False(string.IsNullOrEmpty(error));
    }

    [Theory]
    [InlineData("modbus-tcp://plc1:502", "plc1", 502)]
    [InlineData("modbus-tcp://plc1", "plc1", 502)]
    [InlineData("tcp://10.0.0.5:1502", "10.0.0.5", 1502)]
    [InlineData("modbus://sim.azure-iot-operations.svc.cluster.local:502", "sim.azure-iot-operations.svc.cluster.local", 502)]
    [InlineData("192.168.1.10:5020", "192.168.1.10", 5020)]
    [InlineData("plc1", "plc1", 502)]
    [InlineData("[fd00::1]:503", "fd00::1", 503)]
    public void Parses_endpoint_addresses(string address, string host, int port)
    {
        Assert.Equal(new TcpEndpoint(host, port), ModbusProtocol.ParseEndpoint(address));
    }

    [Theory]
    [InlineData("")]
    [InlineData("http://plc1:80")]
    [InlineData("opc.tcp://server:4840")]
    public void Rejects_non_modbus_endpoints(string address)
    {
        Assert.Throws<ConfigurationException>(() => ModbusProtocol.ParseEndpoint(address));
    }
}
