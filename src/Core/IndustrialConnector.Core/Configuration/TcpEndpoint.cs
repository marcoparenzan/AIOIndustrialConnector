// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.Core.Configuration;

/// <summary>Network address (host and port) of a device, parsed from the inbound endpoint <c>address</c>.</summary>
public readonly record struct TcpEndpoint(string Host, int Port)
{
    public override string ToString() => Host.Contains(':') ? $"[{Host}]:{Port}" : $"{Host}:{Port}";

    /// <summary>Parses <c>scheme://host:port</c>, <c>host:port</c> or <c>host</c> (IPv6 literals bracketed).</summary>
    /// <param name="address">The endpoint address.</param>
    /// <param name="protocolName">For error messages, e.g. "Modbus".</param>
    /// <param name="defaultScheme">Scheme assumed when the address has none.</param>
    /// <param name="acceptedSchemes">Lower-case schemes accepted.</param>
    /// <param name="defaultPort">Port used when the address has none.</param>
    /// <exception cref="ConfigurationException">The address is missing, malformed or has another scheme.</exception>
    public static TcpEndpoint Parse(string? address, string protocolName, string defaultScheme, IReadOnlyCollection<string> acceptedSchemes, int defaultPort)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            throw new ConfigurationException("The device inbound endpoint has no address.");
        }

        string s = address.Trim();
        if (!s.Contains("://", StringComparison.Ordinal))
        {
            s = defaultScheme + "://" + s;
        }

        if (!Uri.TryCreate(s, UriKind.Absolute, out Uri? uri) || string.IsNullOrEmpty(uri.Host))
        {
            throw new ConfigurationException($"Invalid {protocolName} endpoint address '{address}'. Expected e.g. '{defaultScheme}://host:{defaultPort}'.");
        }

        if (!acceptedSchemes.Contains(uri.Scheme.ToLowerInvariant()))
        {
            throw new ConfigurationException($"Unsupported scheme '{uri.Scheme}' in endpoint address '{address}'. Only {protocolName} is supported (use '{defaultScheme}://').");
        }

        // Unknown schemes have no default port, so Uri reports -1 when none is given.
        int port = uri.Port <= 0 ? defaultPort : uri.Port;
        return new TcpEndpoint(uri.Host.Trim('[', ']'), port);
    }
}
