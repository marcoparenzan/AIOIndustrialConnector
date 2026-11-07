# Running the S7 simulation locally

Ways to exercise the S7 mode on a developer machine, from the fastest to the most complete. None of them needs Docker, Kubernetes, Azure or a real PLC.

| Option | What runs | Use it for |
| --- | --- | --- |
| A. Unit tests | In-process S7 simulator on a free loopback port + the real sampler and S7netplus client | Regression, CI |
| B. Probe with `--simulator` | Simulator and connector sampling code in one process, using the deploy manifests | Trying an address map in seconds |
| C. Standalone simulator + probe | Simulator on TCP port 10200, probe (or any S7 client) connecting to it | Testing like a real PLC |
| D. Probe against a real PLC | Read-only polling of real hardware | Validating a map before deploying it |

The simulator is an **independent implementation** of ISO-on-TCP (TPKT/COTP) and the S7comm *setup communication* and *read variable* services, written from the wire format — not a wrapper around S7netplus — so tests exercise a genuine protocol round trip. It does not implement writes. It accepts any rack/slot.

## A. Unit tests

```powershell
dotnet test --solution AIOIndustrialConnector.slnx
```

[SimulatorSamplingTests](../../tests/S7/IndustrialConnector.S7.Tests/SimulatorSamplingTests.cs) starts `S7SimulatorServer` on port 0, fills its memory and samples it through the full chain: `S7DatasetSamplerFactory` → plan → `S7NetClient` → TCP → simulator. It covers the whole memory map, one shared connection per PLC, reads larger than one PDU, a missing data block (points become `null`), PLC restart/reconnect and an unreachable PLC. To write your own test:

```csharp
await using var plc = S7SimulatorServer.Start(IPAddress.Loopback, 0, new S7Memory().CreateDataBlock(1, 64));
plc.Update(m => m.SetFloat(S7Memory.DataBlock, 1, 0, 21.5f));
// ... build a Device whose endpoint address is $"s7://127.0.0.1:{plc.Port}" and sample DB1.DBD0
```

## B. Probe with the in-process simulator

```powershell
dotnet run --project src/Tools/IndustrialProbe -- --protocol s7 --device deploy/s7/k8s/device.yaml --asset deploy/s7/k8s/asset.yaml --simulator
```

The probe reads the same `Device` and `Asset` manifests you would deploy, replaces the endpoint address with `127.0.0.1:10200`, starts the simulator there, and samples every dataset at its configured interval. Output:

```text
# schema process: {"$schema":"http://json-schema.org/draft-07/schema#","type":"object","properties":{...}}
16:24:04.871 identity -> state-store:s7-sim-asset-identity (17 ms): {"serialNumber":"AIO-SIM-0001","totalEnergyKWh":1000.000010161}
16:24:04.871 process -> mqtt:s7/s7-sim-device/s7-sim-asset/process (33 ms): {"temperature":25.0005,"humidity":50,"cycleCounter":1,"pressureDelta":0,"running":true,"alarm":false,"tankLevelPercent":50.01,"doorClosed":true,"emergencyStop":false,"pumpOn":true,"valveOpen":true}
```

Options: `--count N` (samples per dataset, default unlimited, Ctrl+C to stop), `--dataset <name>`, `--verbose` (shows the planned read requests), `--address <s7://host:port>`.

Edit `deploy/s7/k8s/asset.yaml` (or a copy) and re-run to try an address map; configuration errors are printed exactly as the connector would report them on the device status.

## C. Standalone simulator

```powershell
# Terminal 1
dotnet run --project src/S7/S7Simulator -- --port 10200 --interval 1000

# Terminal 2: the connector code against it
dotnet run --project src/Tools/IndustrialProbe -- --protocol s7 --device deploy/s7/k8s/device.yaml --asset deploy/s7/k8s/asset.yaml --address s7://127.0.0.1:10200
```

Use `127.0.0.1` rather than `localhost` (IPv4 only; on Windows `localhost` tries IPv6 first). Environment variables `S7_PORT` and `S7_INTERVAL_MS` work too (used by the container). Any S7 client (Snap7 client demo, S7netplus, Node-RED `node-red-contrib-s7`) can connect for reads. Port 102 needs administrator rights on Windows and root on Linux, hence 10200.

## D. Probe against a real PLC

```powershell
dotnet run --project src/Tools/IndustrialProbe -- --protocol s7 --device my-device.yaml --asset my-asset.yaml --address s7://192.168.0.1:102 --count 1 --verbose
```

Read-only, so it is a safe way to validate an address map against real hardware. Remember the [PLC preparation](CONFIGURATION.md#plc-preparation-read-this-first): PUT/GET and non-optimized data blocks. Note that the probe's `--simulator` flag must be left off.

## Memory map

| Address | Name | Encoding | Behaviour |
| --- | --- | --- | --- |
| `DB1.DBD0` | temperature | REAL | 20–30 °C, sine, 60 s period |
| `DB1.DBW4` | humidity | INT, ×0.1 | 40.0–60.0 % |
| `DB1.DBD6` | cycleCounter | DWORD | +1 per update |
| `DB1.DBW10` | pressureDelta | INT | −500..500 |
| `DB1.DBX12.0` | running | BOOL | follows the pump |
| `DB1.DBX12.1` | alarm | BOOL | when temperature > 29 °C |
| `DB1.DBB20` | serialNumber | STRING[16] (`length` 16) | `AIO-SIM-0001` |
| `DB1.DBB40` | totalEnergy | LREAL | kWh, slowly increasing |
| `MW0` | tankLevelRaw | WORD, 0–4095 ADC | sine, 120 s period |
| `I0.0` | doorClosed | BOOL | always true |
| `I0.1` | emergencyStop | BOOL | always false |
| `Q0.0` | pumpOn | BOOL | toggles every 10 s |
| `Q0.1` | valveOpen | BOOL | follows pumpOn |

DB1 is 64 bytes; the flag, input and output areas are 256 bytes. Any other data block does not exist (reading it returns the PLC error *object does not exist*). The map is defined in [SimulatedProcess.cs](../../src/S7/S7Simulator/SimulatedProcess.cs).

## In a k3d cluster

For the full AIO path on a local cluster (Akri operator, MQTT broker): `./deploy/deploy-k3d.sh s7`, see [USAGE.md](../../USAGE.md#5-development-deployment-k3d-kubectl).
