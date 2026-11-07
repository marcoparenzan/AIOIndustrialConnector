// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using AioIndustrialConnector.Core;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Connectivity;
using Microsoft.Extensions.Logging;
using global::S7.Net;
using NetDataType = global::S7.Net.DataType;

namespace AioIndustrialConnector.S7;

/// <summary>
/// <see cref="IS7Client"/> built on S7netplus. Connection lifecycle, request serialization, timeouts and error
/// mapping come from <see cref="SerializedConnection{TConnection}"/>.
/// </summary>
/// <remarks>
/// S7netplus splits reads larger than the negotiated PDU into several requests by itself, so a block may be bigger
/// than one PDU.
/// </remarks>
public sealed class S7NetClient : SerializedConnection<Plc>, IS7Client
{
    private readonly TcpEndpoint _endpoint;
    private readonly S7CpuType _cpuType;
    private readonly short _rack;
    private readonly short _slot;
    private readonly TimeSpan _connectTimeout;

    public S7NetClient(TcpEndpoint endpoint, S7CpuType cpuType, short rack, short slot, TimeSpan connectTimeout, TimeSpan requestTimeout, ILogger logger)
        : base($"S7 PLC {endpoint} (rack {rack}, slot {slot})", requestTimeout, logger)
    {
        _endpoint = endpoint;
        _cpuType = cpuType;
        _rack = rack;
        _slot = slot;
        _connectTimeout = connectTimeout;
    }

    public Task<byte[]> ReadBytesAsync(S7Area area, int dbNumber, int start, int count, CancellationToken cancellationToken)
    {
        NetDataType type = area switch
        {
            S7Area.DataBlock => NetDataType.DataBlock,
            S7Area.Memory => NetDataType.Memory,
            S7Area.Input => NetDataType.Input,
            S7Area.Output => NetDataType.Output,
            _ => throw new ArgumentOutOfRangeException(nameof(area)),
        };

        // Only data blocks carry a block number; the others must be sent with 0.
        int db = area == S7Area.DataBlock ? dbNumber : 0;
        return ExecuteAsync(plc => plc.ReadBytesAsync(type, db, start, count, cancellationToken),
            $"read {count} byte(s) at {Describe(area, dbNumber)}.{start}", cancellationToken);
    }

    protected override async Task<Plc> ConnectAsync(CancellationToken cancellationToken)
    {
        var plc = new Plc(Enum.Parse<global::S7.Net.CpuType>(_cpuType.ToString()), _endpoint.Host, _endpoint.Port, _rack, _slot)
        {
            ReadTimeout = (int)RequestTimeout.TotalMilliseconds,
            WriteTimeout = (int)RequestTimeout.TotalMilliseconds,
        };

        try
        {
            await plc.OpenAsync(cancellationToken).WaitAsync(_connectTimeout, cancellationToken).ConfigureAwait(false);
            return plc;
        }
        catch (TimeoutException)
        {
            Discard(plc);
            throw new S7CommunicationException($"Connecting to S7 PLC {_endpoint} timed out after {_connectTimeout.TotalMilliseconds} ms.");
        }
        catch (OperationCanceledException)
        {
            Discard(plc);
            throw;
        }
        catch (Exception ex)
        {
            Discard(plc);
            throw new S7CommunicationException($"Cannot connect to S7 PLC {_endpoint} (rack {_rack}, slot {_slot}): {ex.Message}", ex);
        }
    }

    protected override bool IsConnected(Plc plc) => plc.IsConnected;

    protected override void Close(Plc plc) => Discard(plc);

    protected override DeviceRejectedException? AsRejection(Exception exception, string description) =>
        S7ErrorClassifier.IsRejection(exception)
            ? new S7DeviceException($"S7 PLC {_endpoint} rejected '{description}': {exception.Message}", exception)
            : null;

    private void Discard(Plc plc)
    {
        try
        {
            plc.Close();
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "Ignoring error while closing the S7 connection to {Endpoint}", _endpoint);
        }
    }

    private static string Describe(S7Area area, int db) => area switch
    {
        S7Area.DataBlock => $"DB{db}",
        S7Area.Memory => "M",
        S7Area.Input => "I",
        _ => "Q",
    };
}

/// <summary>Decides whether an S7netplus exception means "the PLC refused this request" (connection fine) or a transport failure.</summary>
/// <remarks>
/// S7netplus reports a per-item error code of the PLC (object does not exist, address out of range, access denied, ...)
/// as a plain <see cref="Exception"/> whose message starts with "Received error from PLC". The response was read
/// completely, so the connection stays usable. Everything else (timeouts, IO errors, malformed responses) resets the connection.
/// </remarks>
internal static class S7ErrorClassifier
{
    private const string PlcErrorPrefix = "Received error from PLC";

    public static bool IsRejection(Exception exception) =>
        exception.GetType() == typeof(Exception) && exception.Message.StartsWith(PlcErrorPrefix, StringComparison.Ordinal);
}
