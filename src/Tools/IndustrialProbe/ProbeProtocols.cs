// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

using System.Diagnostics;
using System.Net;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Modbus;
using AioIndustrialConnector.S7;
using Azure.Iot.Operations.Connector;
using Microsoft.Extensions.Logging;
using ModbusSim = AioIndustrialConnector.Modbus.Simulator;
using S7Sim = AioIndustrialConnector.S7.Simulator;

namespace AioIndustrialConnector.Probe;

/// <summary>A running in-process simulator started by the probe.</summary>
internal interface ISimulatorHandle : IAsyncDisposable
{
    string Description { get; }
}

/// <summary>
/// What the probe needs from a protocol (strategy): the production sampler factory, endpoint parsing, and an
/// in-process simulator. Everything else in the probe is protocol-neutral.
/// </summary>
internal interface IProbeProtocol : IAsyncDisposable
{
    string Name { get; }

    string DefaultSimulatorAddress { get; }

    IDatasetSamplerFactory CreateFactory(ILoggerFactory loggerFactory);

    TcpEndpoint ParseEndpoint(string? address);

    ISimulatorHandle StartSimulator(int port);
}

internal static class ProbeProtocols
{
    public static IProbeProtocol Create(string name, ILoggerFactory loggerFactory) => name.ToLowerInvariant() switch
    {
        "modbus" => new ModbusProbeProtocol(loggerFactory),
        "s7" => new S7ProbeProtocol(loggerFactory),
        _ => throw new ArgumentException($"Unknown protocol '{name}'. Use modbus or s7."),
    };
}

/// <summary>Base for simulators that update their process state on a timer.</summary>
internal abstract class TimedSimulator : ISimulatorHandle
{
    private readonly CancellationTokenSource _stop = new();
    private Task? _loop;

    public abstract string Description { get; }

    protected void StartLoop(Action<TimeSpan, uint> update)
    {
        _loop = Task.Run(async () =>
        {
            var clock = Stopwatch.StartNew();
            uint cycle = 0;
            using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(500));
            try
            {
                do
                {
                    update(clock.Elapsed, ++cycle);
                }
                while (await timer.WaitForNextTickAsync(_stop.Token));
            }
            catch (OperationCanceledException)
            {
            }
        });
    }

    protected abstract ValueTask StopServerAsync();

    public async ValueTask DisposeAsync()
    {
        await _stop.CancelAsync();
        if (_loop is not null)
        {
            await _loop;
        }

        await StopServerAsync();
        _stop.Dispose();
    }
}

internal sealed class ModbusProbeProtocol : IProbeProtocol
{
    private readonly ILoggerFactory _loggerFactory;
    private readonly ModbusClientPool _pool;

    public ModbusProbeProtocol(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
        _pool = new ModbusClientPool(loggerFactory);
    }

    public string Name => "modbus";

    public string DefaultSimulatorAddress => "modbus-tcp://127.0.0.1:5020";

    public IDatasetSamplerFactory CreateFactory(ILoggerFactory loggerFactory) => new ModbusDatasetSamplerFactory(_pool, loggerFactory);

    public TcpEndpoint ParseEndpoint(string? address) => ModbusProtocol.ParseEndpoint(address);

    public ISimulatorHandle StartSimulator(int port) => new Simulator(port);

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private sealed class Simulator : TimedSimulator
    {
        private readonly ModbusSim.ModbusSimulatorServer _server;

        public Simulator(int port)
        {
            _server = ModbusSim.ModbusSimulatorServer.Start(IPAddress.Loopback, port, 1, 2);
            StartLoop((elapsed, cycle) =>
            {
                foreach (byte unit in _server.UnitIds)
                {
                    ModbusSim.SimulatedProcess.Update(_server.Store(unit), elapsed, cycle);
                }
            });
        }

        public override string Description => $"Modbus simulator listening on 127.0.0.1:{_server.Port} (units 1, 2)";

        protected override ValueTask StopServerAsync() => _server.DisposeAsync();
    }
}

internal sealed class S7ProbeProtocol : IProbeProtocol
{
    private readonly S7ClientPool _pool;

    public S7ProbeProtocol(ILoggerFactory loggerFactory) => _pool = new S7ClientPool(loggerFactory);

    public string Name => "s7";

    public string DefaultSimulatorAddress => "s7://127.0.0.1:10200";

    public IDatasetSamplerFactory CreateFactory(ILoggerFactory loggerFactory) => new S7DatasetSamplerFactory(_pool, loggerFactory);

    public TcpEndpoint ParseEndpoint(string? address) => S7Protocol.ParseEndpoint(address);

    public ISimulatorHandle StartSimulator(int port) => new Simulator(port);

    public ValueTask DisposeAsync() => _pool.DisposeAsync();

    private sealed class Simulator : TimedSimulator
    {
        private readonly S7Sim.S7SimulatorServer _server;

        public Simulator(int port)
        {
            _server = S7Sim.S7SimulatorServer.Start(IPAddress.Loopback, port, S7Sim.SimulatedProcess.CreateMemory());
            StartLoop((elapsed, cycle) => _server.Update(m => S7Sim.SimulatedProcess.Update(m, elapsed, cycle)));
        }

        public override string Description => $"S7 simulator listening on 127.0.0.1:{_server.Port}";

        protected override ValueTask StopServerAsync() => _server.DisposeAsync();
    }
}
