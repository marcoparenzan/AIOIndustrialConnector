// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Values;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;

namespace AioIndustrialConnector.S7;

/// <summary>
/// Everything needed to sample one S7 asset dataset: resolved points, read requests and timing.
/// </summary>
public sealed record S7DatasetPlan(
    string DatasetName,
    IReadOnlyList<S7Point> Points,
    IReadOnlyList<S7ReadBlock> Blocks,
    TimeSpan SamplingInterval) : DatasetPlan<S7Point, S7ReadBlock>(DatasetName, Points, Blocks, SamplingInterval)
{
    private static readonly S7Planner Planner = new();

    /// <summary>Resolves and validates every data point of <paramref name="dataset"/> and plans the read requests.</summary>
    /// <exception cref="ConfigurationException">One or more points are invalid; the message lists all problems.</exception>
    public static S7DatasetPlan Build(AssetDataset dataset, S7Settings assetDefaults, S7Settings endpointSettings)
    {
        var plan = Planner.Build(dataset, assetDefaults, endpointSettings);
        return new S7DatasetPlan(plan.DatasetName, plan.Points, plan.Blocks, plan.SamplingInterval);
    }
}

/// <summary>Request-size limits of an S7 dataset.</summary>
public readonly record struct S7Limits(int MaxBytes, int MaxGap);

/// <summary>The S7 part of dataset planning: address and type rules, limits, request grouping.</summary>
internal sealed class S7Planner : DatasetPlanner<S7Settings, S7Point, S7ReadBlock, S7Limits>
{
    public const int DefaultMaxBytesPerRead = 200;
    public const int MaxStringLength = 254;

    protected override S7Limits ResolveLimits(S7Settings s, List<string> errors)
    {
        int maxBytes = s.MaxBytesPerRead ?? DefaultMaxBytesPerRead;
        int maxGap = s.MaxGap ?? 0;
        if (maxBytes < 1)
        {
            errors.Add($"maxBytesPerRead must be at least 1, got {maxBytes}.");
            maxBytes = DefaultMaxBytesPerRead;
        }

        if (maxGap < 0)
        {
            errors.Add($"maxGap must not be negative, got {maxGap}.");
            maxGap = 0;
        }

        return new S7Limits(maxBytes, maxGap);
    }

    protected override void ValidatePoints(IReadOnlyList<S7Point> points, S7Limits limits, List<string> errors)
    {
        foreach (S7Point point in points)
        {
            if (point.Length > limits.MaxBytes)
            {
                errors.Add($"Data point '{point.Name}' spans {point.Length} bytes, more than the {limits.MaxBytes} allowed in one request.");
            }
        }
    }

    protected override IReadOnlyList<S7ReadBlock> PlanBlocks(IReadOnlyList<S7Point> points, S7Limits limits) =>
        S7ReadPlanner.Plan(points, limits.MaxBytes, limits.MaxGap);

    protected override bool TryCreatePoint(string name, string? dataSource, S7Settings s, out S7Point? point, out string? error)
    {
        point = null;
        if (!S7Address.TryParse(dataSource, out S7Address address, out error))
        {
            return false;
        }

        // The address width fixes the natural type; 'B' doubles as the untyped byte offset for 64-bit values and strings.
        DataType dataType = s.DataType ?? address.Width switch
        {
            S7Width.Bit => DataType.Boolean,
            S7Width.Byte => DataType.Byte,
            S7Width.Word => DataType.UInt16,
            _ => DataType.UInt32,
        };

        if (!FitsWidth(address, dataType))
        {
            error = $"dataType {dataType} does not fit the {address.Width} address {address}. "
                + "Use X for Boolean, W for Int16/UInt16, D for Int32/UInt32/Float32, and B for Byte/SByte/String and 64-bit types.";
            return false;
        }

        if (s.Bit is int bit)
        {
            if (address.Width == S7Width.Bit)
            {
                error = "'bit' cannot be combined with a bit address; the bit is part of the address.";
                return false;
            }

            if (dataType != DataType.Boolean)
            {
                error = $"'bit' requires dataType Boolean, got {dataType}.";
                return false;
            }

            int bits = address.WidthBytes * 8;
            if (bit < 0 || bit >= bits)
            {
                error = $"bit must be 0..{bits - 1} for {address}, got {bit}.";
                return false;
            }
        }

        if (dataType is DataType.Boolean or DataType.String && (s.Scale is not null || s.Offset is not null))
        {
            error = $"scale and offset apply only to numeric data types, not to {dataType}.";
            return false;
        }

        if (s.Scale is double scale && !double.IsFinite(scale) || s.Offset is double offset && !double.IsFinite(offset))
        {
            error = "scale and offset must be finite numbers.";
            return false;
        }

        int length;
        if (dataType == DataType.String)
        {
            if (s.Length is not int chars || chars < 1 || chars > MaxStringLength)
            {
                error = $"dataType String requires 'length' (the declared size of STRING[n], 1..{MaxStringLength}).";
                return false;
            }

            length = chars + 2; // S7 STRING header: maximum length, current length
        }
        else if (dataType == DataType.Boolean)
        {
            length = address.WidthBytes;
        }
        else
        {
            length = dataType.ByteSize();
        }

        if (address.ByteOffset + length > 65536)
        {
            error = $"the value at {address} with {length} byte(s) runs past the end of the address space.";
            return false;
        }

        point = new S7Point(name, address, dataType, length, s.Bit, s.Scale, s.Offset);
        error = null;
        return true;
    }

    private static bool FitsWidth(S7Address address, DataType type) => address.Width switch
    {
        S7Width.Bit => type == DataType.Boolean,
        S7Width.Byte => true,
        S7Width.Word => type is DataType.Boolean or DataType.Int16 or DataType.UInt16,
        _ => type is DataType.Boolean or DataType.Int32 or DataType.UInt32 or DataType.Float32,
    };
}

/// <summary>Groups S7 data points into as few read requests as possible (area + data block groups, see <see cref="RangePlanner"/>).</summary>
public static class S7ReadPlanner
{
    public static IReadOnlyList<S7ReadBlock> Plan(IEnumerable<S7Point> points, int maxBytesPerRead, int maxGap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBytesPerRead, 1);

        return RangePlanner.Plan(
            points,
            groupKey: p => (p.Address.Area, p.Address.DbNumber),
            start: p => p.Address.ByteOffset,
            end: p => p.End,
            limit: _ => maxBytesPerRead,
            maxGap,
            createBlock: (key, start, count, pts) => new S7ReadBlock(key.Area, key.DbNumber, start, count, pts),
            nameOf: p => p.Name);
    }
}
