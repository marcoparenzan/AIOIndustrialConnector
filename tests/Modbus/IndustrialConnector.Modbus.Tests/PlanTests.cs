// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;

namespace AioIndustrialConnector.Modbus.Tests;

public class PlanTests
{
    internal static AssetDataset Dataset(string? datasetConfiguration, params (string Name, string Source, string? Config)[] points) => new()
    {
        Name = "ds",
        DatasetConfiguration = datasetConfiguration,
        DataPoints = points.Select(p => new AssetDatasetDataPoint { Name = p.Name, DataSource = p.Source, DataPointConfiguration = p.Config }).ToList(),
    };

    private static ModbusDatasetPlan Build(AssetDataset dataset, string? assetDefaults = null, string? endpoint = null) =>
        ModbusDatasetPlan.Build(dataset, ModbusSettings.Parse(assetDefaults, "asset"), ModbusSettings.Parse(endpoint, "endpoint"));

    [Fact]
    public void Defaults_are_uint16_for_registers_and_boolean_for_bits_unit_1_and_1s()
    {
        ModbusDatasetPlan plan = Build(Dataset(null, ("a", "hr:0", null), ("b", "coil:0", null)));

        Assert.Equal(TimeSpan.FromSeconds(1), plan.SamplingInterval);
        Assert.Equal(DataType.UInt16, plan.Points.Single(p => p.Name == "a").DataType);
        Assert.Equal(DataType.Boolean, plan.Points.Single(p => p.Name == "b").DataType);
        Assert.All(plan.Points, p => Assert.Equal(1, p.UnitId));
    }

    [Fact]
    public void Point_overrides_dataset_overrides_asset_overrides_endpoint()
    {
        ModbusDatasetPlan plan = Build(
            Dataset("""{"samplingIntervalMs": 250, "wordOrder": "LittleEndian"}""",
                ("a", "hr:0", """{"dataType":"Float32"}"""),
                ("b", "hr:2", """{"dataType":"Float32","wordOrder":"BigEndian","unitId":7}""")),
            assetDefaults: """{"unitId": 3, "samplingIntervalMs": 5000}""",
            endpoint: """{"unitId": 2, "byteOrder": "LittleEndian"}""");

        Assert.Equal(TimeSpan.FromMilliseconds(250), plan.SamplingInterval);
        ModbusPoint a = plan.Points.Single(p => p.Name == "a");
        ModbusPoint b = plan.Points.Single(p => p.Name == "b");
        Assert.Equal((byte)3, a.UnitId);
        Assert.Equal(WordOrder.LittleEndian, a.WordOrder);
        Assert.Equal(ByteOrder.LittleEndian, a.ByteOrder);
        Assert.Equal((byte)7, b.UnitId);
        Assert.Equal(WordOrder.BigEndian, b.WordOrder);
    }

    [Fact]
    public void Adjacent_points_are_merged_and_gaps_split_requests()
    {
        ModbusDatasetPlan plan = Build(Dataset(null,
            ("t", "hr:0", """{"dataType":"Float32"}"""),
            ("h", "hr:2", null),
            ("far", "hr:10", null),
            ("bit", "hr:2", """{"bit": 3}"""),
            ("c0", "coil:0", null),
            ("c1", "coil:1", null),
            ("i", "ir:0", null)));

        Assert.Equal(4, plan.Blocks.Count);
        ModbusReadBlock first = plan.Blocks.Single(b => b.Table == ModbusTable.HoldingRegister && b.Start == 0);
        Assert.Equal(3, first.Count);
        Assert.Equal(["t", "h", "bit"], first.Points.Select(p => p.Name).OrderBy(n => n == "t" ? 0 : n == "h" ? 1 : 2));
        Assert.Contains(plan.Blocks, b => b.Table == ModbusTable.HoldingRegister && b.Start == 10 && b.Count == 1);
        Assert.Contains(plan.Blocks, b => b.Table == ModbusTable.Coil && b.Start == 0 && b.Count == 2);
        Assert.Contains(plan.Blocks, b => b.Table == ModbusTable.InputRegister && b.Start == 0 && b.Count == 1);
    }

    [Fact]
    public void MaxGap_allows_bridging_unused_addresses()
    {
        ModbusDatasetPlan plan = Build(Dataset("""{"maxGap": 8}""", ("a", "hr:0", null), ("b", "hr:9", null)));
        ModbusReadBlock block = Assert.Single(plan.Blocks);
        Assert.Equal(0, block.Start);
        Assert.Equal(10, block.Count);
    }

    [Fact]
    public void Requests_respect_size_limit()
    {
        var points = Enumerable.Range(0, 10).Select(i => ($"p{i}", $"hr:{i}", (string?)null)).ToArray();
        ModbusDatasetPlan plan = Build(Dataset("""{"maxRegistersPerRead": 4}""", points));
        Assert.Equal([4, 4, 2], plan.Blocks.Select(b => (int)b.Count));
    }

    [Fact]
    public void Different_units_are_never_merged()
    {
        ModbusDatasetPlan plan = Build(Dataset(null, ("a", "hr:0", null), ("b", "hr:1", """{"unitId":2}""")));
        Assert.Equal(2, plan.Blocks.Count);
    }

    [Theory]
    [InlineData("hr:0", """{"dataType":"String"}""", "length")]
    [InlineData("coil:0", """{"dataType":"Int16"}""", "single bits")]
    [InlineData("coil:0", """{"bit":1}""", "only to registers")]
    [InlineData("hr:0", """{"bit":16}""", "0..15")]
    [InlineData("hr:0", """{"bit":1,"dataType":"UInt16"}""", "requires dataType Boolean")]
    [InlineData("hr:0", """{"dataType":"Boolean","scale":2}""", "numeric")]
    [InlineData("hr:65535", """{"dataType":"Float32"}""", "end of the address space")]
    [InlineData("hr:0", """{"dataType":"Float33"}""", "Invalid Modbus configuration")]
    [InlineData("hr:0", """not json""", "Invalid Modbus configuration")]
    [InlineData("/api/x", null, "Unrecognized Modbus address")]
    public void Invalid_points_are_reported(string source, string? config, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() => Build(Dataset(null, ("p", source, config))));
        Assert.Contains(expected, ex.Message);
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var ex = Assert.Throws<ConfigurationException>(() => Build(Dataset("""{"samplingIntervalMs": 1}""",
            ("a", "bad", null), ("a", "hr:0", null), ("", "hr:1", null))));
        Assert.Contains("4 configuration error(s)", ex.Message);
    }

    [Fact]
    public void Empty_dataset_is_an_error()
    {
        Assert.Throws<ConfigurationException>(() => Build(Dataset(null)));
    }
}
