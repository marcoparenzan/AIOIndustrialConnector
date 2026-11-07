// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.Modbus;

/// <summary>Order of the two bytes inside one 16-bit register.</summary>
public enum ByteOrder
{
    /// <summary>High byte first (the Modbus standard).</summary>
    BigEndian,

    /// <summary>Low byte first (byte-swapped devices).</summary>
    LittleEndian,
}

/// <summary>Order of the registers of a multi-register value.</summary>
public enum WordOrder
{
    /// <summary>Most significant register first (lowest address holds the high word).</summary>
    BigEndian,

    /// <summary>Least significant register first ("word swapped").</summary>
    LittleEndian,
}

public static class ModbusDataTypeExtensions
{
    /// <summary>Whether Modbus can carry this type (registers are 16 bit, so no 8-bit integers).</summary>
    public static bool IsSupportedByModbus(this DataType type) => type is not (DataType.Byte or DataType.SByte);

    /// <summary>Number of 16-bit registers a value of this type occupies (string length is configured separately).</summary>
    public static int RegisterCount(this DataType type, int stringLength = 1) => type switch
    {
        DataType.Boolean or DataType.Int16 or DataType.UInt16 => 1,
        DataType.Int32 or DataType.UInt32 or DataType.Float32 => 2,
        DataType.Int64 or DataType.UInt64 or DataType.Float64 => 4,
        DataType.String => stringLength,
        _ => throw new ArgumentOutOfRangeException(nameof(type), $"{type} is not supported by Modbus."),
    };
}
