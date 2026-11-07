// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// One Modbus read request (a contiguous range of one table on one unit) and the data points it serves.
/// </summary>
public sealed record ModbusReadBlock(byte UnitId, ModbusTable Table, ushort Start, ushort Count, IReadOnlyList<ModbusPoint> Points) : IReadBlock
{
    IReadOnlyList<IDataPoint> IReadBlock.Points => Points;

    public override string ToString() => $"unit {UnitId} {Table} [{Start}..{Start + Count - 1}] ({Points.Count} point(s))";
}
