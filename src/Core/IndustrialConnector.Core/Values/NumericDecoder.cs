// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;

namespace AioIndustrialConnector.Core.Values;

/// <summary>
/// Converts big-endian bytes into the typed values every protocol publishes. Protocols only differ in how they
/// obtain the big-endian bytes (Modbus swaps bytes/words first, S7 is big endian natively).
/// </summary>
/// <remarks>
/// Returned values are <see cref="bool"/>, <see cref="long"/>, <see cref="ulong"/>, <see cref="double"/> or <see cref="string"/>
/// (a fixed set, so the JSON writer can switch on it). A scaled value is always a <see cref="double"/>.
/// Non-finite floating point values (NaN, infinity) are returned as null because JSON cannot represent them.
/// </remarks>
public static class NumericDecoder
{
    /// <param name="type">A numeric type (not Boolean/String).</param>
    /// <param name="bigEndian">Exactly <see cref="DataTypeExtensions.ByteSize"/> bytes, most significant first.</param>
    /// <param name="scale">Optional linear scale: result = raw * scale + offset.</param>
    /// <param name="offset">Optional linear offset.</param>
    public static object? Decode(DataType type, ReadOnlySpan<byte> bigEndian, double? scale, double? offset)
    {
        if (bigEndian.Length != type.ByteSize())
        {
            throw new ArgumentException($"{type} needs {type.ByteSize()} byte(s), got {bigEndian.Length}.", nameof(bigEndian));
        }

        // Every arm is cast to object on purpose: otherwise the switch expression's natural type would be
        // double (the best common type of long, ulong and double) and integers would silently lose precision.
        object raw = type switch
        {
            DataType.Byte => (object)(ulong)bigEndian[0],
            DataType.SByte => (object)(long)(sbyte)bigEndian[0],
            DataType.Int16 => (object)(long)BinaryPrimitives.ReadInt16BigEndian(bigEndian),
            DataType.UInt16 => (object)(ulong)BinaryPrimitives.ReadUInt16BigEndian(bigEndian),
            DataType.Int32 => (object)(long)BinaryPrimitives.ReadInt32BigEndian(bigEndian),
            DataType.UInt32 => (object)(ulong)BinaryPrimitives.ReadUInt32BigEndian(bigEndian),
            DataType.Int64 => (object)BinaryPrimitives.ReadInt64BigEndian(bigEndian),
            DataType.UInt64 => (object)BinaryPrimitives.ReadUInt64BigEndian(bigEndian),
            DataType.Float32 => (object)(double)BinaryPrimitives.ReadSingleBigEndian(bigEndian),
            DataType.Float64 => (object)BinaryPrimitives.ReadDoubleBigEndian(bigEndian),
            _ => throw new ArgumentOutOfRangeException(nameof(type), $"Unsupported numeric data type {type}."),
        };

        if (scale is not null || offset is not null)
        {
            double value = raw switch
            {
                long l => l,
                ulong u => u,
                double d => d,
                _ => throw new InvalidOperationException(),
            };

            raw = value * (scale ?? 1.0) + (offset ?? 0.0);
        }

        return raw is double result && !double.IsFinite(result) ? null : raw;
    }
}
