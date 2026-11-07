// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.S7;

/// <summary>
/// Converts the bytes read from an S7 PLC into typed values. S7 is big endian natively, so the numeric conversion
/// is the shared <see cref="NumericDecoder"/>; this class adds bits and the S7 STRING framing.
/// </summary>
public static class S7ValueDecoder
{
    /// <param name="point">The point definition.</param>
    /// <param name="bytes">Exactly <see cref="S7Point.Length"/> bytes, as received.</param>
    public static object? Decode(S7Point point, ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != point.Length)
        {
            throw new ArgumentException($"Data point '{point.Name}' needs {point.Length} byte(s), got {bytes.Length}.", nameof(bytes));
        }

        switch (point.DataType)
        {
            case DataType.String:
                return DecodeString(bytes);

            case DataType.Boolean when point.Address.Width == S7Width.Bit:
                return ((bytes[0] >> point.Address.BitOffset) & 1) == 1;

            case DataType.Boolean:
                {
                    ulong raw = 0;
                    foreach (byte b in bytes)
                    {
                        raw = (raw << 8) | b; // big endian: the first byte is the most significant
                    }

                    return point.Bit is int bit ? ((raw >> bit) & 1) == 1 : raw != 0;
                }

            default:
                return NumericDecoder.Decode(point.DataType, bytes, point.Scale, point.Offset);
        }
    }

    /// <summary>S7 STRING: byte 0 = declared maximum length, byte 1 = current length, then the characters.</summary>
    private static string DecodeString(ReadOnlySpan<byte> bytes)
    {
        int current = bytes[1];
        int available = bytes.Length - 2;
        int length = Math.Min(Math.Min(current, bytes[0]), available);
        return Encoding.Latin1.GetString(bytes.Slice(2, length));
    }
}
