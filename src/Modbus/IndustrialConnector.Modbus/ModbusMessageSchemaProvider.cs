// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Schema;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Modbus;

/// <summary>Registers the JSON Schema of each Modbus dataset payload.</summary>
public sealed class ModbusMessageSchemaProvider : MessageSchemaProviderBase<ModbusSettings>
{
    public ModbusMessageSchemaProvider(ILogger<ModbusMessageSchemaProvider> logger) : base(logger) { }

    protected override ModbusSettings EmptySettings => ModbusSettings.Empty;

    protected override ModbusSettings ParseSettings(string? json, string context) => ModbusSettings.Parse(json, context);

    protected override IDatasetPlan BuildPlan(AssetDataset dataset, ModbusSettings assetDefaults, ModbusSettings endpointSettings) =>
        ModbusDatasetPlan.Build(dataset, assetDefaults, endpointSettings);
}
