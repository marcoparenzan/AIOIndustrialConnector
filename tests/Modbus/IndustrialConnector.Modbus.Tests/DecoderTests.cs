// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Values;
using AioIndustrialConnector.Modbus;
using AioIndustrialConnector.Modbus.Simulator;

namespace AioIndustrialConnector.Modbus.Tests;

public class DecoderTests
{
    private static ModbusPoint Point(DataType type, int length = 0, ByteOrder bo = ByteOrder.BigEndian, WordOrder wo = WordOrder.BigEndian,
        int? bit = null, double? scale = null, double? offset = null) =>
        new("p", new ModbusAddress(ModbusTable.HoldingRegister, 0), 1, type, bo, wo, length == 0 ? type.RegisterCount() : length, bit, scale, offset);

    [Fact]
    public void Decodes_int16_and_uint16()
    {
        Assert.Equal(-2L, ModbusValueDecoder.DecodeRegisters(Point(DataType.Int16), [0xFFFE]));
        Assert.Equal(65534UL, ModbusValueDecoder.DecodeRegisters(Point(DataType.UInt16), [0xFFFE]));
    }

    [Fact]
    public void Decodes_float32_in_all_byte_and_word_orders()
    {
        const float value = 123.456f;
        ushort[] abcd = RegisterWriter.Float32(value);
        ushort[] cdab = RegisterWriter.Float32(value, wordSwap: true);
        ushort[] badc = RegisterWriter.ByteSwap(abcd);
        ushort[] dcba = RegisterWriter.ByteSwap(cdab);

        Assert.Equal((double)value, ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32), abcd));
        Assert.Equal((double)value, ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32, wo: WordOrder.LittleEndian), cdab));
        Assert.Equal((double)value, ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32, bo: ByteOrder.LittleEndian), badc));
        Assert.Equal((double)value, ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32, bo: ByteOrder.LittleEndian, wo: WordOrder.LittleEndian), dcba));
    }

    [Fact]
    public void Decodes_32_and_64_bit_integers_and_float64()
    {
        Assert.Equal(-100000L, ModbusValueDecoder.DecodeRegisters(Point(DataType.Int32), RegisterWriter.Int32(-100000)));
        Assert.Equal(4000000000UL, ModbusValueDecoder.DecodeRegisters(Point(DataType.UInt32), RegisterWriter.UInt32(4000000000)));
        Assert.Equal(-1234567890123L, ModbusValueDecoder.DecodeRegisters(Point(DataType.Int64, wo: WordOrder.LittleEndian), RegisterWriter.Int64(-1234567890123, wordSwap: true)));
        Assert.Equal(Math.PI, ModbusValueDecoder.DecodeRegisters(Point(DataType.Float64), RegisterWriter.Float64(Math.PI)));
    }

    [Fact]
    public void Applies_scale_and_offset_as_double()
    {
        object? value = ModbusValueDecoder.DecodeRegisters(Point(DataType.UInt16, scale: 0.1, offset: -5), [523]);
        Assert.IsType<double>(value);
        Assert.Equal(47.3, (double)value!, 9);
    }

    [Fact]
    public void Extracts_bits_and_nonzero_booleans()
    {
        Assert.Equal(true, ModbusValueDecoder.DecodeRegisters(Point(DataType.Boolean, bit: 1), [0b10]));
        Assert.Equal(false, ModbusValueDecoder.DecodeRegisters(Point(DataType.Boolean, bit: 0), [0b10]));
        Assert.Equal(true, ModbusValueDecoder.DecodeRegisters(Point(DataType.Boolean, bit: 15), [0x8000]));
        Assert.Equal(true, ModbusValueDecoder.DecodeRegisters(Point(DataType.Boolean), [42]));
        Assert.Equal(false, ModbusValueDecoder.DecodeRegisters(Point(DataType.Boolean), [0]));
    }

    [Fact]
    public void Decodes_strings_and_trims_padding()
    {
        Assert.Equal("AIO-SIM-0001", ModbusValueDecoder.DecodeRegisters(Point(DataType.String, length: 8), RegisterWriter.Ascii("AIO-SIM-0001", 8)));
        Assert.Equal("AB", ModbusValueDecoder.DecodeRegisters(Point(DataType.String, length: 2), RegisterWriter.Ascii("AB  ", 2)));
        Assert.Equal("HELLO", ModbusValueDecoder.DecodeRegisters(Point(DataType.String, length: 3, bo: ByteOrder.LittleEndian), RegisterWriter.ByteSwap(RegisterWriter.Ascii("HELLO", 3))));
    }

    [Fact]
    public void Non_finite_floats_become_null()
    {
        Assert.Null(ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32), RegisterWriter.Float32(float.NaN)));
        Assert.Null(ModbusValueDecoder.DecodeRegisters(Point(DataType.Float64), RegisterWriter.Float64(double.PositiveInfinity)));
    }

    [Fact]
    public void Rejects_wrong_register_count()
    {
        Assert.Throws<ArgumentException>(() => ModbusValueDecoder.DecodeRegisters(Point(DataType.Float32), [1]));
    }
}
