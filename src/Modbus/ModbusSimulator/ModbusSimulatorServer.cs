// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Net;
using System.Net.Sockets;
using NModbus;

namespace AioIndustrialConnector.Modbus.Simulator;

/// <summary>
/// An in-process Modbus TCP server (NModbus slave network) with one data store per unit id.
/// </summary>
/// <remarks>
/// Used by the unit tests (start on port 0 to get a free port) and by the simulator console host.
/// Addresses in the data stores are zero-based protocol addresses, the same the connector uses.
/// </remarks>
public sealed class ModbusSimulatorServer : IAsyncDisposable
{
    private readonly TcpListener _listener;
    private readonly IModbusSlaveNetwork _network;
    private readonly Dictionary<byte, IModbusSlave> _slaves = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly Task _listenTask;
    private int _disposed;

    private ModbusSimulatorServer(TcpListener listener, IModbusSlaveNetwork network, IEnumerable<IModbusSlave> slaves)
    {
        _listener = listener;
        _network = network;
        foreach (IModbusSlave slave in slaves)
        {
            _slaves[slave.UnitId] = slave;
        }

        _listenTask = Task.Run(() => _network.ListenAsync(_stop.Token));
    }

    /// <summary>The TCP port the server listens on (the actual one when started on port 0).</summary>
    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    public IReadOnlyCollection<byte> UnitIds => _slaves.Keys;

    /// <summary>Starts listening on <paramref name="address"/>:<paramref name="port"/> and serves the given unit ids.</summary>
    public static ModbusSimulatorServer Start(IPAddress address, int port, params byte[] unitIds)
    {
        if (unitIds.Length == 0)
        {
            unitIds = [1];
        }

        var listener = new TcpListener(address, port);
        listener.Start();

        var factory = new ModbusFactory();
        IModbusSlaveNetwork network = factory.CreateSlaveNetwork(listener);
        var slaves = new List<IModbusSlave>();
        foreach (byte unitId in unitIds.Distinct())
        {
            IModbusSlave slave = factory.CreateSlave(unitId);
            network.AddSlave(slave);
            slaves.Add(slave);
        }

        return new ModbusSimulatorServer(listener, network, slaves);
    }

    /// <summary>The data store of one unit; write to it to change what clients read.</summary>
    public ISlaveDataStore Store(byte unitId = 1) =>
        _slaves.TryGetValue(unitId, out IModbusSlave? slave)
            ? slave.DataStore
            : throw new ArgumentException($"Unit {unitId} is not served by this simulator.", nameof(unitId));

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1)
        {
            return;
        }

        await _stop.CancelAsync().ConfigureAwait(false);
        try
        {
            _listener.Stop();
        }
        catch (SocketException)
        {
            // Already stopped.
        }

        try
        {
            await _listenTask.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // ListenAsync ends with an exception when the listener is stopped; nothing to report.
        }

        _network.Dispose();
        _stop.Dispose();
    }
}
