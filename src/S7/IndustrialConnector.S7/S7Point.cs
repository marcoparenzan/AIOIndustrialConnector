// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.S7;

/// <summary>
/// A fully resolved and validated S7 data point: where to read it and how to decode it.
/// </summary>
/// <param name="Name">Data point name; the property name in the published JSON payload.</param>
/// <param name="Address">Area, data block, byte and bit of the first byte.</param>
/// <param name="DataType">Interpretation of the raw data.</param>
/// <param name="Length">Number of bytes read from the PLC (for a String: 2 header bytes + the maximum length).</param>
/// <param name="Bit">For a Boolean read from a B/W/D address, the bit to extract (0 = least significant); null means "value != 0".
/// For X addresses the bit comes from the address itself.</param>
/// <param name="Scale">Optional linear scale; the published value becomes raw * Scale + Offset.</param>
/// <param name="Offset">Optional linear offset, see <paramref name="Scale"/>.</param>
public sealed record S7Point(
    string Name,
    S7Address Address,
    DataType DataType,
    int Length,
    int? Bit,
    double? Scale,
    double? Offset) : IDataPoint
{
    public bool IsScaled => Scale is not null || Offset is not null;

    /// <summary>Exclusive end byte offset.</summary>
    public int End => Address.ByteOffset + Length;

    public JsonValueType JsonType => DataType.ToJsonType(IsScaled);

    public string Describe() => $"{DataType} at {Address}";
}
