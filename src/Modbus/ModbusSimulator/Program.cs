// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

// Modbus TCP device simulator for local testing of the AIO Modbus connector.
// Usage: ModbusSimulator [--port 5020] [--units 1,2] [--interval 1000]
// Environment variables MODBUS_PORT, MODBUS_UNITS and MODBUS_INTERVAL_MS override the defaults the same way.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using AioIndustrialConnector.Modbus.Simulator;

int port = int.Parse(Option("--port", "MODBUS_PORT", "5020"), CultureInfo.InvariantCulture);
byte[] units = Option("--units", "MODBUS_UNITS", "1")
    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
    .Select(u => byte.Parse(u, CultureInfo.InvariantCulture))
    .ToArray();
int intervalMs = int.Parse(Option("--interval", "MODBUS_INTERVAL_MS", "1000"), CultureInfo.InvariantCulture);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

await using ModbusSimulatorServer server = ModbusSimulatorServer.Start(IPAddress.Any, port, units);
Console.WriteLine($"Modbus TCP simulator listening on 0.0.0.0:{server.Port}, unit id(s) {string.Join(",", server.UnitIds)}, update every {intervalMs} ms. Ctrl+C to stop.");

var clock = Stopwatch.StartNew();
uint cycle = 0;
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
do
{
    cycle++;
    foreach (byte unit in server.UnitIds)
    {
        SimulatedProcess.Update(server.Store(unit), clock.Elapsed, cycle);
    }
}
while (await WaitAsync());

Console.WriteLine("Simulator stopped.");

async Task<bool> WaitAsync()
{
    try
    {
        return await timer.WaitForNextTickAsync(stop.Token);
    }
    catch (OperationCanceledException)
    {
        return false;
    }
}

string Option(string name, string env, string fallback)
{
    int i = Array.IndexOf(args, name);
    if (i >= 0 && i + 1 < args.Length)
    {
        return args[i + 1];
    }

    return Environment.GetEnvironmentVariable(env) is { Length: > 0 } value ? value : fallback;
}
