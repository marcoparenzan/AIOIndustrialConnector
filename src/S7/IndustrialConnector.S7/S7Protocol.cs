// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Hosting;
using Azure.Iot.Operations.Connector;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.S7;

/// <summary>
/// Facade of the Siemens S7 protocol: the host registers this one object and needs nothing else from the library.
/// </summary>
public sealed class S7Protocol : IProtocolModule
{
    /// <summary>ISO-on-TCP (RFC 1006) port of S7 PLCs.</summary>
    public const int DefaultPort = 102;

    private static readonly string[] Schemes = ["s7", "s7-tcp", "siemens-s7", "tcp"];

    public string Name => "s7";

    public IReadOnlyCollection<string> Aliases { get; } = ["siemens", "siemens-s7", "s7-tcp"];

    public string EndpointType => "Custom.SiemensS7";

    public string LeaderElectionLockName => "aio-s7-connector-leader";

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<S7ClientPool>();
        services.AddSingleton<IDatasetSamplerFactory>(sp => new S7DatasetSamplerFactory(
            sp.GetRequiredService<S7ClientPool>(), sp.GetRequiredService<ILoggerFactory>()));
        services.AddSingleton<IMessageSchemaProvider>(sp => new S7MessageSchemaProvider(
            sp.GetRequiredService<ILogger<S7MessageSchemaProvider>>()));
    }

    /// <summary>
    /// Parses the endpoint address. Accepted forms: <c>s7://host:102</c>, <c>s7-tcp://host</c>, <c>tcp://host:102</c>,
    /// <c>host:102</c>, <c>host</c> (port defaults to 102); IPv6 literals bracketed.
    /// </summary>
    /// <exception cref="Core.ConfigurationException">The address is missing, malformed or not S7.</exception>
    public static TcpEndpoint ParseEndpoint(string? address) =>
        TcpEndpoint.Parse(address, "Siemens S7", "s7", Schemes, DefaultPort);
}
