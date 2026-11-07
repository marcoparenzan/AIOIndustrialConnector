// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;
using AioIndustrialConnector.Modbus.Simulator;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AioIndustrialConnector.Modbus.Tests;

/// <summary>
/// End to end: sampler factory -> NModbus TCP client -> in-process NModbus simulator over loopback TCP.
/// </summary>
public sealed class SimulatorSamplingTests : IAsyncLifetime
{
    private ModbusSimulatorServer _server = null!;
    private ModbusClientPool _pool = null!;

    public ValueTask InitializeAsync()
    {
        _server = ModbusSimulatorServer.Start(IPAddress.Loopback, 0, 1, 2);
        _pool = new ModbusClientPool(NullLoggerFactory.Instance);
        return ValueTask.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _pool.DisposeAsync();
        await _server.DisposeAsync();
    }

    private Device Device(string? additionalConfiguration = null, int? port = null) => new()
    {
        Endpoints = new DeviceEndpoints
        {
            Inbound = new()
            {
                ["modbus"] = new InboundEndpointSchemaMapValue
                {
                    Address = $"modbus-tcp://127.0.0.1:{port ?? _server.Port}",
                    EndpointType = "Custom.ModbusTcp",
                    AdditionalConfiguration = additionalConfiguration,
                },
            },
        },
    };

    private static Asset Asset(AssetDataset dataset, string? defaults = null) => new()
    {
        DeviceRef = new AssetDeviceRef { DeviceName = "dev", EndpointName = "modbus" },
        Datasets = [dataset],
        DefaultDatasetsConfiguration = defaults,
    };

    private IDatasetSampler CreateSampler(AssetDataset dataset, Device? device = null, string? assetDefaults = null) =>
        new ModbusDatasetSamplerFactory(_pool, NullLoggerFactory.Instance)
            .CreateDatasetSampler("dev", device ?? Device(), "modbus", "asset", Asset(dataset, assetDefaults), dataset, null);

    private static async Task<JsonElement> SampleAsync(IDatasetSampler sampler, AssetDataset dataset)
    {
        byte[] payload = await sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    [Fact]
    public async Task Samples_the_simulated_process_map()
    {
        SimulatedProcess.Update(_server.Store(1), TimeSpan.Zero, cycle: 42);

        AssetDataset dataset = PlanTests.Dataset("""{"samplingIntervalMs": 500}""",
            ("temperature", "hr:0", """{"dataType":"Float32"}"""),
            ("humidity", "hr:2", """{"scale":0.1}"""),
            ("cycleCounter", "hr:3", """{"dataType":"UInt32"}"""),
            ("pressureDelta", "40006", """{"dataType":"Int16"}"""),
            ("running", "hr:6", """{"bit":0}"""),
            ("alarm", "hr:6", """{"bit":1}"""),
            ("serialNumber", "hr:10", """{"dataType":"String","length":8}"""),
            ("totalEnergy", "hr:20", """{"dataType":"Float64"}"""),
            ("flowRate", "ir:0", """{"dataType":"Float32","wordOrder":"LittleEndian"}"""),
            ("tankLevel", "ir:2", """{"scale":0.02442002442,"offset":0}"""),
            ("byteSwapped", "ir:3", """{"byteOrder":"LittleEndian"}"""),
            ("pumpOn", "coil:0", null),
            ("valveOpen", "00002", null),
            ("doorClosed", "di:0", null),
            ("emergencyStop", "10002", null));

        IDatasetSampler sampler = CreateSampler(dataset);
        Assert.Equal(TimeSpan.FromMilliseconds(500), await sampler.GetSamplingIntervalAsync(dataset, TestContext.Current.CancellationToken));

        JsonElement json = await SampleAsync(sampler, dataset);

        Assert.Equal(25.0, json.GetProperty("temperature").GetDouble(), 3);
        Assert.Equal(50.0, json.GetProperty("humidity").GetDouble(), 6);
        Assert.Equal(42u, json.GetProperty("cycleCounter").GetUInt32());
        Assert.Equal(0, json.GetProperty("pressureDelta").GetInt32());
        Assert.True(json.GetProperty("running").GetBoolean());
        Assert.False(json.GetProperty("alarm").GetBoolean());
        Assert.Equal(SimulatedProcess.SerialNumber, json.GetProperty("serialNumber").GetString());
        Assert.Equal(1000.0, json.GetProperty("totalEnergy").GetDouble(), 6);
        Assert.Equal(10.0, json.GetProperty("flowRate").GetDouble(), 3);
        Assert.Equal(50.0, json.GetProperty("tankLevel").GetDouble(), 1);
        Assert.Equal(1234, json.GetProperty("byteSwapped").GetInt32());
        Assert.True(json.GetProperty("pumpOn").GetBoolean());
        Assert.True(json.GetProperty("valveOpen").GetBoolean());
        Assert.True(json.GetProperty("doorClosed").GetBoolean());
        Assert.False(json.GetProperty("emergencyStop").GetBoolean());

        // Payload property order follows the data point order of the dataset.
        Assert.Equal("temperature", json.EnumerateObject().First().Name);
    }

    [Fact]
    public async Task Reads_reflect_changes_in_the_device()
    {
        AssetDataset dataset = PlanTests.Dataset(null, ("setpoint", "hr:100", """{"dataType":"Int32"}"""));
        IDatasetSampler sampler = CreateSampler(dataset);

        _server.Store(1).WriteHolding(100, RegisterWriter.Int32(-7));
        Assert.Equal(-7, (await SampleAsync(sampler, dataset)).GetProperty("setpoint").GetInt32());

        _server.Store(1).WriteHolding(100, RegisterWriter.Int32(123456));
        Assert.Equal(123456, (await SampleAsync(sampler, dataset)).GetProperty("setpoint").GetInt32());
    }

    [Fact]
    public async Task Unit_id_comes_from_endpoint_configuration()
    {
        _server.Store(1).WriteHolding(200, 111);
        _server.Store(2).WriteHolding(200, 222);

        AssetDataset dataset = PlanTests.Dataset(null, ("v", "hr:200", null));
        JsonElement json = await SampleAsync(CreateSampler(dataset, Device("""{"unitId": 2}""")), dataset);
        Assert.Equal(222, json.GetProperty("v").GetInt32());

        AssetDataset perPoint = PlanTests.Dataset(null, ("u1", "hr:200", """{"unitId":1}"""), ("u2", "hr:200", """{"unitId":2}"""));
        json = await SampleAsync(CreateSampler(perPoint, Device("""{"unitId": 2}""")), perPoint);
        Assert.Equal(111, json.GetProperty("u1").GetInt32());
        Assert.Equal(222, json.GetProperty("u2").GetInt32());
    }

    [Fact]
    public async Task Datasets_of_the_same_device_share_one_client()
    {
        AssetDataset a = PlanTests.Dataset(null, ("a", "hr:0", null));
        AssetDataset b = PlanTests.Dataset(null, ("b", "hr:1", null));
        var sa = (PolledDatasetSampler)CreateSampler(a);
        var sb = (PolledDatasetSampler)CreateSampler(b);

        Assert.Same(_pool.GetClient(new TcpEndpoint("127.0.0.1", _server.Port), ModbusSettings.Empty), _pool.GetClient(new TcpEndpoint("127.0.0.1", _server.Port), ModbusSettings.Empty));
        await SampleAsync(sa, a);
        await SampleAsync(sb, b);
    }

    [Fact]
    public async Task Invalid_configuration_yields_a_sampler_that_reports_the_error()
    {
        AssetDataset dataset = PlanTests.Dataset(null, ("bad", "not-an-address", null));
        IDatasetSampler sampler = CreateSampler(dataset);

        var ex = await Assert.ThrowsAsync<ConfigurationException>(() => sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken));
        Assert.Contains("not-an-address", ex.Message);
    }

    [Fact]
    public async Task Unreachable_device_fails_with_communication_error()
    {
        // Grab a free port and close it again so nothing listens there.
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        AssetDataset dataset = PlanTests.Dataset(null, ("v", "hr:0", null));
        IDatasetSampler sampler = CreateSampler(dataset, Device("""{"connectTimeoutMs": 1000, "timeoutMs": 500}""", closedPort));

        await Assert.ThrowsAsync<ModbusCommunicationException>(() => sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Recovers_after_the_device_restarts()
    {
        int port;
        await using (var first = ModbusSimulatorServer.Start(IPAddress.Loopback, 0))
        {
            port = first.Port;
            first.Store().WriteHolding(0, 1);
            AssetDataset dataset = PlanTests.Dataset(null, ("v", "hr:0", null));
            IDatasetSampler sampler = CreateSampler(dataset, Device("""{"timeoutMs": 1000}""", port));
            Assert.Equal(1, (await SampleAsync(sampler, dataset)).GetProperty("v").GetInt32());

            await first.DisposeAsync();

            await using var second = ModbusSimulatorServer.Start(IPAddress.Loopback, port);
            second.Store().WriteHolding(0, 2);

            // The stale connection fails; the sampler retries once on a fresh connection.
            Assert.Equal(2, (await SampleAsync(sampler, dataset)).GetProperty("v").GetInt32());
        }
    }
}
