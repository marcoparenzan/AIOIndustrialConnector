// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Core.Sampling;

/// <summary>
/// Strategy: executes one read request of a protocol and stores the decoded values of its data points.
/// </summary>
public interface IBlockReader
{
    /// <exception cref="DeviceRejectedException">The device rejected this request; the others can still succeed.</exception>
    /// <exception cref="DeviceCommunicationException">The connection failed.</exception>
    Task ReadAsync(IReadBlock block, IDictionary<string, object?> values, CancellationToken cancellationToken);
}

/// <summary>Typed base for <see cref="IBlockReader"/> implementations.</summary>
public abstract class BlockReader<TBlock> : IBlockReader where TBlock : class, IReadBlock
{
    public Task ReadAsync(IReadBlock block, IDictionary<string, object?> values, CancellationToken cancellationToken) =>
        ReadBlockAsync((TBlock)block, values, cancellationToken);

    protected abstract Task ReadBlockAsync(TBlock block, IDictionary<string, object?> values, CancellationToken cancellationToken);
}

/// <summary>
/// Samples one asset dataset by running the plan's read blocks through a protocol <see cref="IBlockReader"/> and
/// serializing the result as a flat JSON object (<c>{"dataPointName": value, ...}</c>). Identical for every protocol.
/// </summary>
/// <remarks>
/// <para>The <see cref="PollingTelemetryConnectorWorker"/> calls <see cref="SampleDatasetAsync"/> once per sampling
/// interval and forwards the payload to the dataset destination (MQTT topic or broker state store).</para>
/// <para>If the device rejects a single read request, the points of that request are published as <c>null</c> and the
/// others are still published. If every request is rejected, or the connection fails, sampling throws and the worker
/// reports the error on the device status.</para>
/// </remarks>
public sealed class PolledDatasetSampler : IDatasetSampler
{
    private readonly IDatasetPlan _plan;
    private readonly IBlockReader _reader;
    private readonly string _assetName;
    private readonly ILogger _logger;

    public PolledDatasetSampler(IDatasetPlan plan, IBlockReader reader, string assetName, ILogger logger)
    {
        _plan = plan;
        _reader = reader;
        _assetName = assetName;
        _logger = logger;
    }

    public IDatasetPlan Plan => _plan;

    public Task<TimeSpan> GetSamplingIntervalAsync(AssetDataset dataset, CancellationToken cancellationToken = default) =>
        Task.FromResult(_plan.SamplingInterval);

    public async Task<byte[]> SampleDatasetAsync(AssetDataset dataset, CancellationToken cancellationToken = default)
    {
        Dictionary<string, object?> values;
        try
        {
            values = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DeviceCommunicationException ex)
        {
            // The server may have dropped an idle connection; one immediate retry on a fresh connection
            // avoids reporting a spurious error for that case. A second failure is reported.
            _logger.LogWarning("Sampling dataset {Dataset} of asset {Asset} failed ({Message}); retrying once on a new connection",
                _plan.DatasetName, _assetName, ex.Message);
            values = await ReadAllAsync(cancellationToken).ConfigureAwait(false);
        }

        return PayloadSerializer.Serialize(_plan.Points, values);
    }

    private async Task<Dictionary<string, object?>> ReadAllAsync(CancellationToken cancellationToken)
    {
        var values = new Dictionary<string, object?>(_plan.Points.Count, StringComparer.Ordinal);
        var rejected = new List<DeviceRejectedException>();

        foreach (IReadBlock block in _plan.Blocks)
        {
            try
            {
                await _reader.ReadAsync(block, values, cancellationToken).ConfigureAwait(false);
            }
            catch (DeviceRejectedException ex)
            {
                rejected.Add(ex);
                _logger.LogWarning("Dataset {Dataset} of asset {Asset}: {Block} rejected: {Message}. Its data points are published as null.",
                    _plan.DatasetName, _assetName, block, ex.Message);
                foreach (IDataPoint point in block.Points)
                {
                    values[point.Name] = null;
                }
            }
        }

        if (rejected.Count > 0 && rejected.Count == _plan.Blocks.Count)
        {
            throw new InvalidOperationException(
                $"Every read request of dataset '{_plan.DatasetName}' on asset '{_assetName}' was rejected by the device: {rejected[0].Message}",
                rejected.Count == 1 ? rejected[0] : new AggregateException(rejected));
        }

        return values;
    }
}

/// <summary>
/// Stand-in sampler for a dataset whose configuration is invalid: every sample fails with the configuration error,
/// so that the worker reports it on the device status in Azure Device Registry (visible in the portal).
/// </summary>
public sealed class FaultedDatasetSampler : IDatasetSampler
{
    private readonly Exception _error;

    public FaultedDatasetSampler(Exception error) => _error = error;

    public Task<TimeSpan> GetSamplingIntervalAsync(AssetDataset dataset, CancellationToken cancellationToken = default) =>
        Task.FromResult(TimeSpan.FromSeconds(30));

    public Task<byte[]> SampleDatasetAsync(AssetDataset dataset, CancellationToken cancellationToken = default) =>
        Task.FromException<byte[]>(_error);
}
