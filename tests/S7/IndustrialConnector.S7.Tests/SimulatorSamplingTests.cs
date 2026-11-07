// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Net;
using System.Text.Json;
using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.S7.Simulator;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AioIndustrialConnector.S7.Tests;

/// <summary>
/// End to end: sampler factory -> S7netplus client -> in-process S7 simulator over loopback TCP.
/// The simulator is an independent implementation of ISO-on-TCP / S7comm, so this exercises the real wire protocol.
/// </summary>
public sealed class SimulatorSamplingTests : IAsyncLifetime
{
    private S7SimulatorServer _server = null!;
    private S7ClientPool _pool = null!;

    public ValueTask InitializeAsync()
    {
        _server = S7SimulatorServer.Start(IPAddress.Loopback, 0, SimulatedProcess.CreateMemory());
        _pool = new S7ClientPool(NullLoggerFactory.Instance);
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
                ["plc"] = new InboundEndpointSchemaMapValue
                {
                    Address = $"s7://127.0.0.1:{port ?? _server.Port}",
                    EndpointType = "Custom.SiemensS7",
                    AdditionalConfiguration = additionalConfiguration,
                },
            },
        },
    };

    private static Asset Asset(AssetDataset dataset) => new()
    {
        DeviceRef = new AssetDeviceRef { DeviceName = "dev", EndpointName = "plc" },
        Datasets = [dataset],
    };

    private IDatasetSampler CreateSampler(AssetDataset dataset, Device? device = null) =>
        new S7DatasetSamplerFactory(_pool, NullLoggerFactory.Instance)
            .CreateDatasetSampler("dev", device ?? Device(), "plc", "asset", Asset(dataset), dataset, null);

    private static async Task<JsonElement> SampleAsync(IDatasetSampler sampler, AssetDataset dataset)
    {
        byte[] payload = await sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken);
        return JsonDocument.Parse(payload).RootElement.Clone();
    }

    [Fact]
    public async Task Samples_the_simulated_process_map()
    {
        _server.Update(m => SimulatedProcess.Update(m, TimeSpan.Zero, cycle: 42));

        AssetDataset dataset = PlanTests.Dataset("""{"samplingIntervalMs": 500}""",
            ("temperature", "DB1.DBD0", """{"dataType":"Real"}"""),
            ("humidity", "DB1.DBW4", """{"dataType":"Int","scale":0.1}"""),
            ("cycleCounter", "DB1.DBD6", null),
            ("pressureDelta", "DB1.DBW10", """{"dataType":"Int16"}"""),
            ("running", "DB1.DBX12.0", null),
            ("alarm", "DB1.DBX12.1", null),
            ("serialNumber", "DB1.DBB20", """{"dataType":"String","length":16}"""),
            ("totalEnergy", "DB1.DBB40", """{"dataType":"LReal"}"""),
            ("tankLevel", "MW0", """{"scale":0.02442002442}"""),
            ("doorClosed", "I0.0", null),
            ("emergencyStop", "E0.1", null),
            ("pumpOn", "Q0.0", null),
            ("valveOpen", "A0.1", null));

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
        Assert.Equal(50.0, json.GetProperty("tankLevel").GetDouble(), 1);
        Assert.True(json.GetProperty("doorClosed").GetBoolean());
        Assert.False(json.GetProperty("emergencyStop").GetBoolean());
        Assert.True(json.GetProperty("pumpOn").GetBoolean());
        Assert.True(json.GetProperty("valveOpen").GetBoolean());
    }

    [Fact]
    public async Task Datasets_of_the_same_plc_share_one_connection()
    {
        AssetDataset a = PlanTests.Dataset(null, ("a", "DB1.DBW4", null));
        AssetDataset b = PlanTests.Dataset(null, ("b", "DB1.DBW10", null));
        IDatasetSampler sa = CreateSampler(a);
        IDatasetSampler sb = CreateSampler(b);

        await SampleAsync(sa, a);
        await SampleAsync(sb, b);
        await SampleAsync(sa, a);

        Assert.Equal(1, _server.ConnectionsAccepted);
    }

    [Fact]
    public async Task Reads_larger_than_one_pdu_are_split_and_reassembled()
    {
        _server.Update(m =>
        {
            m.CreateDataBlock(2, 1000);
            m.SetUInt16(S7Memory.DataBlock, 2, 0, 111);
            m.SetUInt16(S7Memory.DataBlock, 2, 598, 222);
        });

        // One request of 600 bytes (maxGap bridges the unused bytes), well above the 240-byte PDU of the simulator.
        AssetDataset dataset = PlanTests.Dataset("""{"maxBytesPerRead": 1000, "maxGap": 1000}""",
            ("first", "DB2.DBW0", null), ("last", "DB2.DBW598", null));

        JsonElement json = await SampleAsync(CreateSampler(dataset), dataset);

        Assert.Equal(111, json.GetProperty("first").GetInt32());
        Assert.Equal(222, json.GetProperty("last").GetInt32());
    }

    [Fact]
    public async Task Missing_data_block_is_published_as_null_and_other_points_survive()
    {
        AssetDataset dataset = PlanTests.Dataset(null, ("ok", "DB1.DBW4", null), ("missing", "DB99.DBW0", null));

        JsonElement json = await SampleAsync(CreateSampler(dataset), dataset);

        Assert.Equal(JsonValueKind.Number, json.GetProperty("ok").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task All_requests_rejected_throws()
    {
        AssetDataset dataset = PlanTests.Dataset(null, ("missing", "DB99.DBW0", null));
        IDatasetSampler sampler = CreateSampler(dataset);

        await Assert.ThrowsAsync<InvalidOperationException>(() => sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken));
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
    public async Task Unreachable_plc_fails_with_communication_error()
    {
        var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        int closedPort = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();

        AssetDataset dataset = PlanTests.Dataset(null, ("v", "DB1.DBW4", null));
        IDatasetSampler sampler = CreateSampler(dataset, Device("""{"connectTimeoutMs": 1000, "timeoutMs": 500}""", closedPort));

        await Assert.ThrowsAsync<S7CommunicationException>(() => sampler.SampleDatasetAsync(dataset, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Recovers_after_the_plc_restarts()
    {
        int port;
        await using (var first = S7SimulatorServer.Start(IPAddress.Loopback, 0, new S7Memory().CreateDataBlock(1, 16)))
        {
            port = first.Port;
            first.Update(m => m.SetUInt16(S7Memory.DataBlock, 1, 0, 1));
            AssetDataset dataset = PlanTests.Dataset(null, ("v", "DB1.DBW0", null));
            IDatasetSampler sampler = CreateSampler(dataset, Device("""{"timeoutMs": 1000}""", port));
            Assert.Equal(1, (await SampleAsync(sampler, dataset)).GetProperty("v").GetInt32());

            await first.DisposeAsync();

            await using var second = S7SimulatorServer.Start(IPAddress.Loopback, port, new S7Memory().CreateDataBlock(1, 16));
            second.Update(m => m.SetUInt16(S7Memory.DataBlock, 1, 0, 2));

            // The stale connection fails; the sampler retries once on a fresh connection.
            Assert.Equal(2, (await SampleAsync(sampler, dataset)).GetProperty("v").GetInt32());
        }
    }
}
