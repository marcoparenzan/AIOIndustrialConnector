// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Values;

namespace AioIndustrialConnector.S7;

/// <summary>The CPU family; selects how the connection (TSAP) is addressed.</summary>
public enum S7CpuType
{
    S7200,
    S7200Smart,
    Logo0BA8,
    S7300,
    S7400,
    S71200,
    S71500,
}

/// <summary>
/// S7 settings, read from the stringified JSON fields of the Azure Device Registry resources.
/// Same cascade as every protocol: data point &gt; dataset &gt; asset defaults &gt; device endpoint &gt; built-in defaults.
/// </summary>
public sealed record S7Settings : IProtocolSettings<S7Settings>
{
    /// <summary>Endpoint level: CPU family. Default S71500.</summary>
    public S7CpuType? CpuType { get; init; }

    /// <summary>Endpoint level: rack number. Default 0.</summary>
    public short? Rack { get; init; }

    /// <summary>Endpoint level: slot number. Default 2 for S7300, otherwise 1.</summary>
    public short? Slot { get; init; }

    /// <summary>Endpoint level: response timeout per request, in milliseconds. Default 3000.</summary>
    public int? TimeoutMs { get; init; }

    /// <summary>Endpoint level: TCP/ISO connect timeout, in milliseconds. Default 5000.</summary>
    public int? ConnectTimeoutMs { get; init; }

    /// <summary>Dataset level: sampling interval, in milliseconds. Default 1000.</summary>
    public int? SamplingIntervalMs { get; init; }

    /// <summary>Largest number of bytes read in one request (merged ranges). Default 200.</summary>
    public int? MaxBytesPerRead { get; init; }

    /// <summary>
    /// Largest number of unused bytes that may be read to merge two data points into one request. Default 0.
    /// Reading a gap inside an existing data block is harmless on S7, so a small value saves requests.
    /// </summary>
    public int? MaxGap { get; init; }

    /// <summary>Data point level: how to interpret the raw value. Default from the address width (X Boolean, B Byte, W UInt16, D UInt32).</summary>
    [JsonConverter(typeof(S7DataTypeConverter))]
    public DataType? DataType { get; init; }

    /// <summary>Data point level: maximum number of characters of a String (the declared size of the S7 STRING[n]).</summary>
    public int? Length { get; init; }

    /// <summary>Data point level: extract a single bit (0 = least significant) of a byte/word/double word as a boolean.</summary>
    public int? Bit { get; init; }

    /// <summary>Data point level: published value = raw * Scale + Offset. Setting either makes the value a floating point number.</summary>
    public double? Scale { get; init; }

    /// <summary>Data point level: see <see cref="Scale"/>.</summary>
    public double? Offset { get; init; }

    public static S7Settings Empty { get; } = new();

    public static S7Settings Parse(string? json, string context) =>
        SettingsJson.Parse(json, context, Empty, "S7");

    public S7Settings WithFallback(S7Settings fallback) => new()
    {
        CpuType = CpuType ?? fallback.CpuType,
        Rack = Rack ?? fallback.Rack,
        Slot = Slot ?? fallback.Slot,
        TimeoutMs = TimeoutMs ?? fallback.TimeoutMs,
        ConnectTimeoutMs = ConnectTimeoutMs ?? fallback.ConnectTimeoutMs,
        SamplingIntervalMs = SamplingIntervalMs ?? fallback.SamplingIntervalMs,
        MaxBytesPerRead = MaxBytesPerRead ?? fallback.MaxBytesPerRead,
        MaxGap = MaxGap ?? fallback.MaxGap,
        DataType = DataType ?? fallback.DataType,
        Length = Length ?? fallback.Length,
        Bit = Bit ?? fallback.Bit,
        Scale = Scale ?? fallback.Scale,
        Offset = Offset ?? fallback.Offset,
    };
}

/// <summary>
/// Reads <c>dataType</c> accepting the common names (Boolean, Int16, Float32, ...) and the Siemens ones
/// (Bool, Byte, SInt, Int, DInt, LInt, USInt, UInt, UDInt, ULInt, Word, DWord, LWord, Real, LReal, String), case-insensitive.
/// </summary>
internal sealed class S7DataTypeConverter : JsonConverter<DataType>
{
    private static readonly Dictionary<string, DataType> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Boolean"] = DataType.Boolean, ["Bool"] = DataType.Boolean,
        ["Byte"] = DataType.Byte, ["USInt"] = DataType.Byte,
        ["SByte"] = DataType.SByte, ["SInt"] = DataType.SByte,
        ["Int16"] = DataType.Int16, ["Int"] = DataType.Int16,
        ["UInt16"] = DataType.UInt16, ["UInt"] = DataType.UInt16, ["Word"] = DataType.UInt16,
        ["Int32"] = DataType.Int32, ["DInt"] = DataType.Int32,
        ["UInt32"] = DataType.UInt32, ["UDInt"] = DataType.UInt32, ["DWord"] = DataType.UInt32,
        ["Int64"] = DataType.Int64, ["LInt"] = DataType.Int64,
        ["UInt64"] = DataType.UInt64, ["ULInt"] = DataType.UInt64, ["LWord"] = DataType.UInt64,
        ["Float32"] = DataType.Float32, ["Real"] = DataType.Float32,
        ["Float64"] = DataType.Float64, ["LReal"] = DataType.Float64,
        ["String"] = DataType.String,
    };

    public override DataType Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        string? text = reader.TokenType == JsonTokenType.String ? reader.GetString() : null;
        if (text is not null && Names.TryGetValue(text.Trim(), out DataType type))
        {
            return type;
        }

        throw new JsonException($"Unknown S7 dataType '{text ?? reader.TokenType.ToString()}'. Use one of: {string.Join(", ", Names.Keys)}.");
    }

    public override void Write(Utf8JsonWriter writer, DataType value, JsonSerializerOptions options) =>
        writer.WriteStringValue(value.ToString());
}
