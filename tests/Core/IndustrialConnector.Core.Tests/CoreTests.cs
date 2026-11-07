// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text;
using AioIndustrialConnector.Core.Hosting;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;
using AioIndustrialConnector.S7;

namespace AioIndustrialConnector.Core.Tests;

public class ProtocolCatalogTests
{
    private static readonly ProtocolCatalog Catalog = new([new ModbusProtocol(), new S7Protocol()]);

    private static string? NoEnvironment(string _) => null;

    [Theory]
    [InlineData("modbus", "modbus")]
    [InlineData("Modbus-TCP", "modbus")]
    [InlineData("s7", "s7")]
    [InlineData("SIEMENS", "s7")]
    public void Selects_by_name_or_alias(string requested, string expected) =>
        Assert.Equal(expected, Catalog.Select(["--protocol", requested], NoEnvironment).Name);

    [Fact]
    public void Argument_wins_over_environment()
    {
        IProtocolModule module = Catalog.Select(["--protocol", "s7"], name => name == ProtocolCatalog.EnvironmentVariable ? "modbus" : null);
        Assert.Equal("s7", module.Name);
    }

    [Fact]
    public void Environment_is_used_without_argument()
    {
        IProtocolModule module = Catalog.Select([], name => name == ProtocolCatalog.EnvironmentVariable ? "s7" : null);
        Assert.Equal("s7", module.Name);
    }

    [Fact]
    public void Missing_or_unknown_protocol_is_a_configuration_error()
    {
        var missing = Assert.Throws<ConfigurationException>(() => Catalog.Select([], NoEnvironment));
        Assert.Contains("modbus, s7", missing.Message);
        Assert.Throws<ConfigurationException>(() => Catalog.Select(["--protocol", "profinet"], NoEnvironment));
    }

    [Fact]
    public void Modules_have_distinct_endpoint_types_and_lock_names()
    {
        Assert.Equal(2, Catalog.Modules.Select(m => m.EndpointType).Distinct().Count());
        Assert.Equal(2, Catalog.Modules.Select(m => m.LeaderElectionLockName).Distinct().Count());
        Assert.Equal("Custom.ModbusTcp", new ModbusProtocol().EndpointType);
        Assert.Equal("Custom.SiemensS7", new S7Protocol().EndpointType);
    }
}

public class NumericDecoderTests
{
    [Fact]
    public void Integers_keep_full_64_bit_precision()
    {
        Assert.Equal(long.MinValue, NumericDecoder.Decode(DataType.Int64, [0x80, 0, 0, 0, 0, 0, 0, 0], null, null));
        Assert.Equal(ulong.MaxValue, NumericDecoder.Decode(DataType.UInt64, [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF], null, null));
    }

    [Fact]
    public void Wrong_length_and_non_numeric_types_are_rejected()
    {
        Assert.Throws<ArgumentException>(() => NumericDecoder.Decode(DataType.Int32, [1, 2], null, null));
        Assert.Throws<ArgumentOutOfRangeException>(() => NumericDecoder.Decode(DataType.String, [], null, null));
    }
}

public class RangePlannerTests
{
    private sealed record P(string Name, int Group, int Start, int End);

    private sealed record B(int Group, int Start, int Count, IReadOnlyList<P> Points);

    private static IReadOnlyList<B> Plan(IEnumerable<P> points, int limit, int gap) =>
        RangePlanner.Plan(points, p => p.Group, p => p.Start, p => p.End, _ => limit, gap, (g, s, c, pts) => new B(g, s, c, pts), p => p.Name);

    [Fact]
    public void Merges_adjacent_overlapping_and_gapped_points_within_limits()
    {
        var points = new[] { new P("a", 1, 0, 2), new P("b", 1, 2, 4), new P("c", 1, 3, 5), new P("d", 1, 9, 10), new P("e", 2, 0, 1) };

        IReadOnlyList<B> blocks = Plan(points, limit: 100, gap: 0);
        Assert.Equal([(1, 0, 5), (1, 9, 1), (2, 0, 1)], blocks.Select(b => (b.Group, b.Start, b.Count)));

        blocks = Plan(points, limit: 100, gap: 4);
        Assert.Equal([(1, 0, 10), (2, 0, 1)], blocks.Select(b => (b.Group, b.Start, b.Count)));

        blocks = Plan(points, limit: 4, gap: 4);
        Assert.True(blocks.All(b => b.Count <= 4));
    }

    [Fact]
    public void A_point_larger_than_the_limit_is_an_error()
    {
        Assert.Throws<ArgumentException>(() => Plan([new P("big", 1, 0, 50)], limit: 10, gap: 0));
    }
}

public class PayloadSerializerTests
{
    private sealed record Pt(string Name) : IDataPoint
    {
        public JsonValueType JsonType => JsonValueType.Integer;

        public string Describe() => Name;
    }

    [Fact]
    public void Writes_every_supported_value_type_in_point_order_and_missing_values_as_null()
    {
        var points = new IDataPoint[] { new Pt("b"), new Pt("l"), new Pt("u"), new Pt("d"), new Pt("s"), new Pt("n"), new Pt("absent") };
        var values = new Dictionary<string, object?> { ["b"] = true, ["l"] = -5L, ["u"] = ulong.MaxValue, ["d"] = 1.5, ["s"] = "x", ["n"] = null };

        Assert.Equal("""{"b":true,"l":-5,"u":18446744073709551615,"d":1.5,"s":"x","n":null,"absent":null}""",
            Encoding.UTF8.GetString(PayloadSerializer.Serialize(points, values)));
    }

    [Fact]
    public void Unexpected_value_types_are_a_bug_and_throw()
    {
        Assert.Throws<InvalidOperationException>(() => PayloadSerializer.Serialize([new Pt("x")], new Dictionary<string, object?> { ["x"] = 5 }));
    }
}
