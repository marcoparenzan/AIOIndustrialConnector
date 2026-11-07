// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Connector.Files;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Core.Sampling;

/// <summary>
/// Template method for the <see cref="IDatasetSamplerFactory"/> of a protocol: creates a
/// <see cref="PolledDatasetSampler"/> for every dataset of every asset bound to a device endpoint. Invalid
/// configuration yields a <see cref="FaultedDatasetSampler"/> so the error shows up on the device status.
/// A protocol supplies parsing, planning and how to get a (shared) reader for the endpoint.
/// </summary>
/// <typeparam name="TSettings">The protocol's settings record.</typeparam>
/// <typeparam name="TEndpoint">The protocol's parsed endpoint address.</typeparam>
public abstract class DatasetSamplerFactoryBase<TSettings, TEndpoint> : IDatasetSamplerFactory
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ILogger _logger;

    protected DatasetSamplerFactoryBase(string protocolName, ILoggerFactory loggerFactory)
    {
        ProtocolName = protocolName;
        _loggerFactory = loggerFactory;
        _logger = loggerFactory.CreateLogger(GetType());
    }

    protected string ProtocolName { get; }

    protected abstract TEndpoint ParseEndpoint(string? address);

    protected abstract TSettings ParseSettings(string? json, string context);

    protected abstract IDatasetPlan BuildPlan(AssetDataset dataset, TSettings assetDefaults, TSettings endpointSettings);

    /// <summary>Returns the reader backed by the shared client of this endpoint (one connection per device).</summary>
    /// <exception cref="ConfigurationException">The endpoint settings are invalid.</exception>
    protected abstract IBlockReader CreateReader(TEndpoint endpoint, TSettings endpointSettings);

    public IDatasetSampler CreateDatasetSampler(string deviceName, Device device, string inboundEndpointName, string assetName, Asset asset, AssetDataset dataset, EndpointCredentials? endpointCredentials)
    {
        try
        {
            InboundEndpointSchemaMapValue endpoint = GetInboundEndpoint(deviceName, device, inboundEndpointName);
            TEndpoint address = ParseEndpoint(endpoint.Address);
            TSettings endpointSettings = ParseSettings(endpoint.AdditionalConfiguration, $"endpoint '{inboundEndpointName}' of device '{deviceName}'");
            TSettings assetDefaults = ParseSettings(asset.DefaultDatasetsConfiguration, $"defaultDatasetsConfiguration of asset '{assetName}'");

            if (endpointCredentials?.Username is not null || endpointCredentials?.ClientCertificate is not null)
            {
                _logger.LogWarning("Endpoint {Endpoint} of device {Device} has credentials configured, but {Protocol} has no authentication; they are ignored.",
                    inboundEndpointName, deviceName, ProtocolName);
            }

            IDatasetPlan plan = BuildPlan(dataset, assetDefaults, endpointSettings);
            IBlockReader reader = CreateReader(address, endpointSettings);

            _logger.LogInformation("Asset {Asset}, dataset {Dataset}: {Points} data point(s) from {Endpoint} in {Requests} request(s) every {Interval} ms",
                assetName, dataset.Name, plan.Points.Count, address, plan.Blocks.Count, plan.SamplingInterval.TotalMilliseconds);
            foreach (IReadBlock block in plan.Blocks)
            {
                _logger.LogDebug("  {Block}: {Points}", block, string.Join(", ", block.Points.Select(p => p.Name)));
            }

            return new PolledDatasetSampler(plan, reader, assetName, _loggerFactory.CreateLogger<PolledDatasetSampler>());
        }
        catch (ConfigurationException ex)
        {
            _logger.LogError("Asset {Asset}, dataset {Dataset} cannot be sampled: {Message}", assetName, dataset.Name, ex.Message);
            return new FaultedDatasetSampler(ex);
        }
    }

    public static InboundEndpointSchemaMapValue GetInboundEndpoint(string deviceName, Device device, string inboundEndpointName)
    {
        if (device.Endpoints?.Inbound is { } inbound && inbound.TryGetValue(inboundEndpointName, out InboundEndpointSchemaMapValue? endpoint))
        {
            return endpoint;
        }

        throw new ConfigurationException($"Device '{deviceName}' has no inbound endpoint named '{inboundEndpointName}'.");
    }
}
