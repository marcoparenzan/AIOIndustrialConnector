// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Sampling;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// Modbus strategy for <see cref="PolledDatasetSampler"/>: reads one block (registers or bits) and decodes its points.
/// </summary>
public sealed class ModbusBlockReader : BlockReader<ModbusReadBlock>
{
    private readonly IModbusClient _client;

    public ModbusBlockReader(IModbusClient client) => _client = client;

    protected override async Task ReadBlockAsync(ModbusReadBlock block, IDictionary<string, object?> values, CancellationToken cancellationToken)
    {
        if (block.Table.IsRegisterTable())
        {
            ushort[] registers = await _client.ReadRegistersAsync(block.UnitId, block.Table, block.Start, block.Count, cancellationToken).ConfigureAwait(false);
            EnsureLength(block, registers.Length);
            foreach (ModbusPoint point in block.Points)
            {
                values[point.Name] = ModbusValueDecoder.DecodeRegisters(point, registers.AsSpan(point.Address.Offset - block.Start, point.Length));
            }
        }
        else
        {
            bool[] bits = await _client.ReadBitsAsync(block.UnitId, block.Table, block.Start, block.Count, cancellationToken).ConfigureAwait(false);
            EnsureLength(block, bits.Length);
            foreach (ModbusPoint point in block.Points)
            {
                values[point.Name] = ModbusValueDecoder.DecodeBit(bits[point.Address.Offset - block.Start]);
            }
        }
    }

    private static void EnsureLength(ModbusReadBlock block, int received)
    {
        // Bit reads come back padded to whole bytes by some stacks, so accept "at least".
        if (received < block.Count)
        {
            throw new ModbusCommunicationException($"Short response for {block}: expected {block.Count} values, got {received}.");
        }
    }
}
