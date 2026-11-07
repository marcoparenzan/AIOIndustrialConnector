// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Converts raw Modbus bits and registers into typed values: applies byte and word order, then delegates the
/// numeric conversion (types, scale/offset, non-finite handling) to the shared <see cref="NumericDecoder"/>.
/// </summary>
public static class ModbusValueDecoder
{
    /// <summary>Decodes a point read from a coil or discrete input table.</summary>
    public static object DecodeBit(bool value) => value;

    /// <summary>
    /// Decodes a point read from a register table.
    /// </summary>
    /// <param name="point">The point definition.</param>
    /// <param name="registers">Exactly <see cref="ModbusPoint.Length"/> registers, in address order, as received (high byte first).</param>
    public static object? DecodeRegisters(ModbusPoint point, ReadOnlySpan<ushort> registers)
    {
        if (registers.Length != point.Length)
        {
            throw new ArgumentException($"Data point '{point.Name}' needs {point.Length} register(s), got {registers.Length}.", nameof(registers));
        }

        if (point.DataType == DataType.String)
        {
            return DecodeString(registers, point.ByteOrder);
        }

        if (point.DataType == DataType.Boolean)
        {
            ushort raw = Swap(registers[0], point.ByteOrder);
            return point.Bit is int bit ? ((raw >> bit) & 1) == 1 : raw != 0;
        }

        Span<byte> bytes = stackalloc byte[8];
        Span<byte> big = bytes[..(registers.Length * 2)];
        ToBigEndianBytes(registers, point.ByteOrder, point.WordOrder, big);
        return NumericDecoder.Decode(point.DataType, big, point.Scale, point.Offset);
    }

    /// <summary>
    /// Writes the registers as one big-endian byte sequence (most significant byte first),
    /// applying the configured byte order inside each register and the register order across registers.
    /// </summary>
    internal static void ToBigEndianBytes(ReadOnlySpan<ushort> registers, ByteOrder byteOrder, WordOrder wordOrder, Span<byte> destination)
    {
        int n = registers.Length;
        for (int i = 0; i < n; i++)
        {
            // With word order little endian the lowest address holds the least significant word.
            ushort register = registers[wordOrder == WordOrder.BigEndian ? i : n - 1 - i];
            BinaryPrimitives.WriteUInt16BigEndian(destination.Slice(i * 2, 2), Swap(register, byteOrder));
        }
    }

    private static ushort Swap(ushort register, ByteOrder byteOrder) =>
        byteOrder == ByteOrder.BigEndian ? register : BinaryPrimitives.ReverseEndianness(register);

    private static string DecodeString(ReadOnlySpan<ushort> registers, ByteOrder byteOrder)
    {
        // Strings are stored in address order (word order does not apply), two characters per register.
        Span<byte> bytes = registers.Length <= 128 ? stackalloc byte[registers.Length * 2] : new byte[registers.Length * 2];
        for (int i = 0; i < registers.Length; i++)
        {
            BinaryPrimitives.WriteUInt16BigEndian(bytes.Slice(i * 2, 2), Swap(registers[i], byteOrder));
        }

        int length = bytes.IndexOf((byte)0);
        if (length < 0)
        {
            length = bytes.Length;
        }

        return Encoding.Latin1.GetString(bytes[..length]).TrimEnd(' ');
    }
}
