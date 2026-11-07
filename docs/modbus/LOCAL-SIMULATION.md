# Running the Modbus simulation locally

Three ways to exercise the connector on a developer machine, from the fastest to the most complete. None of them needs Docker, Kubernetes or Azure.

| Option | What runs | Use it for |
| --- | --- | --- |
| A. Unit tests | In-process NModbus simulator on a free loopback port + the real sampler | Regression, CI |
| B. Probe with `--simulator` | Simulator and connector sampling code in one process, using the deploy manifests | Trying a register map in seconds |
| C. Standalone simulator + probe | Simulator on TCP port 5020, probe (or any Modbus tool) connecting to it | Testing like a real device, with external Modbus tools too |

## A. Unit tests

```powershell
dotnet test --solution AIOIndustrialConnector.slnx
```

[SimulatorSamplingTests](../../tests/Modbus/IndustrialConnector.Modbus.Tests/SimulatorSamplingTests.cs) starts `ModbusSimulatorServer` on port 0, fills it with `SimulatedProcess.Update(...)` and samples it through the full chain: `ModbusDatasetSamplerFactory` → plan → `NModbusTcpClient` → TCP → NModbus slave. It also covers multiple unit ids, device restart/reconnect and unreachable devices. To write your own test:

```csharp
await using var server = ModbusSimulatorServer.Start(IPAddress.Loopback, 0, 1);
server.Store(1).WriteHolding(100, RegisterWriter.Float32(21.5f, wordSwap: true));
// ... build a Device whose endpoint address is $"modbus-tcp://127.0.0.1:{server.Port}" and sample
```

## B. Probe with the in-process simulator

```powershell
dotnet run --project src/Tools/IndustrialProbe -- --protocol modbus --device deploy/modbus/k8s/device.yaml --asset deploy/modbus/k8s/asset.yaml --simulator
```

The probe reads the same `Device` and `Asset` manifests you would deploy, replaces the endpoint address with `127.0.0.1:5020`, starts the simulator there, and samples every dataset at its configured interval. Output:

```text
# schema process: {"$schema":"http://json-schema.org/draft-07/schema#","type":"object","properties":{...}}
01:30:14.845 process -> mqtt:modbus/modbus-sim-device/modbus-sim-asset/process (2 ms): {"temperature":25.53,"humidity":50.7,"cycleCounter":3,...}
01:30:13.881 identity -> state-store:modbus-sim-asset-identity (28 ms): {"serialNumber":"AIO-SIM-0001","totalEnergyKWh":1000.000025093}
```

Options: `--protocol modbus` (default), `--count N` (samples per dataset, default unlimited, Ctrl+C to stop), `--dataset <name>`, `--verbose` (shows the planned read requests), `--address <modbus-tcp://host:port>`.

Edit `deploy/modbus/k8s/asset.yaml` (or a copy) and re-run to try a register map; configuration errors are printed exactly as the connector would report them on the device status.

## C. Standalone simulator

```powershell
# Terminal 1
dotnet run --project src/Modbus/ModbusSimulator -- --port 5020 --units 1,2 --interval 1000

# Terminal 2: the connector code against it
dotnet run --project src/Tools/IndustrialProbe -- --protocol modbus --device deploy/modbus/k8s/device.yaml --asset deploy/modbus/k8s/asset.yaml --address modbus-tcp://127.0.0.1:5020
```

Use `127.0.0.1` rather than `localhost`: the simulator listens on IPv4 only, and on Windows `localhost` tries IPv6 (`::1`) first, which delays the first connection by about 2 seconds.

Environment variables `MODBUS_PORT`, `MODBUS_UNITS`, `MODBUS_INTERVAL_MS` work too (used by the container). Any Modbus client can connect to it (e.g. `mbpoll -a 1 -r 1 -c 10 -p 5020 127.0.0.1`, or Modbus Poll / QModMaster on Windows). Port 502 needs administrator rights on Windows and root on Linux, hence 5020.

### Probe against a real device

```powershell
dotnet run --project src/Tools/IndustrialProbe -- --protocol modbus --device my-device.yaml --asset my-asset.yaml --address modbus-tcp://192.168.1.10:502 --count 1 --verbose
```

This is a safe, read-only way to validate a register map against real hardware before deploying it.

## Register map

The simulator serves the same map on every unit id. All addresses are zero-based (`hr:0` = Modicon `40001`).

| Address | Name | Encoding | Behaviour |
| --- | --- | --- | --- |
| `hr:0-1` | temperature | Float32, ABCD | 20–30 °C, sine, 60 s period |
| `hr:2` | humidity | UInt16, ×0.1 | 40.0–60.0 % |
| `hr:3-4` | cycleCounter | UInt32, ABCD | +1 per update |
| `hr:5` | pressureDelta | Int16 | −500..500 |
| `hr:6` | statusWord | bit 0 running, bit 1 alarm | alarm when temperature > 29 °C |
| `hr:10-17` | serialNumber | String, 8 registers | `AIO-SIM-0001` |
| `hr:20-23` | totalEnergy | Float64, ABCD | kWh, slowly increasing |
| `ir:0-1` | flowRate | Float32, **CDAB** (word swapped) | 0 when the pump is off, ~10–12 m³/h when on |
| `ir:2` | tankLevelRaw | UInt16, 0–4095 ADC | sine, 120 s period |
| `ir:3` | byteSwapped | UInt16 1234, **byte swapped** | reads 1234 only with `byteOrder: LittleEndian` |
| `coil:0` | pumpOn | bit | toggles every 10 s |
| `coil:1` | valveOpen | bit | follows pumpOn |
| `di:0` | doorClosed | bit | always true |
| `di:1` | emergencyStop | bit | always false |

The map is defined in [SimulatedProcess.cs](../../src/Modbus/ModbusSimulator/SimulatedProcess.cs).

## In a k3d cluster

For the full AIO path on a local cluster (Akri operator, MQTT broker): `./deploy/deploy-k3d.sh modbus`, see [USAGE.md](../../USAGE.md#5-development-deployment-k3d-kubectl).
