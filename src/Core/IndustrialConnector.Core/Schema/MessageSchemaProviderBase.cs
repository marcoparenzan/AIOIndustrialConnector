// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;
using SchemaFormat = Azure.Iot.Operations.Services.SchemaRegistry.SchemaRegistry.Format;
using SchemaType = Azure.Iot.Operations.Services.SchemaRegistry.SchemaRegistry.SchemaType;

namespace AioIndustrialConnector.Core.Schema;

/// <summary>
/// Template method for the <see cref="IMessageSchemaProvider"/> of a protocol: registers an accurate JSON Schema per
/// dataset in the schema registry (used by data flows and the portal), derived from the same plan the sampler uses.
/// </summary>
public abstract class MessageSchemaProviderBase<TSettings> : IMessageSchemaProvider
{
    private readonly ILogger _logger;

    protected MessageSchemaProviderBase(ILogger logger) => _logger = logger;

    protected abstract TSettings EmptySettings { get; }

    protected abstract TSettings ParseSettings(string? json, string context);

    protected abstract IDatasetPlan BuildPlan(AssetDataset dataset, TSettings assetDefaults, TSettings endpointSettings);

    public Task<ConnectorMessageSchema?> GetMessageSchemaAsync(Device device, Asset asset, string datasetName, AssetDataset dataset, CancellationToken cancellationToken = default)
    {
        try
        {
            TSettings endpointSettings = EmptySettings;
            string? endpointName = asset.DeviceRef?.EndpointName;
            if (endpointName is not null && device.Endpoints?.Inbound?.TryGetValue(endpointName, out var endpoint) == true)
            {
                endpointSettings = ParseSettings(endpoint.AdditionalConfiguration, $"endpoint '{endpointName}'");
            }

            TSettings assetDefaults = ParseSettings(asset.DefaultDatasetsConfiguration, "asset defaultDatasetsConfiguration");
            IDatasetPlan plan = BuildPlan(dataset, assetDefaults, endpointSettings);

            var schema = new ConnectorMessageSchema(JsonSchemaBuilder.Build(plan), SchemaFormat.JsonSchemaDraft07, SchemaType.MessageSchema, "1.0", null);
            return Task.FromResult<ConnectorMessageSchema?>(schema);
        }
        catch (ConfigurationException ex)
        {
            // The sampler reports the configuration error; without a valid plan there is no schema to register.
            _logger.LogWarning("No message schema for dataset {Dataset}: {Message}", datasetName, ex.Message);
            return Task.FromResult<ConnectorMessageSchema?>(null);
        }
    }

    /// <summary>Events are not supported by these connectors.</summary>
    public Task<ConnectorMessageSchema?> GetMessageSchemaAsync(Device device, Asset asset, string eventName, AssetEvent assetEvent, CancellationToken cancellationToken = default) =>
        Task.FromResult<ConnectorMessageSchema?>(null);
}
