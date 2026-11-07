// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;

namespace AioIndustrialConnector.S7.Tests;

public class AddressAndEndpointTests
{
    [Theory]
    [InlineData("DB1.DBX0.3", S7Area.DataBlock, 1, 0, 3, S7Width.Bit)]
    [InlineData("db10.dbb4", S7Area.DataBlock, 10, 4, 0, S7Width.Byte)]
    [InlineData("DB1.DBW2", S7Area.DataBlock, 1, 2, 0, S7Width.Word)]
    [InlineData(" DB200.DBD8 ", S7Area.DataBlock, 200, 8, 0, S7Width.DWord)]
    [InlineData("M0.1", S7Area.Memory, 0, 0, 1, S7Width.Bit)]
    [InlineData("MB5", S7Area.Memory, 0, 5, 0, S7Width.Byte)]
    [InlineData("MW10", S7Area.Memory, 0, 10, 0, S7Width.Word)]
    [InlineData("MD20", S7Area.Memory, 0, 20, 0, S7Width.DWord)]
    [InlineData("I1.7", S7Area.Input, 0, 1, 7, S7Width.Bit)]
    [InlineData("E1.7", S7Area.Input, 0, 1, 7, S7Width.Bit)]
    [InlineData("IW2", S7Area.Input, 0, 2, 0, S7Width.Word)]
    [InlineData("Q0.0", S7Area.Output, 0, 0, 0, S7Width.Bit)]
    [InlineData("A0.0", S7Area.Output, 0, 0, 0, S7Width.Bit)]
    [InlineData("QD4", S7Area.Output, 0, 4, 0, S7Width.DWord)]
    public void Parses_siemens_notation(string text, S7Area area, int db, int byteOffset, int bit, S7Width width)
    {
        S7Address a = S7Address.Parse(text);
        Assert.Equal(new S7Address(area, db, byteOffset, bit, width), a);
    }

    [Theory]
    [InlineData("")]
    [InlineData("hr:0")]
    [InlineData("DB0.DBW0")]
    [InlineData("DB1.DBX0")]
    [InlineData("DB1.DBX0.8")]
    [InlineData("DB1.DBW0.1")]
    [InlineData("DB1")]
    [InlineData("M0")]
    [InlineData("MW")]
    [InlineData("T1")]
    public void Rejects_invalid_addresses(string text)
    {
        Assert.False(S7Address.TryParse(text, out _, out string? error));
        Assert.NotNull(error);
    }

    [Theory]
    [InlineData("DB1.DBX0.3")]
    [InlineData("DB12.DBW6")]
    [InlineData("MD20")]
    [InlineData("I1.7")]
    public void Round_trips_through_text(string text) => Assert.Equal(text, S7Address.Parse(text).ToString());

    [Theory]
    [InlineData("s7://plc.local:1102", "plc.local", 1102)]
    [InlineData("s7://plc.local", "plc.local", 102)]
    [InlineData("s7-tcp://10.0.0.5", "10.0.0.5", 102)]
    [InlineData("tcp://10.0.0.5:102", "10.0.0.5", 102)]
    [InlineData("10.0.0.5:103", "10.0.0.5", 103)]
    [InlineData("plc", "plc", 102)]
    [InlineData("s7://[fd00::1]:102", "fd00::1", 102)]
    public void Parses_endpoints(string address, string host, int port) =>
        Assert.Equal(new TcpEndpoint(host, port), S7Protocol.ParseEndpoint(address));

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("modbus-tcp://host:502")]
    [InlineData("http://host")]
    public void Rejects_invalid_endpoints(string? address) =>
        Assert.Throws<ConfigurationException>(() => S7Protocol.ParseEndpoint(address));
}
