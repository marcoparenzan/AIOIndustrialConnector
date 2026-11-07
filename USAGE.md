# Usage

## 1. Installation

### Prerequisites

| Tool | Needed for | Notes |
| --- | --- | --- |
| .NET SDK 10 or later | build, test, container images | The projects target `net10.0`; a newer SDK (e.g. 11) also builds them. The .NET 10 runtime is needed to run tests and tools. |
| Azure CLI + `azure-iot-ops` extension | deployment to an AIO instance | `az extension add --upgrade --name azure-iot-ops` |
| ORAS CLI | publishing the connector metadata | <https://oras.land/docs/installation/> |
| kubectl (+ k3d) | development deployments only | Not needed for a portal/CLI deployment. |

Docker is **not** required: container images are built by the .NET SDK (`-t:PublishContainer`) and pushed straight to the registry or written to a tarball.

Build output goes to `$BUILD_ARTIFACTS` when that environment variable is set; otherwise to `D:\Dev\BuildArtifacts\AIOIndustrialConnector\<project>\` when `D:\Dev\BuildArtifacts` exists; otherwise to `artifacts\` inside the repository (git-ignored). See [Directory.Build.props](Directory.Build.props).

### Get the code and build

```powershell
cd D:\Dev\2026\IoTHub\AIOIndustrialConnector
dotnet build AIOIndustrialConnector.slnx
```

## 2. Test

```powershell
dotnet test --solution AIOIndustrialConnector.slnx
```

The tests use xUnit v3 on Microsoft.Testing.Platform (enabled in [global.json](global.json)). Three projects: `tests/Core` (protocol-neutral units and the protocol selection), `tests/Modbus` and `tests/S7`. The Modbus and S7 end-to-end tests start the protocol's simulator in-process on a free loopback port, so no network setup is needed.

## 3. Choose the protocol (mode)

One executable, one protocol per instance. The mode is taken from `--protocol <modbus|s7>` or the `CONNECTOR_PROTOCOL` environment variable (argument wins). Without either the process exits with an error listing the available modes. The container images built by `Publish-Connector.ps1` / `deploy-k3d.sh` have the variable baked in, so a connector template needs no extra configuration.

| Protocol | Image built with | Per-protocol guide |
| --- | --- | --- |
| Modbus TCP | `-p:ConnectorProtocol=modbus` → `aio-modbus-connector` | [docs/modbus](docs/modbus/README.md) |
| Siemens S7 | `-p:ConnectorProtocol=s7` → `aio-s7-connector` | [docs/s7](docs/s7/README.md) |

## 4. Run locally against a simulator

See [docs/modbus/LOCAL-SIMULATION.md](docs/modbus/LOCAL-SIMULATION.md) and [docs/s7/LOCAL-SIMULATION.md](docs/s7/LOCAL-SIMULATION.md). In short:

```powershell
# Simulator and connector sampling code in one process, using the deploy manifests
dotnet run --project src/Tools/IndustrialProbe -- --protocol modbus --device deploy/modbus/k8s/device.yaml --asset deploy/modbus/k8s/asset.yaml --simulator
dotnet run --project src/Tools/IndustrialProbe -- --protocol s7     --device deploy/s7/k8s/device.yaml     --asset deploy/s7/k8s/asset.yaml     --simulator

# Or a standalone simulator and the probe in another terminal
dotnet run --project src/Modbus/ModbusSimulator -- --port 5020
dotnet run --project src/S7/S7Simulator -- --port 10200
```

The probe and simulators run on any machine (bare metal included). The **connector host** itself currently needs the AIO environment (MQTT credentials and Azure Device Registry service supplied by the Akri operator); running it standalone is on the [roadmap](ROADMAP.md).

## 5. Deploy to an Azure IoT Operations instance

Full walkthroughs: [docs/modbus/PORTAL.md](docs/modbus/PORTAL.md), [docs/s7/PORTAL.md](docs/s7/PORTAL.md). Unattended version: [AGENT.md](AGENT.md).

```powershell
az login
# Image + metadata to ACR, connector template on the instance (one run per protocol)
./deploy/Publish-Connector.ps1 -Protocol modbus -AcrName <acr> -ResourceGroup <rg> -Instance <aio-instance> -IncludeSimulator
./deploy/Publish-Connector.ps1 -Protocol s7     -AcrName <acr> -ResourceGroup <rg> -Instance <aio-instance> -IncludeSimulator
# Optional: sample device + asset through Azure Resource Manager (visible in the portal)
./deploy/modbus/New-SampleAsset.ps1 -ResourceGroup <rg> -Instance <aio-instance>
./deploy/s7/New-SampleAsset.ps1     -ResourceGroup <rg> -Instance <aio-instance>
```

## 6. Development deployment (k3d, kubectl)

For the local AIO developer cluster described in the SDK's `doc/setup.md`:

```bash
./deploy/deploy-k3d.sh modbus     # builds image tarballs, imports them into k3d, applies deploy/modbus/k8s/*.yaml
./deploy/deploy-k3d.sh s7
mosquitto_sub -L mqtt://localhost/modbus/modbus-sim-device/modbus-sim-asset/process
mosquitto_sub -L mqtt://localhost/s7/s7-sim-device/s7-sim-asset/process
```

Resources applied with kubectl are **not** synchronized to Azure and do not appear in the portal; use this path only for development.

## 7. Configure your own device

1. Create a device with an inbound endpoint of type **`Custom.ModbusTcp`** (address `modbus-tcp://<ip>:502`) or **`Custom.SiemensS7`** (address `s7://<ip>:102`, plus CPU type, rack and slot in the endpoint configuration).
2. Create an asset on that endpoint, add datasets (one per sampling interval / destination), and add one data point per value: `dataSource` = the protocol address (Modbus: `hr:0`, `ir:10`, `coil:3`, `40001`; S7: `DB1.DBD0`, `MW10`, `Q0.1`), configuration = data type and decoding options.

All options: [docs/modbus/CONFIGURATION.md](docs/modbus/CONFIGURATION.md), [docs/s7/CONFIGURATION.md](docs/s7/CONFIGURATION.md).
