// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Hosting;
using Azure.Iot.Operations.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Facade of the Modbus TCP protocol: the host registers this one object and needs nothing else from the library.
/// </summary>
public sealed class ModbusProtocol : IProtocolModule
{
    public const int DefaultPort = 502;

    private static readonly string[] Schemes = ["modbus-tcp", "modbus", "tcp", "modbustcp"];

    public string Name => "modbus";

    public IReadOnlyCollection<string> Aliases { get; } = ["modbus-tcp", "modbustcp"];

    public string EndpointType => "Custom.ModbusTcp";

    public string LeaderElectionLockName => "aio-modbus-connector-leader";

    public void AddServices(IServiceCollection services)
    {
        services.AddSingleton<ModbusClientPool>();
        services.AddSingleton<IDatasetSamplerFactory>(sp => new ModbusDatasetSamplerFactory(
            sp.GetRequiredService<ModbusClientPool>(), sp.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>()));
        services.AddSingleton<IMessageSchemaProvider>(sp => new ModbusMessageSchemaProvider(
            sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<ModbusMessageSchemaProvider>>()));
    }

    /// <summary>
    /// Parses the endpoint address. Accepted forms: <c>modbus-tcp://host:502</c>, <c>modbus://host:502</c>, <c>tcp://host:502</c>,
    /// <c>host:502</c>, <c>host</c> (port defaults to 502); IPv6 literals bracketed.
    /// </summary>
    /// <exception cref="Core.ConfigurationException">The address is missing, malformed or not Modbus TCP.</exception>
    public static TcpEndpoint ParseEndpoint(string? address) =>
        TcpEndpoint.Parse(address, "Modbus TCP", "modbus-tcp", Schemes, DefaultPort);
}
