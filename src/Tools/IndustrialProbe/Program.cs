// Copyright (c) 2026 Marco Parenzan. Licensed under the MIT License.

// IndustrialProbe: runs the connector's sampling code locally, without Kubernetes, MQTT or Azure Device Registry.
// It loads the same Device/Asset manifests you deploy, samples every dataset at its configured interval
// and prints the JSON payload that the connector would publish.
//
// Usage:
//   IndustrialProbe --protocol <modbus|s7> --device deploy/<protocol>/k8s/device.yaml --asset deploy/<protocol>/k8s/asset.yaml
//                   [--address <endpoint address>] [--simulator] [--count 5] [--dataset process] [--verbose]
//
//   --protocol   which protocol module to exercise (default: modbus)
//   --address    overrides the endpoint address (the manifest usually holds an in-cluster DNS name)
//   --simulator  starts the protocol's built-in simulator in-process on the address port
//   --count      number of samples per dataset, 0 = until Ctrl+C (default 0)
//   --dataset    sample only this dataset
//   --verbose    debug logging (shows the planned read requests)

using System.Diagnostics;
using AioIndustrialConnector.Core.Configuration;
using AioIndustrialConnector.Core.Sampling;
using AioIndustrialConnector.Core.Schema;
using AioIndustrialConnector.Probe;
using Azure.Iot.Operations.Connector;
using Azure.Iot.Operations.Services.AssetAndDeviceRegistry.Models;
using Microsoft.Extensions.Logging;

string? devicePath = Arg("--device");
string? assetPath = Arg("--asset");
if (devicePath is null || assetPath is null)
{
    Console.Error.WriteLine("Usage: IndustrialProbe --protocol <modbus|s7> --device <device.yaml> --asset <asset.yaml> [--address <address>] [--simulator] [--count N] [--dataset name] [--verbose]");
    return 2;
}

bool verbose = args.Contains("--verbose");
using ILoggerFactory loggerFactory = LoggerFactory.Create(b => b
    .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
    .SetMinimumLevel(verbose ? LogLevel.Debug : LogLevel.Information));
ILogger log = loggerFactory.CreateLogger("IndustrialProbe");

await using IProbeProtocol protocol = ProbeProtocols.Create(Arg("--protocol") ?? "modbus", loggerFactory);

(string deviceName, Device device) = AdrYamlLoader.LoadDevice(devicePath);
(string assetName, Asset asset) = AdrYamlLoader.LoadAsset(assetPath);
string endpointName = asset.DeviceRef.EndpointName;

if (device.Endpoints?.Inbound?.TryGetValue(endpointName, out InboundEndpointSchemaMapValue? endpoint) != true)
{
    Console.Error.WriteLine($"Device '{deviceName}' has no inbound endpoint '{endpointName}' (referenced by asset '{assetName}').");
    return 2;
}

bool useSimulator = args.Contains("--simulator");
if (Arg("--address") is { } address)
{
    endpoint!.Address = address;
}
else if (useSimulator)
{
    endpoint!.Address = protocol.DefaultSimulatorAddress;
}

ISimulatorHandle? simulator = null;
if (useSimulator)
{
    simulator = protocol.StartSimulator(protocol.ParseEndpoint(endpoint!.Address).Port);
    log.LogInformation("{Description}", simulator.Description);
}

int count = int.TryParse(Arg("--count"), out int c) ? c : 0;
string? onlyDataset = Arg("--dataset");

using var stop = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); };

IDatasetSamplerFactory factory = protocol.CreateFactory(loggerFactory);

log.LogInformation("Protocol {Protocol}, device {Device}, endpoint {Endpoint} -> {Address}; asset {Asset} with {Count} dataset(s)",
    protocol.Name, deviceName, endpointName, endpoint!.Address, assetName, asset.Datasets?.Count ?? 0);

var loops = new List<Task>();
foreach (AssetDataset dataset in asset.Datasets ?? [])
{
    if (onlyDataset is not null && dataset.Name != onlyDataset)
    {
        continue;
    }

    IDatasetSampler sampler = factory.CreateDatasetSampler(deviceName, device, endpointName, assetName, asset, dataset, null);
    if (sampler is PolledDatasetSampler polled)
    {
        Console.WriteLine($"# schema {dataset.Name}: {JsonSchemaBuilder.Build(polled.Plan)}");
    }

    loops.Add(SampleLoopAsync(sampler, dataset, stop.Token));
}

await Task.WhenAll(loops);

if (simulator is not null)
{
    await simulator.DisposeAsync();
}

return 0;

async Task SampleLoopAsync(IDatasetSampler sampler, AssetDataset dataset, CancellationToken token)
{
    string destination = dataset.Destinations?.FirstOrDefault() switch
    {
        { Target: DatasetTarget.Mqtt } d => $"mqtt:{d.Configuration?.Topic}",
        { Target: DatasetTarget.BrokerStateStore } d => $"state-store:{d.Configuration?.Key}",
        { } d => d.Target.ToString(),
        null => "(no destination)",
    };

    TimeSpan interval = await sampler.GetSamplingIntervalAsync(dataset, token);
    using var timer = new PeriodicTimer(interval);
    int samples = 0;
    try
    {
        do
        {
            try
            {
                var sw = Stopwatch.StartNew();
                byte[] payload = await sampler.SampleDatasetAsync(dataset, token);
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {dataset.Name} -> {destination} ({sw.ElapsedMilliseconds} ms): {System.Text.Encoding.UTF8.GetString(payload)}");
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // The real connector reports this on the device status in Azure Device Registry.
                Console.WriteLine($"{DateTime.Now:HH:mm:ss.fff} {dataset.Name} ERROR: {ex.Message}");
            }

            samples++;
        }
        while ((count == 0 || samples < count) && await timer.WaitForNextTickAsync(token));
    }
    catch (OperationCanceledException)
    {
        // Ctrl+C
    }
}

string? Arg(string name)
{
    int i = Array.IndexOf(args, name);
    return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
}
