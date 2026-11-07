// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Sampling;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Modbus;

/// <summary>Creates a sampler for every dataset of every asset bound to a Modbus device endpoint.</summary>
public sealed class ModbusDatasetSamplerFactory : DatasetSamplerFactoryBase<ModbusSettings, TcpEndpoint>
{
    private readonly ModbusClientPool _pool;

    public ModbusDatasetSamplerFactory(ModbusClientPool pool, ILoggerFactory loggerFactory) : base("Modbus TCP", loggerFactory) => _pool = pool;

    protected override TcpEndpoint ParseEndpoint(string? address) => ModbusProtocol.ParseEndpoint(address);

    protected override ModbusSettings ParseSettings(string? json, string context) => ModbusSettings.Parse(json, context);

    protected override IDatasetPlan BuildPlan(AssetDataset dataset, ModbusSettings assetDefaults, ModbusSettings endpointSettings) =>
        ModbusDatasetPlan.Build(dataset, assetDefaults, endpointSettings);

    protected override IBlockReader CreateReader(TcpEndpoint endpoint, ModbusSettings endpointSettings) =>
        new ModbusBlockReader(_pool.GetClient(endpoint, endpointSettings));
}
