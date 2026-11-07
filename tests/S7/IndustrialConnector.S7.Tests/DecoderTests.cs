// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers.Binary;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.S7.Tests;

public class DecoderTests
{
    private static S7Point Point(DataType type, S7Address address, int length = 0, int? bit = null, double? scale = null, double? offset = null) =>
        new("p", address, type, length == 0 ? Math.Max(type.ByteSize(), address.WidthBytes) : length, bit, scale, offset);

    private static readonly S7Address Word = new(S7Area.DataBlock, 1, 0, 0, S7Width.Word);
    private static readonly S7Address DWord = new(S7Area.DataBlock, 1, 0, 0, S7Width.DWord);
    private static readonly S7Address ByteAt = new(S7Area.DataBlock, 1, 0, 0, S7Width.Byte);

    [Fact]
    public void Integers_are_big_endian_and_signed_or_unsigned()
    {
        Assert.Equal(-2L, S7ValueDecoder.Decode(Point(DataType.Int16, Word), [0xFF, 0xFE]));
        Assert.Equal(65534UL, S7ValueDecoder.Decode(Point(DataType.UInt16, Word), [0xFF, 0xFE]));
        Assert.Equal(-100000L, S7ValueDecoder.Decode(Point(DataType.Int32, DWord), [0xFF, 0xFE, 0x79, 0x60]));
        Assert.Equal(4000000000UL, S7ValueDecoder.Decode(Point(DataType.UInt32, DWord), [0xEE, 0x6B, 0x28, 0x00]));
        Assert.Equal(200UL, S7ValueDecoder.Decode(Point(DataType.Byte, ByteAt), [200]));
        Assert.Equal(-56L, S7ValueDecoder.Decode(Point(DataType.SByte, ByteAt), [200]));
    }

    [Fact]
    public void Floats_decode_and_non_finite_values_become_null()
    {
        byte[] real = new byte[4];
        BinaryPrimitives.WriteSingleBigEndian(real, 21.5f);
        Assert.Equal(21.5, S7ValueDecoder.Decode(Point(DataType.Float32, DWord), real));

        byte[] lreal = new byte[8];
        BinaryPrimitives.WriteDoubleBigEndian(lreal, Math.PI);
        Assert.Equal(Math.PI, S7ValueDecoder.Decode(Point(DataType.Float64, ByteAt), lreal));

        BinaryPrimitives.WriteSingleBigEndian(real, float.NaN);
        Assert.Null(S7ValueDecoder.Decode(Point(DataType.Float32, DWord), real));
    }

    [Fact]
    public void Scale_and_offset_give_a_double()
    {
        object? value = S7ValueDecoder.Decode(Point(DataType.UInt16, Word, scale: 0.1, offset: -5), [0x02, 0x0B]); // 523
        Assert.Equal(523 * 0.1 - 5, Assert.IsType<double>(value), 9);
    }

    [Fact]
    public void Bits_come_from_the_address_or_from_the_bit_setting()
    {
        var x = new S7Address(S7Area.DataBlock, 1, 0, 3, S7Width.Bit);
        Assert.Equal(true, S7ValueDecoder.Decode(Point(DataType.Boolean, x), [0b1000]));
        Assert.Equal(false, S7ValueDecoder.Decode(Point(DataType.Boolean, x), [0b0111]));

        Assert.Equal(true, S7ValueDecoder.Decode(Point(DataType.Boolean, Word, bit: 8), [0x01, 0x00])); // bit 8 = lsb of the high byte
        Assert.Equal(false, S7ValueDecoder.Decode(Point(DataType.Boolean, Word, bit: 0), [0x01, 0x00]));
        Assert.Equal(true, S7ValueDecoder.Decode(Point(DataType.Boolean, Word), [0x00, 0x2A]));
    }

    [Fact]
    public void String_uses_the_current_length_from_the_header()
    {
        byte[] s7String = [16, 5, (byte)'H', (byte)'E', (byte)'L', (byte)'L', (byte)'O', 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0];
        Assert.Equal("HELLO", S7ValueDecoder.Decode(Point(DataType.String, ByteAt, length: 18), s7String));

        byte[] empty = new byte[18];
        empty[0] = 16;
        Assert.Equal("", S7ValueDecoder.Decode(Point(DataType.String, ByteAt, length: 18), empty));

        byte[] corrupt = new byte[18];
        corrupt[0] = 16;
        corrupt[1] = 200; // current length beyond the declared size must not read past the buffer
        Assert.Equal(16, ((string)S7ValueDecoder.Decode(Point(DataType.String, ByteAt, length: 18), corrupt)!).Length);
    }

    [Fact]
    public void Wrong_byte_count_is_rejected()
    {
        Assert.Throws<ArgumentException>(() => S7ValueDecoder.Decode(Point(DataType.Float32, DWord), [1, 2]));
    }
}
