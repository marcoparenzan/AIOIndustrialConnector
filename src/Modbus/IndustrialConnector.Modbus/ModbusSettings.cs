// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Modbus settings, read from the stringified JSON fields of the Azure Device Registry resources.
/// </summary>
/// <remarks>
/// The same shape is accepted at every level; a more specific level overrides a more generic one:
/// data point (<c>dataPointConfiguration</c>) &gt; dataset (<c>datasetConfiguration</c>) &gt;
/// asset (<c>defaultDatasetsConfiguration</c>) &gt; device endpoint (<c>additionalConfiguration</c>) &gt; built-in defaults.
/// Not every property is meaningful at every level (for example <see cref="DataType"/> only on a data point,
/// <see cref="TimeoutMs"/> only on the endpoint); properties set at a level where they make no sense are ignored.
/// </remarks>
public sealed record ModbusSettings : IProtocolSettings<ModbusSettings>
{
    /// <summary>Modbus unit identifier (slave id), 0..255. Default 1.</summary>
    public byte? UnitId { get; init; }

    /// <summary>Endpoint level: response timeout per request, in milliseconds. Default 3000.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Endpoint level: TCP connect timeout, in milliseconds. Default 5000.</summary>
    public int? ConnectTimeoutMs { get; init; }

    /// <summary>Dataset level: sampling interval, in milliseconds. Default 1000.</summary>
    public int? SamplingIntervalMs { get; init; }

    /// <summary>Byte order inside a register. Default big endian.</summary>
    public ByteOrder? ByteOrder { get; init; }

    /// <summary>Register order of multi-register values. Default big endian (high word first).</summary>
    public WordOrder? WordOrder { get; init; }

    /// <summary>Largest register block read in one request (protocol limit 125).</summary>
    public int? MaxRegistersPerRead { get; init; }

    /// <summary>Largest bit block read in one request (protocol limit 2000).</summary>
    public int? MaxBitsPerRead { get; init; }

    /// <summary>
    /// Largest number of unused addresses that may be read to merge two data points into one request. Default 0
    /// (merge only adjacent or overlapping points), because many devices reject reads of unmapped addresses.
    /// </summary>
    public int? MaxGap { get; init; }

    /// <summary>Data point level: how to interpret the raw value. Default Boolean for coils/discrete inputs, UInt16 for registers.</summary>
    public DataType? DataType { get; init; }

    /// <summary>Data point level: number of registers of a <see cref="Core.Values.DataType.String"/> value.</summary>
    public int? Length { get; init; }

    /// <summary>Data point level: extract a single bit (0 = least significant) of a register as a boolean.</summary>
    public int? Bit { get; init; }

    /// <summary>Data point level: published value = raw * Scale + Offset. Setting either makes the value a floating point number.</summary>
    public double? Scale { get; init; }

    /// <summary>Data point level: see <see cref="Scale"/>.</summary>
    public double? Offset { get; init; }

    public static ModbusSettings Empty { get; } = new();

    public static ModbusSettings Parse(string? json, string context) =>
        SettingsJson.Parse(json, context, Empty, "Modbus");

    public ModbusSettings WithFallback(ModbusSettings fallback) => new()
    {
        UnitId = UnitId ?? fallback.UnitId,
        TimeoutMs = TimeoutMs ?? fallback.TimeoutMs,
        ConnectTimeoutMs = ConnectTimeoutMs ?? fallback.ConnectTimeoutMs,
        SamplingIntervalMs = SamplingIntervalMs ?? fallback.SamplingIntervalMs,
        ByteOrder = ByteOrder ?? fallback.ByteOrder,
        WordOrder = WordOrder ?? fallback.WordOrder,
        MaxRegistersPerRead = MaxRegistersPerRead ?? fallback.MaxRegistersPerRead,
        MaxBitsPerRead = MaxBitsPerRead ?? fallback.MaxBitsPerRead,
        MaxGap = MaxGap ?? fallback.MaxGap,
        DataType = DataType ?? fallback.DataType,
        Length = Length ?? fallback.Length,
        Bit = Bit ?? fallback.Bit,
        Scale = Scale ?? fallback.Scale,
        Offset = Offset ?? fallback.Offset,
    };
}
