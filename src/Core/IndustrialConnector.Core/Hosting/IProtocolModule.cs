// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using Azure.Iot.Operations.Connector;
using Microsoft.Extensions.DependencyInjection;

namespace AioIndustrialConnector.Core.Hosting;

/// <summary>
/// Facade over one protocol: everything the host needs to know to run the connector in that mode. The host never
/// touches protocol internals (clients, planners, decoders); it asks the module to register its services.
/// </summary>
public interface IProtocolModule
{
    /// <summary>Short mode name used in <c>CONNECTOR_PROTOCOL</c> / <c>--protocol</c>, lower case (e.g. "modbus", "s7").</summary>
    string Name { get; }

    /// <summary>Other accepted spellings of <see cref="Name"/>, lower case.</summary>
    IReadOnlyCollection<string> Aliases { get; }

    /// <summary>The ADR inbound endpoint type served in this mode (e.g. "Custom.ModbusTcp").</summary>
    string EndpointType { get; }

    /// <summary>Lease name for leader election; distinct per protocol so templates never compete for one lock.</summary>
    string LeaderElectionLockName { get; }

    /// <summary>
    /// Registers the protocol's <see cref="IDatasetSamplerFactory"/>, <see cref="IMessageSchemaProvider"/> and
    /// client pool. Everything else (MQTT, ADR, leader election, worker) is registered by the host.
    /// </summary>
    void AddServices(IServiceCollection services);
}
