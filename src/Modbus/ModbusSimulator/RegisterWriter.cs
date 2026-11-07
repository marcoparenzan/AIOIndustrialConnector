// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;
using NModbus;

namespace AioIndustrialConnector.Modbus.Simulator;

/// <summary>
/// Encodes typed values into 16-bit registers, in the layouts the connector can decode.
/// </summary>
public static class RegisterWriter
{
    /// <summary>Big-endian registers of a 32-bit float; <paramref name="wordSwap"/> puts the low word first.</summary>
    public static ushort[] Float32(float value, bool wordSwap = false) => Words(BitConverter.SingleToUInt32Bits(value), wordSwap);

    public static ushort[] Int32(int value, bool wordSwap = false) => Words(unchecked((uint)value), wordSwap);

    public static ushort[] UInt32(uint value, bool wordSwap = false) => Words(value, wordSwap);

    public static ushort[] Float64(double value, bool wordSwap = false) => Words64(BitConverter.DoubleToUInt64Bits(value), wordSwap);

    public static ushort[] Int64(long value, bool wordSwap = false) => Words64(unchecked((ulong)value), wordSwap);

    /// <summary>ASCII string, two characters per register, padded with NULs to <paramref name="registers"/>.</summary>
    public static ushort[] Ascii(string text, int registers)
    {
        byte[] bytes = new byte[registers * 2];
        Encoding.Latin1.GetBytes(text.AsSpan(0, Math.Min(text.Length, bytes.Length)), bytes);
        var result = new ushort[registers];
        for (int i = 0; i < registers; i++)
        {
            result[i] = BinaryPrimitives.ReadUInt16BigEndian(bytes.AsSpan(i * 2, 2));
        }

        return result;
    }

    /// <summary>Swaps the two bytes of every register (for byte-swapped devices).</summary>
    public static ushort[] ByteSwap(ushort[] registers) => registers.Select(BinaryPrimitives.ReverseEndianness).ToArray();

    public static void WriteHolding(this ISlaveDataStore store, ushort start, params ushort[] registers) =>
        store.HoldingRegisters.WritePoints(start, registers);

    public static void WriteInput(this ISlaveDataStore store, ushort start, params ushort[] registers) =>
        store.InputRegisters.WritePoints(start, registers);

    public static void WriteCoils(this ISlaveDataStore store, ushort start, params bool[] values) =>
        store.CoilDiscretes.WritePoints(start, values);

    public static void WriteDiscreteInputs(this ISlaveDataStore store, ushort start, params bool[] values) =>
        store.CoilInputs.WritePoints(start, values);

    private static ushort[] Words(uint value, bool wordSwap)
    {
        ushort high = (ushort)(value >> 16);
        ushort low = (ushort)value;
        return wordSwap ? [low, high] : [high, low];
    }

    private static ushort[] Words64(ulong value, bool wordSwap)
    {
        ushort[] words = [(ushort)(value >> 48), (ushort)(value >> 32), (ushort)(value >> 16), (ushort)value];
        if (wordSwap)
        {
            Array.Reverse(words);
        }

        return words;
    }
}
