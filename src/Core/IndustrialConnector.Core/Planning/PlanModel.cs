// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.Core.Planning;

/// <summary>JSON Schema primitive type of a published data point.</summary>
public enum JsonValueType
{
    Boolean,
    Integer,
    Number,
    String,
}

/// <summary>A resolved, validated data point of any protocol.</summary>
public interface IDataPoint
{
    /// <summary>Property name in the published JSON payload.</summary>
    string Name { get; }

    /// <summary>JSON Schema type of the published value.</summary>
    JsonValueType JsonType { get; }

    /// <summary>Human-readable origin of the value, used as the schema description (e.g. "Float32 at hr:0 (unit 1)").</summary>
    string Describe();
}

/// <summary>One read request to the device and the data points it serves.</summary>
public interface IReadBlock
{
    IReadOnlyList<IDataPoint> Points { get; }
}

/// <summary>Everything needed to sample one asset dataset and to describe its payload.</summary>
public interface IDatasetPlan
{
    string DatasetName { get; }

    TimeSpan SamplingInterval { get; }

    IReadOnlyList<IDataPoint> Points { get; }

    IReadOnlyList<IReadBlock> Blocks { get; }
}

/// <summary>Standard <see cref="IDatasetPlan"/> with protocol-typed points and blocks.</summary>
public record DatasetPlan<TPoint, TBlock>(
    string DatasetName,
    IReadOnlyList<TPoint> Points,
    IReadOnlyList<TBlock> Blocks,
    TimeSpan SamplingInterval) : IDatasetPlan
    where TPoint : class, IDataPoint
    where TBlock : class, IReadBlock
{
    IReadOnlyList<IDataPoint> IDatasetPlan.Points => Points;

    IReadOnlyList<IReadBlock> IDatasetPlan.Blocks => Blocks;
}
