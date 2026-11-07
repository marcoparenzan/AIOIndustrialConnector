// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using Microsoft.Extensions.Logging;

namespace AioIndustrialConnector.Core.Connectivity;

/// <summary>
/// Base for a protocol client that owns one device connection: opened lazily, re-opened after any failure, and
/// used by one request at a time (industrial devices accept only a few connections and process one transaction at
/// a time per connection, and the protocol libraries are not thread safe).
/// </summary>
/// <typeparam name="TConnection">The protocol library's connection object (NModbus master, S7 Plc, ...).</typeparam>
/// <remarks>
/// Protocol libraries rarely take a <see cref="CancellationToken"/>. A request is bounded by the transport timeout; on
/// cancellation the pending request is abandoned and the connection is closed, so a late response cannot be matched
/// to a later request.
/// </remarks>
public abstract class SerializedConnection<TConnection> : IAsyncDisposable where TConnection : class
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TConnection? _connection;
    private bool _disposed;

    protected SerializedConnection(string endpointText, TimeSpan requestTimeout, ILogger logger)
    {
        EndpointText = endpointText;
        RequestTimeout = requestTimeout;
        Logger = logger;
    }

    protected string EndpointText { get; }

    protected TimeSpan RequestTimeout { get; }

    protected ILogger Logger { get; }

    /// <summary>Opens a connection. Throw <see cref="DeviceCommunicationException"/> on failure.</summary>
    protected abstract Task<TConnection> ConnectAsync(CancellationToken cancellationToken);

    /// <summary>True while <paramref name="connection"/> is still usable.</summary>
    protected abstract bool IsConnected(TConnection connection);

    /// <summary>Closes <paramref name="connection"/>; must not throw.</summary>
    protected abstract void Close(TConnection connection);

    /// <summary>Maps a protocol library exception to a device rejection (connection fine) or <c>null</c> (communication failure).</summary>
    protected abstract DeviceRejectedException? AsRejection(Exception exception, string description);

    /// <summary>Runs <paramref name="request"/> on the connection, mapping failures to the shared exception types.</summary>
    protected async Task<T> ExecuteAsync<T>(Func<TConnection, Task<T>> request, string description, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            TConnection connection = await EnsureConnectedAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await request(connection).WaitAsync(RequestTimeout + TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Reset();
                throw;
            }
            catch (Exception ex)
            {
                if (AsRejection(ex, description) is { } rejection)
                {
                    // A well-formed negative answer: the connection is fine, the request was not.
                    throw rejection;
                }

                Reset();
                throw new DeviceCommunicationException($"'{description}' to {EndpointText} failed: {ex.Message}", ex);
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TConnection> EnsureConnectedAsync(CancellationToken cancellationToken)
    {
        if (_connection is { } existing && IsConnected(existing))
        {
            return existing;
        }

        Reset();
        _connection = await ConnectAsync(cancellationToken).ConfigureAwait(false);
        Logger.LogInformation("Connected to {Endpoint}", EndpointText);
        return _connection;
    }

    private void Reset()
    {
        if (_connection is not { } connection)
        {
            return;
        }

        _connection = null;
        Close(connection);
        Logger.LogInformation("Closed connection to {Endpoint}", EndpointText);
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await _gate.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            Reset();
        }
        finally
        {
            _gate.Release();
        }

        GC.SuppressFinalize(this);
    }
}
