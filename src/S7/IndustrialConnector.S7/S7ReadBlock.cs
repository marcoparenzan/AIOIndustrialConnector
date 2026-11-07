// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Planning;

namespace AioIndustrialConnector.S7;

/// <summary>
/// One S7 read request (a contiguous byte range of one area / data block) and the data points it serves.
/// </summary>
public sealed record S7ReadBlock(S7Area Area, int DbNumber, int Start, int Count, IReadOnlyList<S7Point> Points) : IReadBlock
{
    IReadOnlyList<IDataPoint> IReadBlock.Points => Points;

    public override string ToString()
    {
        string area = Area switch
        {
            S7Area.DataBlock => $"DB{DbNumber}",
            S7Area.Memory => "M",
            S7Area.Input => "I",
            _ => "Q",
        };

        return $"{area} bytes [{Start}..{Start + Count - 1}] ({Points.Count} point(s))";
    }
}
