// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Configuration;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;

namespace AioIndustrialConnector.Core.Planning;

/// <summary>
/// Template method that turns an asset dataset into a <see cref="DatasetPlan{TPoint,TBlock}"/>:
/// resolves the settings cascade, validates every data point (collecting <i>all</i> errors, because in the portal the
/// user cannot see pod logs), and plans the read requests. A protocol only supplies the three hooks.
/// </summary>
/// <typeparam name="TSettings">The protocol's settings record.</typeparam>
/// <typeparam name="TPoint">The protocol's resolved data point.</typeparam>
/// <typeparam name="TBlock">The protocol's read request.</typeparam>
/// <typeparam name="TLimits">Request-size limits resolved from the dataset settings (validated once, reused for planning).</typeparam>
public abstract class DatasetPlanner<TSettings, TPoint, TBlock, TLimits>
    where TSettings : class, IProtocolSettings<TSettings>
    where TPoint : class, IDataPoint
    where TBlock : class, IReadBlock
{
    public const int DefaultSamplingIntervalMs = 1000;
    public const int MinSamplingIntervalMs = 10;

    /// <exception cref="ConfigurationException">One or more points are invalid; the message lists all problems.</exception>
    public DatasetPlan<TPoint, TBlock> Build(AssetDataset dataset, TSettings assetDefaults, TSettings endpointSettings)
    {
        TSettings datasetSettings = TSettings
            .Parse(dataset.DatasetConfiguration, $"dataset '{dataset.Name}'")
            .WithFallback(assetDefaults)
            .WithFallback(endpointSettings);

        var errors = new List<string>();
        var points = new List<TPoint>();
        var names = new HashSet<string>(StringComparer.Ordinal);

        int samplingMs = datasetSettings.SamplingIntervalMs ?? DefaultSamplingIntervalMs;
        if (samplingMs < MinSamplingIntervalMs)
        {
            errors.Add($"samplingIntervalMs must be at least {MinSamplingIntervalMs}, got {samplingMs}.");
            samplingMs = DefaultSamplingIntervalMs;
        }

        TLimits limits = ResolveLimits(datasetSettings, errors);

        if (dataset.DataPoints is null || dataset.DataPoints.Count == 0)
        {
            errors.Add("The dataset has no data points.");
        }

        foreach (AssetDatasetDataPoint dataPoint in dataset.DataPoints ?? [])
        {
            string label = string.IsNullOrWhiteSpace(dataPoint.Name) ? $"<unnamed, dataSource '{dataPoint.DataSource}'>" : $"'{dataPoint.Name}'";

            if (string.IsNullOrWhiteSpace(dataPoint.Name))
            {
                errors.Add($"Data point {label} has no name.");
                continue;
            }

            if (!names.Add(dataPoint.Name))
            {
                errors.Add($"Data point name {label} is used more than once.");
                continue;
            }

            TSettings pointSettings;
            try
            {
                pointSettings = TSettings
                    .Parse(dataPoint.DataPointConfiguration, $"data point {label}")
                    .WithFallback(datasetSettings);
            }
            catch (ConfigurationException ex)
            {
                errors.Add(ex.Message);
                continue;
            }

            if (TryCreatePoint(dataPoint.Name, dataPoint.DataSource, pointSettings, out TPoint? point, out string? error))
            {
                points.Add(point!);
            }
            else
            {
                errors.Add($"Data point {label}: {error}");
            }
        }

        ValidatePoints(points, limits, errors);

        if (errors.Count > 0)
        {
            throw new ConfigurationException(
                $"Dataset '{dataset.Name}' has {errors.Count} configuration error(s): " + string.Join(" | ", errors));
        }

        return new DatasetPlan<TPoint, TBlock>(dataset.Name, points, PlanBlocks(points, limits), TimeSpan.FromMilliseconds(samplingMs));
    }

    /// <summary>Reads and validates the request-size limits of the dataset (add problems to <paramref name="errors"/>).</summary>
    protected abstract TLimits ResolveLimits(TSettings datasetSettings, List<string> errors);

    /// <summary>Resolves one data point from its <c>dataSource</c> and fully merged settings; returns false with an error message otherwise.</summary>
    protected abstract bool TryCreatePoint(string name, string? dataSource, TSettings settings, out TPoint? point, out string? error);

    /// <summary>Cross-point validation, e.g. a point larger than one request can carry.</summary>
    protected virtual void ValidatePoints(IReadOnlyList<TPoint> points, TLimits limits, List<string> errors) { }

    /// <summary>Groups the points into read requests.</summary>
    protected abstract IReadOnlyList<TBlock> PlanBlocks(IReadOnlyList<TPoint> points, TLimits limits);
}
