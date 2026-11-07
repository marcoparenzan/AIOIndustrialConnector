// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Groups data points into as few Modbus read requests as possible (unit id + table groups, see <see cref="RangePlanner"/>).
/// </summary>
public static class ModbusReadPlanner
{
    /// <summary>Protocol limit for function codes 3 and 4.</summary>
    public const int ProtocolMaxRegisters = 125;

    /// <summary>Protocol limit for function codes 1 and 2.</summary>
    public const int ProtocolMaxBits = 2000;

    public static IReadOnlyList<ModbusReadBlock> Plan(IEnumerable<ModbusPoint> points, int maxRegistersPerRead, int maxBitsPerRead, int maxGap)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(maxRegistersPerRead, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxRegistersPerRead, ProtocolMaxRegisters);
        ArgumentOutOfRangeException.ThrowIfLessThan(maxBitsPerRead, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(maxBitsPerRead, ProtocolMaxBits);

        return RangePlanner.Plan(
            points,
            groupKey: p => (p.UnitId, p.Address.Table),
            start: p => p.Address.Offset,
            end: p => p.End,
            limit: key => key.Table.IsRegisterTable() ? maxRegistersPerRead : maxBitsPerRead,
            maxGap,
            createBlock: (key, start, count, pts) => new ModbusReadBlock(key.UnitId, key.Table, (ushort)start, (ushort)count, pts),
            nameOf: p => p.Name);
    }
}
