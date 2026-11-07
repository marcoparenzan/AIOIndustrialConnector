// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Collections.Concurrent;

namespace AioIndustrialConnector.Core.Connectivity;

/// <summary>
/// Shares one client (one connection) per device among all datasets and assets.
/// </summary>
/// <remarks>
/// Many industrial devices accept only a handful of concurrent connections, so datasets of the same device must not
/// each open their own. Clients live as long as the connector process; an unused client holds a connection only
/// until the device closes it (clients reconnect lazily on the next request).
/// </remarks>
/// <typeparam name="TKey">What identifies a distinct connection (endpoint plus timeouts, plus rack/slot for S7).</typeparam>
/// <typeparam name="TClient">The protocol client interface.</typeparam>
public class ClientPool<TKey, TClient> : IAsyncDisposable
    where TKey : notnull
    where TClient : class
{
    private readonly ConcurrentDictionary<TKey, Lazy<TClient>> _clients = new();

    /// <summary>Returns the shared client for <paramref name="key"/>, creating it with <paramref name="create"/> on first use.</summary>
    protected TClient GetOrCreate(TKey key, Func<TKey, TClient> create) =>
        _clients.GetOrAdd(key, k => new Lazy<TClient>(() => create(k))).Value;

    public async ValueTask DisposeAsync()
    {
        foreach (Lazy<TClient> client in _clients.Values)
        {
            if (client.IsValueCreated && client.Value is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync().ConfigureAwait(false);
            }
        }

        _clients.Clear();
        GC.SuppressFinalize(this);
    }
}
