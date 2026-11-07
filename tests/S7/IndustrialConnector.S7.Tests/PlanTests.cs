// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Schema;
using AioIndustrialConnector.Core.Values;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;

namespace AioIndustrialConnector.S7.Tests;

public class PlanTests
{
    internal static AssetDataset Dataset(string? datasetConfiguration, params (string Name, string Source, string? Config)[] points) => new()
    {
        Name = "ds",
        DatasetConfiguration = datasetConfiguration,
        DataPoints = points.Select(p => new AssetDatasetDataPoint { Name = p.Name, DataSource = p.Source, DataPointConfiguration = p.Config }).ToList(),
    };

    private static S7DatasetPlan Build(AssetDataset dataset, string? assetDefaults = null, string? endpoint = null) =>
        S7DatasetPlan.Build(dataset, S7Settings.Parse(assetDefaults, "asset"), S7Settings.Parse(endpoint, "endpoint"));

    [Fact]
    public void Defaults_follow_the_address_width()
    {
        S7DatasetPlan plan = Build(Dataset(null, ("x", "DB1.DBX0.0", null), ("b", "DB1.DBB1", null), ("w", "DB1.DBW2", null), ("d", "DB1.DBD4", null)));

        Assert.Equal(TimeSpan.FromSeconds(1), plan.SamplingInterval);
        Assert.Equal(DataType.Boolean, plan.Points.Single(p => p.Name == "x").DataType);
        Assert.Equal(DataType.Byte, plan.Points.Single(p => p.Name == "b").DataType);
        Assert.Equal(DataType.UInt16, plan.Points.Single(p => p.Name == "w").DataType);
        Assert.Equal(DataType.UInt32, plan.Points.Single(p => p.Name == "d").DataType);
    }

    [Fact]
    public void Siemens_data_type_names_are_accepted()
    {
        S7DatasetPlan plan = Build(Dataset(null,
            ("a", "DB1.DBD0", """{"dataType":"Real"}"""),
            ("b", "DB1.DBD4", """{"dataType":"DInt"}"""),
            ("c", "DB1.DBW8", """{"dataType":"INT"}"""),
            ("d", "DB1.DBB10", """{"dataType":"LReal"}""")));

        Assert.Equal(DataType.Float32, plan.Points.Single(p => p.Name == "a").DataType);
        Assert.Equal(DataType.Int32, plan.Points.Single(p => p.Name == "b").DataType);
        Assert.Equal(DataType.Int16, plan.Points.Single(p => p.Name == "c").DataType);
        Assert.Equal(DataType.Float64, plan.Points.Single(p => p.Name == "d").DataType);
    }

    [Fact]
    public void Point_overrides_dataset_overrides_asset()
    {
        S7DatasetPlan plan = Build(
            Dataset("""{"samplingIntervalMs": 250}""", ("a", "DB1.DBD0", """{"dataType":"Float32"}""")),
            assetDefaults: """{"samplingIntervalMs": 5000, "maxGap": 4}""");

        Assert.Equal(TimeSpan.FromMilliseconds(250), plan.SamplingInterval);
    }

    [Fact]
    public void Adjacent_points_of_one_data_block_share_a_request_and_other_blocks_do_not()
    {
        S7DatasetPlan plan = Build(Dataset(null,
            ("a", "DB1.DBW0", null), ("b", "DB1.DBW2", null), ("c", "DB1.DBD4", null),
            ("d", "DB2.DBW0", null), ("e", "MW0", null)));

        Assert.Equal(3, plan.Blocks.Count);
        S7ReadBlock db1 = plan.Blocks.Single(b => b is { Area: S7Area.DataBlock, DbNumber: 1 });
        Assert.Equal((0, 8), (db1.Start, db1.Count));
        Assert.Equal(3, db1.Points.Count);
    }

    [Fact]
    public void Gaps_are_bridged_only_up_to_maxGap_and_requests_are_capped()
    {
        var points = new[] { ("a", "DB1.DBW0", (string?)null), ("b", "DB1.DBW10", (string?)null) };

        Assert.Equal(2, Build(Dataset(null, points)).Blocks.Count);
        Assert.Single(Build(Dataset("""{"maxGap": 8}""", points)).Blocks);
        Assert.Equal(2, Build(Dataset("""{"maxGap": 8, "maxBytesPerRead": 6}""", points)).Blocks.Count);
    }

    [Fact]
    public void String_length_covers_the_two_header_bytes()
    {
        S7DatasetPlan plan = Build(Dataset(null, ("s", "DB1.DBB20", """{"dataType":"String","length":16}""")));

        S7Point point = Assert.Single(plan.Points);
        Assert.Equal(18, point.Length);
        Assert.Equal(JsonValueType.String, point.JsonType);
    }

    [Theory]
    [InlineData("DB1.DBX0.0", """{"dataType":"UInt16"}""", "does not fit")]
    [InlineData("DB1.DBW0", """{"dataType":"Float32"}""", "does not fit")]
    [InlineData("DB1.DBD0", """{"dataType":"Float64"}""", "does not fit")]
    [InlineData("DB1.DBB0", """{"dataType":"String"}""", "requires 'length'")]
    [InlineData("DB1.DBB0", """{"dataType":"String","length":300}""", "requires 'length'")]
    [InlineData("DB1.DBW0", """{"dataType":"Boolean","bit":16}""", "bit must be 0..15")]
    [InlineData("DB1.DBW0", """{"bit":1}""", "requires dataType Boolean")]
    [InlineData("DB1.DBX0.1", """{"bit":1}""", "cannot be combined")]
    [InlineData("DB1.DBW0", """{"dataType":"Boolean","scale":2}""", "numeric")]
    [InlineData("DB1.DBW0", """{"dataType":"Nonsense"}""", "Unknown S7 dataType")]
    [InlineData("hr:0", null, "Unrecognized S7 address")]
    public void Invalid_points_are_reported(string source, string? config, string expected)
    {
        var ex = Assert.Throws<ConfigurationException>(() => Build(Dataset(null, ("p", source, config))));
        Assert.Contains(expected, ex.Message);
        Assert.Contains("'p'", ex.Message);
    }

    [Fact]
    public void All_errors_are_reported_together()
    {
        var ex = Assert.Throws<ConfigurationException>(() => Build(Dataset("""{"samplingIntervalMs": 1}""",
            ("a", "bogus", null), ("a", "DB1.DBW0", null), ("b", "DB1.DBW2", """{"dataType":"Float32"}"""))));

        Assert.Contains("4 configuration error(s)", ex.Message);
    }

    [Fact]
    public void Empty_dataset_is_rejected()
    {
        Assert.Throws<ConfigurationException>(() => Build(Dataset(null)));
    }

    [Fact]
    public void Schema_lists_every_point_with_nullable_types()
    {
        S7DatasetPlan plan = Build(Dataset(null, ("flag", "DB1.DBX0.0", null), ("temp", "DB1.DBD2", """{"dataType":"Real"}"""), ("count", "DB1.DBW6", null)));

        string schema = JsonSchemaBuilder.Build(plan);

        using var doc = System.Text.Json.JsonDocument.Parse(schema);
        var props = doc.RootElement.GetProperty("properties");
        Assert.Equal("boolean", props.GetProperty("flag").GetProperty("type")[0].GetString());
        Assert.Equal("number", props.GetProperty("temp").GetProperty("type")[0].GetString());
        Assert.Equal("integer", props.GetProperty("count").GetProperty("type")[0].GetString());
        Assert.Equal("null", props.GetProperty("count").GetProperty("type")[1].GetString());
    }
}
