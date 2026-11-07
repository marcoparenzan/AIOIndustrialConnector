// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text.Json;
using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Schema;
using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;
using Microsoft.Extensions.Logging.Abstractions;

namespace AioIndustrialConnector.Modbus.Tests;

/// <summary>
/// Sampler behaviour that the simulator cannot easily produce (exception responses), using a scripted fake client.
/// </summary>
public class SamplerBehaviourTests
{
    private sealed class ScriptedClient : IModbusClient
    {
        public Func<ModbusTable, ushort, ushort[]>? Registers { get; init; }
        public Func<ModbusTable, ushort, bool[]>? Bits { get; init; }
        public int Calls { get; private set; }

        public Task<ushort[]> ReadRegistersAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Registers!(table, start));
        }

        public Task<bool[]> ReadBitsAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Bits!(table, start));
        }
    }

    private static ModbusDatasetPlan Plan(params (string, string, string?)[] points) =>
        ModbusDatasetPlan.Build(PlanTests.Dataset(null, points), ModbusSettings.Empty, ModbusSettings.Empty);

    [Fact]
    public async Task Rejected_request_publishes_nulls_and_keeps_other_points()
    {
        ModbusDatasetPlan plan = Plan(("ok", "hr:0", null), ("missing", "hr:50", null));
        var client = new ScriptedClient
        {
            Registers = (_, start) => start == 50 ? throw new ModbusDeviceException(2, "illegal data address") : [7],
        };

        var sampler = new PolledDatasetSampler(plan, new ModbusBlockReader(client), "asset", NullLogger.Instance);
        JsonElement json = JsonDocument.Parse(await sampler.SampleDatasetAsync(PlanTests.Dataset(null), TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(7, json.GetProperty("ok").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("missing").ValueKind);
    }

    [Fact]
    public async Task All_requests_rejected_throws()
    {
        ModbusDatasetPlan plan = Plan(("a", "hr:0", null));
        var client = new ScriptedClient { Registers = (_, _) => throw new ModbusDeviceException(2, "illegal data address") };

        var sampler = new PolledDatasetSampler(plan, new ModbusBlockReader(client), "asset", NullLogger.Instance);
        await Assert.ThrowsAsync<InvalidOperationException>(() => sampler.SampleDatasetAsync(PlanTests.Dataset(null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Communication_failure_is_retried_once()
    {
        ModbusDatasetPlan plan = Plan(("a", "hr:0", null));
        int attempt = 0;
        var client = new ScriptedClient { Registers = (_, _) => ++attempt == 1 ? throw new ModbusCommunicationException("connection reset") : [5] };

        var sampler = new PolledDatasetSampler(plan, new ModbusBlockReader(client), "asset", NullLogger.Instance);
        JsonElement json = JsonDocument.Parse(await sampler.SampleDatasetAsync(PlanTests.Dataset(null), TestContext.Current.CancellationToken)).RootElement;

        Assert.Equal(5, json.GetProperty("a").GetInt32());
        Assert.Equal(2, client.Calls);
    }

    [Fact]
    public async Task Short_response_is_a_communication_error()
    {
        ModbusDatasetPlan plan = Plan(("a", "hr:0", """{"dataType":"Float32"}"""));
        var client = new ScriptedClient { Registers = (_, _) => [1] };

        var sampler = new PolledDatasetSampler(plan, new ModbusBlockReader(client), "asset", NullLogger.Instance);
        await Assert.ThrowsAsync<ModbusCommunicationException>(() => sampler.SampleDatasetAsync(PlanTests.Dataset(null), TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Message_schema_matches_the_data_types()
    {
        ModbusDatasetPlan plan = Plan(
            ("f", "hr:0", """{"dataType":"Float32"}"""),
            ("i", "hr:2", null),
            ("s", "hr:3", """{"scale":0.1}"""),
            ("b", "coil:0", null),
            ("t", "hr:10", """{"dataType":"String","length":2}"""));

        JsonElement props = JsonDocument.Parse(JsonSchemaBuilder.Build(plan)).RootElement.GetProperty("properties");

        Assert.Equal("number", props.GetProperty("f").GetProperty("type")[0].GetString());
        Assert.Equal("integer", props.GetProperty("i").GetProperty("type")[0].GetString());
        Assert.Equal("number", props.GetProperty("s").GetProperty("type")[0].GetString());
        Assert.Equal("boolean", props.GetProperty("b").GetProperty("type")[0].GetString());
        Assert.Equal("string", props.GetProperty("t").GetProperty("type")[0].GetString());
        Assert.Equal("null", props.GetProperty("f").GetProperty("type")[1].GetString());
    }
}
