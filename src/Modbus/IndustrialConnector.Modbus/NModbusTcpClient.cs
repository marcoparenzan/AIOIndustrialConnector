// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Net.Sockets;
using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Connectivity;
using Microsoft.Extensions.Logging;
using NModbus;

namespace AioIndustrialConnector.Modbus;

/// <summary>
/// <see cref="IModbusClient"/> over Modbus TCP, built on NModbus. Connection lifecycle, request serialization,
/// timeouts and error mapping come from <see cref="SerializedConnection{TConnection}"/>.
/// </summary>
public sealed class NModbusTcpClient : SerializedConnection<ModbusLink>, IModbusClient
{
    private static readonly ModbusFactory Factory = new();

    private readonly TcpEndpoint _endpoint;
    private readonly TimeSpan _connectTimeout;

    public NModbusTcpClient(TcpEndpoint endpoint, TimeSpan connectTimeout, TimeSpan requestTimeout, ILogger logger)
        : base($"Modbus TCP server {endpoint}", requestTimeout, logger)
    {
        _endpoint = endpoint;
        _connectTimeout = connectTimeout;
    }

    public TcpEndpoint Endpoint => _endpoint;

    public Task<ushort[]> ReadRegistersAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken) =>
        table switch
        {
            ModbusTable.HoldingRegister => ExecuteAsync(l => l.Master.ReadHoldingRegistersAsync(unitId, start, count), $"read holding registers {start}+{count} on unit {unitId}", cancellationToken),
            ModbusTable.InputRegister => ExecuteAsync(l => l.Master.ReadInputRegistersAsync(unitId, start, count), $"read input registers {start}+{count} on unit {unitId}", cancellationToken),
            _ => throw new ArgumentException($"{table} is not a register table.", nameof(table)),
        };

    public Task<bool[]> ReadBitsAsync(byte unitId, ModbusTable table, ushort start, ushort count, CancellationToken cancellationToken) =>
        table switch
        {
            ModbusTable.Coil => ExecuteAsync(l => l.Master.ReadCoilsAsync(unitId, start, count), $"read coils {start}+{count} on unit {unitId}", cancellationToken),
            ModbusTable.DiscreteInput => ExecuteAsync(l => l.Master.ReadInputsAsync(unitId, start, count), $"read discrete inputs {start}+{count} on unit {unitId}", cancellationToken),
            _ => throw new ArgumentException($"{table} is not a bit table.", nameof(table)),
        };

    protected override async Task<ModbusLink> ConnectAsync(CancellationToken cancellationToken)
    {
        var tcp = new TcpClient { NoDelay = true };
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_connectTimeout);
            await tcp.ConnectAsync(_endpoint.Host, _endpoint.Port, timeout.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            tcp.Dispose();
            throw new ModbusCommunicationException($"Connecting to Modbus TCP server {_endpoint} timed out after {_connectTimeout.TotalMilliseconds} ms.");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            tcp.Dispose();
            throw new ModbusCommunicationException($"Cannot connect to Modbus TCP server {_endpoint}: {ex.Message}", ex);
        }

        IModbusMaster master = Factory.CreateMaster(tcp);
        master.Transport.ReadTimeout = (int)RequestTimeout.TotalMilliseconds;
        master.Transport.WriteTimeout = (int)RequestTimeout.TotalMilliseconds;
        master.Transport.Retries = 0; // retries are the sampler's business, not the transport's
        return new ModbusLink(tcp, master);
    }

    protected override bool IsConnected(ModbusLink link) => link.Tcp.Connected;

    protected override void Close(ModbusLink link)
    {
        try
        {
            link.Master.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Ignoring error while disposing the Modbus master for {Endpoint}", _endpoint);
        }

        try
        {
            link.Tcp.Dispose();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Ignoring error while closing the TCP connection to {Endpoint}", _endpoint);
        }
    }

    protected override DeviceRejectedException? AsRejection(Exception exception, string description) =>
        exception is SlaveException ex
            ? new ModbusDeviceException(ex.SlaveExceptionCode, $"Modbus device {_endpoint} rejected '{description}': exception code {ex.SlaveExceptionCode} ({Describe(ex.SlaveExceptionCode)}).", ex)
            : null;

    private static string Describe(byte code) => code switch
    {
        1 => "illegal function",
        2 => "illegal data address",
        3 => "illegal data value",
        4 => "server device failure",
        5 => "acknowledge",
        6 => "server device busy",
        8 => "memory parity error",
        10 => "gateway path unavailable",
        11 => "gateway target device failed to respond",
        _ => "unknown",
    };
}

/// <summary>The TCP socket together with the NModbus master built on it.</summary>
public sealed record ModbusLink(TcpClient Tcp, IModbusMaster Master);
