// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

namespace AioIndustrialConnector.Core.Planning;

/// <summary>
/// Groups data points that live in an addressable range (Modbus table, S7 data block, ...) into as few read requests
/// as possible. Protocol-neutral: the protocol supplies how to group, where a point starts and ends, and how to build a block.
/// </summary>
/// <remarks>
/// Points are grouped by key, sorted by address and merged while the gap of unused addresses between them is at most
/// <c>maxGap</c> and the merged request stays within the per-request limit. Overlapping points share a request.
/// </remarks>
public static class RangePlanner
{
    /// <typeparam name="TPoint">Point type.</typeparam>
    /// <typeparam name="TKey">Group key (what one request can address: unit + table, area + data block, ...).</typeparam>
    /// <typeparam name="TBlock">Block type built by <paramref name="createBlock"/>.</typeparam>
    /// <param name="points">The points to plan.</param>
    /// <param name="groupKey">Selects the group of a point.</param>
    /// <param name="start">First address of a point.</param>
    /// <param name="end">Exclusive end address of a point.</param>
    /// <param name="limit">Largest request size (in addresses) for a group.</param>
    /// <param name="maxGap">Largest number of unused addresses bridged to merge two points.</param>
    /// <param name="createBlock">Builds a block from (key, start, count, points).</param>
    /// <param name="nameOf">Point name, for error messages.</param>
    public static IReadOnlyList<TBlock> Plan<TPoint, TKey, TBlock>(
        IEnumerable<TPoint> points,
        Func<TPoint, TKey> groupKey,
        Func<TPoint, int> start,
        Func<TPoint, int> end,
        Func<TKey, int> limit,
        int maxGap,
        Func<TKey, int, int, IReadOnlyList<TPoint>, TBlock> createBlock,
        Func<TPoint, string> nameOf)
        where TKey : notnull
    {
        ArgumentOutOfRangeException.ThrowIfNegative(maxGap);

        var blocks = new List<TBlock>();

        foreach (var group in points.GroupBy(groupKey).OrderBy(g => g.Key))
        {
            int groupLimit = limit(group.Key);
            int blockStart = -1;
            int blockEnd = -1; // exclusive
            var current = new List<TPoint>();

            foreach (TPoint point in group.OrderBy(start).ThenBy(end))
            {
                int length = end(point) - start(point);
                if (length > groupLimit)
                {
                    throw new ArgumentException($"Data point '{nameOf(point)}' spans {length} addresses, more than the {groupLimit} allowed in one request.");
                }

                bool fits = current.Count > 0
                    && start(point) <= blockEnd + maxGap
                    && Math.Max(blockEnd, end(point)) - blockStart <= groupLimit;

                if (!fits)
                {
                    Flush();
                    blockStart = start(point);
                    blockEnd = end(point);
                }
                else
                {
                    blockEnd = Math.Max(blockEnd, end(point));
                }

                current.Add(point);
            }

            Flush();

            void Flush()
            {
                if (current.Count > 0)
                {
                    blocks.Add(createBlock(group.Key, blockStart, blockEnd - blockStart, current.ToArray()));
                    current.Clear();
                }
            }
        }

        return blocks;
    }
}
