// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Sampling;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.S7;

/// <summary>Creates a sampler for every dataset of every asset bound to a Siemens S7 device endpoint.</summary>
public sealed class S7DatasetSamplerFactory : DatasetSamplerFactoryBase<S7Settings, TcpEndpoint>
{
    private readonly S7ClientPool _pool;

    public S7DatasetSamplerFactory(S7ClientPool pool, ILoggerFactory loggerFactory) : base("Siemens S7", loggerFactory) => _pool = pool;

    protected override TcpEndpoint ParseEndpoint(string? address) => S7Protocol.ParseEndpoint(address);

    protected override S7Settings ParseSettings(string? json, string context) => S7Settings.Parse(json, context);

    protected override IDatasetPlan BuildPlan(AssetDataset dataset, S7Settings assetDefaults, S7Settings endpointSettings) =>
        S7DatasetPlan.Build(dataset, assetDefaults, endpointSettings);

    protected override IBlockReader CreateReader(TcpEndpoint endpoint, S7Settings endpointSettings) =>
        new S7BlockReader(_pool.GetClient(endpoint, endpointSettings));
}
