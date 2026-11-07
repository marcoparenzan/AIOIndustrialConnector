// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Planning;
using AioIndustrialConnector.Core.Values;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Everything needed to sample one Modbus asset dataset: resolved points, read requests and timing.
/// </summary>
public sealed record ModbusDatasetPlan(
    string DatasetName,
    IReadOnlyList<ModbusPoint> Points,
    IReadOnlyList<ModbusReadBlock> Blocks,
    TimeSpan SamplingInterval) : DatasetPlan<ModbusPoint, ModbusReadBlock>(DatasetName, Points, Blocks, SamplingInterval)
{
    public const byte DefaultUnitId = 1;
    public const int DefaultSamplingIntervalMs = DatasetPlanner<ModbusSettings, ModbusPoint, ModbusReadBlock, ModbusLimits>.DefaultSamplingIntervalMs;
    public const int MinSamplingIntervalMs = DatasetPlanner<ModbusSettings, ModbusPoint, ModbusReadBlock, ModbusLimits>.MinSamplingIntervalMs;

    private static readonly ModbusPlanner Planner = new();

    /// <summary>
    /// Resolves and validates every data point of <paramref name="dataset"/> and plans the read requests.
    /// </summary>
    /// <param name="dataset">The asset dataset.</param>
    /// <param name="assetDefaults">Parsed <c>defaultDatasetsConfiguration</c> of the asset.</param>
    /// <param name="endpointSettings">Parsed <c>additionalConfiguration</c> of the device inbound endpoint.</param>
    /// <exception cref="ConfigurationException">One or more points are invalid; the message lists all problems.</exception>
    public static ModbusDatasetPlan Build(AssetDataset dataset, ModbusSettings assetDefaults, ModbusSettings endpointSettings)
    {
        var plan = Planner.Build(dataset, assetDefaults, endpointSettings);
        return new ModbusDatasetPlan(plan.DatasetName, plan.Points, plan.Blocks, plan.SamplingInterval);
    }
}

/// <summary>Request-size limits of a Modbus dataset.</summary>
public readonly record struct ModbusLimits(int MaxRegisters, int MaxBits, int MaxGap);

/// <summary>The Modbus part of dataset planning: address and type rules, limits, request grouping.</summary>
internal sealed class ModbusPlanner : DatasetPlanner<ModbusSettings, ModbusPoint, ModbusReadBlock, ModbusLimits>
{
    protected override ModbusLimits ResolveLimits(ModbusSettings s, List<string> errors)
    {
        int maxRegisters = s.MaxRegistersPerRead ?? ModbusReadPlanner.ProtocolMaxRegisters;
        int maxBits = s.MaxBitsPerRead ?? ModbusReadPlanner.ProtocolMaxBits;
        int maxGap = s.MaxGap ?? 0;
        if (maxRegisters is < 1 or > ModbusReadPlanner.ProtocolMaxRegisters)
        {
            errors.Add($"maxRegistersPerRead must be 1..{ModbusReadPlanner.ProtocolMaxRegisters}, got {maxRegisters}.");
            maxRegisters = ModbusReadPlanner.ProtocolMaxRegisters;
        }

        if (maxBits is < 1 or > ModbusReadPlanner.ProtocolMaxBits)
        {
            errors.Add($"maxBitsPerRead must be 1..{ModbusReadPlanner.ProtocolMaxBits}, got {maxBits}.");
            maxBits = ModbusReadPlanner.ProtocolMaxBits;
        }

        if (maxGap < 0)
        {
            errors.Add($"maxGap must not be negative, got {maxGap}.");
            maxGap = 0;
        }

        return new ModbusLimits(maxRegisters, maxBits, maxGap);
    }

    protected override void ValidatePoints(IReadOnlyList<ModbusPoint> points, ModbusLimits limits, List<string> errors)
    {
        foreach (ModbusPoint point in points)
        {
            int limit = point.Address.Table.IsRegisterTable() ? limits.MaxRegisters : limits.MaxBits;
            if (point.Length > limit)
            {
                errors.Add($"Data point '{point.Name}' spans {point.Length} addresses, more than the {limit} allowed in one request.");
            }
        }
    }

    protected override IReadOnlyList<ModbusReadBlock> PlanBlocks(IReadOnlyList<ModbusPoint> points, ModbusLimits limits) =>
        ModbusReadPlanner.Plan(points, limits.MaxRegisters, limits.MaxBits, limits.MaxGap);

    protected override bool TryCreatePoint(string name, string? dataSource, ModbusSettings s, out ModbusPoint? point, out string? error)
    {
        point = null;
        if (!ModbusAddress.TryParse(dataSource, out ModbusAddress address, out error))
        {
            return false;
        }

        bool registerTable = address.Table.IsRegisterTable();
        DataType dataType = s.DataType ?? (registerTable && s.Bit is null ? DataType.UInt16 : DataType.Boolean);

        if (!dataType.IsSupportedByModbus())
        {
            error = $"dataType {dataType} is not supported by Modbus (registers are 16 bit); use Int16/UInt16 with bit or scale.";
            return false;
        }

        if (!registerTable)
        {
            if (dataType != DataType.Boolean)
            {
                error = $"{address.Table} holds single bits; dataType must be Boolean, got {dataType}.";
                return false;
            }

            if (s.Bit is not null)
            {
                error = $"'bit' applies only to registers, not to {address.Table}.";
                return false;
            }
        }

        if (s.Bit is int bit)
        {
            if (bit is < 0 or > 15)
            {
                error = $"bit must be 0..15, got {bit}.";
                return false;
            }

            if (dataType != DataType.Boolean)
            {
                error = $"'bit' requires dataType Boolean, got {dataType}.";
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
        if (!registerTable)
        {
            length = 1;
        }
        else if (dataType == DataType.String)
        {
            if (s.Length is not int stringLength || stringLength < 1)
            {
                error = "dataType String requires 'length' (number of registers, at least 1).";
                return false;
            }

            length = stringLength;
        }
        else
        {
            length = dataType.RegisterCount();
        }

        if (address.Offset + length > 65536)
        {
            error = $"the value at {address} with {length} register(s) runs past the end of the address space.";
            return false;
        }

        point = new ModbusPoint(
            name,
            address,
            s.UnitId ?? ModbusDatasetPlan.DefaultUnitId,
            dataType,
            s.ByteOrder ?? ByteOrder.BigEndian,
            s.WordOrder ?? WordOrder.BigEndian,
            length,
            s.Bit,
            s.Scale,
            s.Offset);
        error = null;
        return true;
    }
}
