// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core.Sampling;

namespace AioIndustrialConnector.S7;

/// <summary>S7 strategy for <see cref="PolledDatasetSampler"/>: reads one byte range and decodes its points.</summary>
public sealed class S7BlockReader : BlockReader<S7ReadBlock>
{
    private readonly IS7Client _client;

    public S7BlockReader(IS7Client client) => _client = client;

    protected override async Task ReadBlockAsync(S7ReadBlock block, IDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        byte[] bytes = await _client.ReadBytesAsync(block.Area, block.DbNumber, block.Start, block.Count, cancellationToken).ConfigureAwait(false);
        if (bytes.Length < block.Count)
        {
            throw new S7CommunicationException($"Short response for {block}: expected {block.Count} bytes, got {bytes.Length}.");
        }

        foreach (S7Point point in block.Points)
        {
            values[point.Name] = S7ValueDecoder.Decode(point, bytes.AsSpan(point.Address.ByteOffset - block.Start, point.Length));
        }
    }
}
