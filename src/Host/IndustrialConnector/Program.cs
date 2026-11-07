// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

// Azure IoT Operations industrial connector. One executable, one protocol per instance.
// The Akri operator runs this process in a pod created from a ConnectorTemplate; the SDK's
// PollingTelemetryConnectorWorker discovers devices and assets from Azure Device Registry and calls the
// protocol's dataset sampler on every sampling interval.
//
// Mode: --protocol <modbus|s7> or the CONNECTOR_PROTOCOL environment variable (the container images set it).

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Hosting;
using AioIndustrialConnector.Modbus;
using AioIndustrialConnector.S7;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Protocol;

var catalog = new ProtocolCatalog([new ModbusProtocol(), new S7Protocol()]);

IProtocolModule protocol;
try
{
    protocol = catalog.Select(args, Environment.GetEnvironmentVariable);
}
catch (ConfigurationException ex)
{
    Console.Error.WriteLine(ex.Message);
    return 2;
}

IHost host = Host.CreateDefaultBuilder(args)
    .ConfigureServices(services =>
    {
        // Common to every protocol.
        services.AddSingleton<ApplicationContext>();
        services.AddSingleton(MqttSessionClientProvider.Factory);
        services.AddSingleton<IConnectorLeaderElectionConfigurationProvider>(new LeaderElectionConfigurationProvider(protocol.LeaderElectionLockName));
        services.AddSingleton<IAzureDeviceRegistryClientWrapperProvider>(AzureDeviceRegistryClientWrapperProvider.Factory);

        // The selected protocol: its sampler factory, schema provider and client pool.
        protocol.AddServices(services);

        services.AddHostedService<PollingTelemetryConnectorWorker>();
    })
    .Build();

host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("IndustrialConnector")
    .LogInformation("Starting in {Protocol} mode (endpoint type {EndpointType})", protocol.Name, protocol.EndpointType);

host.Run();
return 0;
