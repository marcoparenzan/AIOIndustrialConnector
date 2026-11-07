// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.Core.Values;

/// <summary>How the raw bits or bytes of a data point are interpreted. Each protocol supports a subset.</summary>
public enum DataType
{
    Boolean,
    Byte,
    SByte,
    Int16,
    UInt16,
    Int32,
    UInt32,
    Int64,
    UInt64,
    Float32,
    Float64,

    /// <summary>Text; the framing (ASCII in registers, S7 STRING header, ...) is protocol specific.</summary>
    String,
}

public static class DataTypeExtensions
{
    /// <summary>Size in bytes of a fixed-size numeric type; 0 for <see cref="DataType.Boolean"/> and <see cref="DataType.String"/>.</summary>
    public static int ByteSize(this DataType type) => type switch
    {
        DataType.Byte or DataType.SByte => 1,
        DataType.Int16 or DataType.UInt16 => 2,
        DataType.Int32 or DataType.UInt32 or DataType.Float32 => 4,
        DataType.Int64 or DataType.UInt64 or DataType.Float64 => 8,
        _ => 0,
    };

    /// <summary>The JSON Schema type of a published value of this type.</summary>
    public static JsonValueType ToJsonType(this DataType type, bool scaled) => type switch
    {
        DataType.Boolean => JsonValueType.Boolean,
        DataType.String => JsonValueType.String,
        DataType.Float32 or DataType.Float64 => JsonValueType.Number,
        _ when scaled => JsonValueType.Number,
        _ => JsonValueType.Integer,
    };
}
