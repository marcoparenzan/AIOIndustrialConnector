// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using System.Text;

namespace AioIndustrialConnector.S7.Simulator;

/// <summary>
/// The memory of a simulated PLC: data blocks, flags (M), inputs (I) and outputs (Q), as plain big-endian byte arrays.
/// Not thread safe by itself; <see cref="S7SimulatorServer.Update"/> serializes access with the protocol thread.
/// </summary>
public sealed class S7Memory
{
    /// <summary>S7comm area codes, as sent on the wire.</summary>
    public const byte Input = 0x81, Output = 0x82, Memory = 0x83, DataBlock = 0x84;

    private readonly Dictionary<(byte Area, int Db), byte[]> _areas = new();

    public S7Memory(int flagBytes = 256)
    {
        foreach (byte area in new[] { Input, Output, Memory })
        {
            _areas[(area, 0)] = new byte[flagBytes];
        }
    }

    /// <summary>Creates data block <paramref name="number"/>. A data block that was never created does not exist for the client.</summary>
    public S7Memory CreateDataBlock(int number, int size)
    {
        _areas[(DataBlock, number)] = new byte[size];
        return this;
    }

    /// <summary>The bytes of an area, or null when the data block does not exist.</summary>
    public byte[]? Find(byte area, int db) => _areas.GetValueOrDefault((area, area == DataBlock ? db : 0));

    private Span<byte> At(byte area, int db, int offset, int length) => Find(area, db)!.AsSpan(offset, length);

    public void SetBit(byte area, int db, int byteOffset, int bit, bool value)
    {
        Span<byte> b = At(area, db, byteOffset, 1);
        b[0] = (byte)(value ? b[0] | (1 << bit) : b[0] & ~(1 << bit));
    }

    public void SetByte(byte area, int db, int offset, byte value) => At(area, db, offset, 1)[0] = value;

    public void SetInt16(byte area, int db, int offset, short value) => BinaryPrimitives.WriteInt16BigEndian(At(area, db, offset, 2), value);

    public void SetUInt16(byte area, int db, int offset, ushort value) => BinaryPrimitives.WriteUInt16BigEndian(At(area, db, offset, 2), value);

    public void SetInt32(byte area, int db, int offset, int value) => BinaryPrimitives.WriteInt32BigEndian(At(area, db, offset, 4), value);

    public void SetUInt32(byte area, int db, int offset, uint value) => BinaryPrimitives.WriteUInt32BigEndian(At(area, db, offset, 4), value);

    public void SetInt64(byte area, int db, int offset, long value) => BinaryPrimitives.WriteInt64BigEndian(At(area, db, offset, 8), value);

    public void SetFloat(byte area, int db, int offset, float value) => BinaryPrimitives.WriteSingleBigEndian(At(area, db, offset, 4), value);

    public void SetDouble(byte area, int db, int offset, double value) => BinaryPrimitives.WriteDoubleBigEndian(At(area, db, offset, 8), value);

    /// <summary>Writes an S7 STRING[<paramref name="maxLength"/>]: maximum length, current length, characters.</summary>
    public void SetString(byte area, int db, int offset, int maxLength, string text)
    {
        Span<byte> s = At(area, db, offset, maxLength + 2);
        s.Clear();
        byte[] chars = Encoding.Latin1.GetBytes(text.Length > maxLength ? text[..maxLength] : text);
        s[0] = (byte)maxLength;
        s[1] = (byte)chars.Length;
        chars.CopyTo(s[2..]);
    }
}
