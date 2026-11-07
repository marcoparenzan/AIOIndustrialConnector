// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// A fully resolved and validated data point: where to read it and how to decode it.
/// </summary>
/// <param name="Name">Data point name; the property name in the published JSON payload.</param>
/// <param name="Address">Table and zero-based offset of the first bit or register.</param>
/// <param name="UnitId">Modbus unit identifier to address.</param>
/// <param name="DataType">Interpretation of the raw data.</param>
/// <param name="ByteOrder">Byte order inside a register.</param>
/// <param name="WordOrder">Register order of multi-register values.</param>
/// <param name="Length">Number of bits (always 1 for bit tables) or registers occupied.</param>
/// <param name="Bit">For a boolean read from a register, the bit to extract (0 = least significant); null means "register != 0".</param>
/// <param name="Scale">Optional linear scale; the published value becomes raw * Scale + Offset.</param>
/// <param name="Offset">Optional linear offset, see <paramref name="Scale"/>.</param>
public sealed record ModbusPoint(
    string Name,
    ModbusAddress Address,
    byte UnitId,
    DataType DataType,
    ByteOrder ByteOrder,
    WordOrder WordOrder,
    int Length,
    int? Bit,
    double? Scale,
    double? Offset) : IDataPoint
{
    /// <summary>True when a scale or offset is configured, so the value is published as a floating point number.</summary>
    public bool IsScaled => Scale is not null || Offset is not null;

    /// <summary>Exclusive end offset (first address after this point).</summary>
    public int End => Address.Offset + Length;

    public JsonValueType JsonType => DataType.ToJsonType(IsScaled);

    public string Describe() => $"{DataType} at {Address} (unit {UnitId})";
}
