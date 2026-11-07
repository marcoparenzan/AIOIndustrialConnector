// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Text.Json;
using System.Text.Json.Serialization;

namespace AioIndustrialConnector.Core.Configuration;

/// <summary>
/// Protocol settings read from the stringified JSON fields of the Azure Device Registry resources. The same shape is
/// accepted at every level and a more specific level overrides a more generic one:
/// data point &gt; dataset &gt; asset defaults &gt; device endpoint &gt; built-in defaults.
/// </summary>
public interface IProtocolSettings<TSelf> where TSelf : IProtocolSettings<TSelf>
{
    static abstract TSelf Empty { get; }

    /// <summary>Dataset level: sampling interval in milliseconds.</summary>
    int? SamplingIntervalMs { get; }

    /// <summary>Returns a copy where every property not set here is taken from <paramref name="fallback"/>.</summary>
    TSelf WithFallback(TSelf fallback);

    /// <summary>Parses a stringified JSON configuration; null, empty and <c>{}</c> give <see cref="Empty"/>.</summary>
    /// <exception cref="ConfigurationException">The JSON is malformed or has invalid values.</exception>
    static abstract TSelf Parse(string? json, string context);
}

/// <summary>Shared JSON reading of protocol settings.</summary>
public static class SettingsJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    /// <summary>Deserializes <paramref name="json"/>, returning <paramref name="empty"/> for null, blank or <c>null</c> JSON.</summary>
    /// <exception cref="ConfigurationException">The JSON is malformed or has invalid values.</exception>
    public static T Parse<T>(string? json, string context, T empty, string protocolName) where T : class
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return empty;
        }

        try
        {
            return JsonSerializer.Deserialize<T>(json, Options) ?? empty;
        }
        catch (JsonException ex)
        {
            throw new ConfigurationException($"Invalid {protocolName} configuration in {context}: {ex.Message}", ex);
        }
    }
}
