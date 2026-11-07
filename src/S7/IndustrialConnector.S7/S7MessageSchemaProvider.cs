// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Schema;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.S7;

/// <summary>Registers the JSON Schema of each S7 dataset payload.</summary>
public sealed class S7MessageSchemaProvider : MessageSchemaProviderBase<S7Settings>
{
    public S7MessageSchemaProvider(ILogger<S7MessageSchemaProvider> logger) : base(logger) { }

    protected override S7Settings EmptySettings => S7Settings.Empty;

    protected override S7Settings ParseSettings(string? json, string context) => S7Settings.Parse(json, context);

    protected override IDatasetPlan BuildPlan(AssetDataset dataset, S7Settings assetDefaults, S7Settings endpointSettings) =>
        S7DatasetPlan.Build(dataset, assetDefaults, endpointSettings);
}
