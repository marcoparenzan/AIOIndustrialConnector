// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

// S7 PLC simulator for local testing of the AIO industrial connector in S7 mode.
// Usage: S7Simulator [--port 10200] [--interval 1000]
// Environment variables S7_PORT and S7_INTERVAL_MS override the defaults the same way.

using System.Diagnostics;
using System.Globalization;
using System.Net;
using AioIndustrialConnector.S7.Simulator;

int port = int.Parse(Option("--port", "S7_PORT", "10200"), CultureInfo.InvariantCulture);
int intervalMs = int.Parse(Option("--interval", "S7_INTERVAL_MS", "1000"), CultureInfo.InvariantCulture);

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };
AppDomain.CurrentDomain.ProcessExit += (_, _) => stop.Cancel();

await using S7SimulatorServer server = S7SimulatorServer.Start(IPAddress.Any, port, SimulatedProcess.CreateMemory());
Console.WriteLine($"S7 simulator listening on 0.0.0.0:{server.Port}, update every {intervalMs} ms. Ctrl+C to stop.");

var clock = Stopwatch.StartNew();
uint cycle = 0;
using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(intervalMs));
do
{
    cycle++;
    server.Update(m => SimulatedProcess.Update(m, clock.Elapsed, cycle));
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
