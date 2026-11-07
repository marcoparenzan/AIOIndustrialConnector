// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Buffers;
using System.Text.Json;
using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.Core.Sampling;

/// <summary>Writes decoded values as the flat JSON payload <c>{"dataPointName": value, ...}</c>.</summary>
public static class PayloadSerializer
{
    public static byte[] Serialize(IReadOnlyList<IDataPoint> points, IReadOnlyDictionary<string, object?> values)
    {
        var buffer = new ArrayBufferWriter<byte>(256);
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            foreach (IDataPoint point in points)
            {
                writer.WritePropertyName(point.Name);
                switch (values.GetValueOrDefault(point.Name))
                {
                    case null: writer.WriteNullValue(); break;
                    case bool b: writer.WriteBooleanValue(b); break;
                    case long l: writer.WriteNumberValue(l); break;
                    case ulong u: writer.WriteNumberValue(u); break;
                    case double d: writer.WriteNumberValue(d); break;
                    case string s: writer.WriteStringValue(s); break;
                    case var other: throw new InvalidOperationException($"Unexpected value type {other.GetType()} for data point '{point.Name}'.");
                }
            }

            writer.WriteEndObject();
        }

        return buffer.WrittenSpan.ToArray();
    }
}
