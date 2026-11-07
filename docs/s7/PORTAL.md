# Registering the S7 connector and configuring a PLC in the portal

This guide makes the **S7 mode** of the connector available on an Azure IoT Operations instance and configures a Siemens PLC and its tags in the **operations experience** (<https://iotoperations.azure.com>). It follows the official flow in [Build and deploy custom Akri connectors](https://learn.microsoft.com/en-us/azure/iot-operations/develop-edge-apps/howto-develop-akri-connectors) and is the S7 twin of [../modbus/PORTAL.md](../modbus/PORTAL.md).

> Status: the steps follow the Microsoft documentation as of November 2026. They have **not yet been executed against a live instance** with this connector (see [OPENPOINTS.md](../../OPENPOINTS.md)). Screens and labels may differ slightly between AIO releases.

The S7 and Modbus modes are the same executable, installed as **two independent connector templates** with their own image, metadata artifact and endpoint type. Installing one does not affect the other.

## How the pieces fit

| Piece | Where it lives | Who creates it |
| --- | --- | --- |
| Connector container image `aio-s7-connector:<ver>` (starts in S7 mode: `CONNECTOR_PROTOCOL=s7` is baked in) | Your Azure Container Registry | `Publish-Connector.ps1 -Protocol s7` |
| Connector metadata `aio-s7-connector-metadata:<ver>` | Same ACR, OCI artifact with media type `application/vnd.microsoft.akri-connector.v1+json` | `Publish-Connector.ps1 -Protocol s7` (`oras push`) |
| **Connector template** (`akriConnectorTemplates` resource) | AIO instance in Azure, synced to the cluster | Azure portal or `az iot ops connector template create` |
| **Device** with a `Custom.SiemensS7` inbound endpoint | Azure Device Registry namespace | Operations experience or `az iot ops ns device …` |
| **Asset** with datasets and data points (= tags) | Azure Device Registry namespace | Operations experience or `az iot ops ns asset custom …` |
| Connector pod | Cluster, created by the Akri operator | Automatic, once an asset uses the endpoint type |

The **metadata file** ([deploy/s7/connector-metadata.json](../../deploy/s7/connector-metadata.json)) drives the portal UI: its schemas become the form fields for the endpoint (*CPU type, Rack, Slot, timeouts*), the dataset (*Sampling interval, Max bytes per request, Max address gap*) and the data point (*Data type, String length, Bit, Scale, Offset*). The portal calls data points **tags**.

## Prerequisites

- An Azure IoT Operations instance (release 2512 or later recommended).
- An Azure Container Registry that the cluster can pull from (anonymous pull, or an image pull secret on the template).
- Azure CLI with the `azure-iot-ops` extension, ORAS CLI, .NET SDK 10+. See [USAGE.md](../../USAGE.md#1-installation).
- A PLC prepared for PUT/GET access and non-optimized data blocks: [CONFIGURATION.md](CONFIGURATION.md#plc-preparation-read-this-first). **Network:** the connector pod must reach the PLC's port 102.

## Step 1 — Publish image and metadata

```powershell
az login
./deploy/Publish-Connector.ps1 -Protocol s7 -AcrName <acr> -ResourceGroup <rg> -Instance <aio-instance> -IncludeSimulator -SkipTemplate
```

This pushes `<acr>.azurecr.io/aio-s7-connector:0.2.0`, optionally `<acr>.azurecr.io/aio-s7-simulator:0.2.0`, and the metadata artifact `<acr>.azurecr.io/aio-s7-connector-metadata:0.2.0` (with `imageName` rewritten to your registry).

Manual equivalent of the metadata push (bash):

```bash
sed "s#__REGISTRY__#<acr>.azurecr.io#" deploy/s7/connector-metadata.json > connector-metadata.json
oras push --config /dev/null:application/vnd.microsoft.akri-connector.v1+json \
  <acr>.azurecr.io/aio-s7-connector-metadata:0.2.0 connector-metadata.json:application/json
```

## Step 2 — Create the connector template

### Azure portal

1. Azure portal → your **Azure IoT Operations instance** → **Components** → **Connector templates** → **+ Create a connector template**.
2. Select **SiemensS7Connector** and check its status is **Valid**. Select **Metadata >**.
3. Name the template, e.g. `s7-connector`. Select **Device inbound endpoint type >**; the type shown is **`Custom.SiemensS7`**. Continue to **Review**, keep 1 replica, **Create**.

### Azure CLI

```bash
az iot ops connector template create --name s7-connector \
  --resource-group <rg> --instance <aio-instance> \
  --connector-metadata-ref <acr>.azurecr.io/aio-s7-connector-metadata:0.2.0 \
  --replicas 1 --log-level info
az iot ops connector template list --resource-group <rg> --instance <aio-instance> -o table
```

> **Keep 1 replica per PLC fleet.** With several replicas only the leader polls (leader election, lock `aio-s7-connector-leader`), so extra replicas only add failover, not throughput.

> **Open question:** how the mode reaches the container. The published image has `CONNECTOR_PROTOCOL=s7` baked in, so the template needs no environment configuration. If you instead reuse the Modbus image, set the environment variable `CONNECTOR_PROTOCOL=s7` or the container argument `--protocol s7` in the template's runtime configuration (check `az iot ops connector template create --help` for the supported options); the connector refuses to start without a protocol.

## Step 3 — (Optional) run the simulator in the cluster

To test without a PLC, deploy the simulator next to AIO (needs kubectl access to the cluster, e.g. through `az connectedk8s proxy`):

```bash
sed "s#image: aio-s7-simulator:0.2.0#image: <acr>.azurecr.io/aio-s7-simulator:0.2.0#" deploy/s7/k8s/simulator.yaml | kubectl apply -f -
```

Its address is `s7://s7-simulator.azure-iot-operations.svc.cluster.local:102`. Memory map: [LOCAL-SIMULATION.md](LOCAL-SIMULATION.md#memory-map).

## Step 4 — Create the device (operations experience)

1. Operations experience → your instance → **Devices** → **+ Create new** → **Device**. Name it, e.g. `line1-plc`.
2. On the **SiemensS7Connector** tile select **+ New** to add an inbound endpoint:
   - **Endpoint name**: e.g. `plc-s7`.
   - **Address**: `s7://192.168.0.1:102` (or the simulator address above).
   - **Authentication**: *Anonymous* (classic S7comm has no user authentication).
   - **Advanced** tab (from `additionalConfigurationSchema`): **CPU type**, **Rack**, **Slot**, **Response timeout (ms)**, **Connect timeout (ms)**.
3. **Save**, **Next** through *Additional info*, **Create**.

CLI equivalent:

```bash
az iot ops ns device create --name line1-plc -i <aio-instance> -g <rg>
az iot ops ns device endpoint inbound add custom --device line1-plc --name plc-s7 \
  --endpoint-type Custom.SiemensS7 --version 1.0 --endpoint-address s7://192.168.0.1:102 \
  --additional-config '{"cpuType": "S71500", "rack": 0, "slot": 1}' -i <aio-instance> -g <rg>
```

## Step 5 — Create the asset and map tags

1. **Assets** → **+ Create new** → **Asset**. Select the inbound endpoint `plc-s7`, name the asset (e.g. `line1-oven`). On the asset defaults you can set **Sampling interval (ms)** for all datasets.
2. **Datasets** → **Create dataset**: name (e.g. `process`), leave **Data source** empty (not used), choose the **Destination** (MQTT topic such as `plant/line1/oven/process`, QoS 1, Retain *Never*; or a broker state store key), and the **Sampling interval (ms)**, optionally **Max bytes per request** and **Max address gap**.
3. **List of tags** → **+ Add tag** for each value:

| Tag name | Data source | Data type | Other fields |
| --- | --- | --- | --- |
| `temperature` | `DB1.DBD0` | Float32 (`Real`) | |
| `humidity` | `DB1.DBW4` | Int16 (`Int`) | Scale `0.1` |
| `running` | `DB1.DBX12.0` | Boolean | |
| `serialNumber` | `DB1.DBB20` | String | String length `16` |
| `totalEnergy` | `DB1.DBB40` | Float64 (`LReal`) | |
| `pumpOn` | `Q0.0` | Boolean | |
| `tankLevel` | `MW0` | UInt16 | Scale `0.0244` |

   Addressing rules, all data types and what fits which address width: [CONFIGURATION.md](CONFIGURATION.md).
4. **Save**, **Next**, **Create**.

Within a few seconds the Akri operator starts a pod named `s7-connector-…` that samples the asset. With many tags, prefer the CLI import (`az iot ops ns asset custom datapoint import --input-file …`) or [deploy/s7/New-SampleAsset.ps1](../../deploy/s7/New-SampleAsset.ps1) as a template.

## Step 6 — Verify

- **Device status** in the operations experience: errors from the connector (unreachable PLC, configuration errors with the full list of problems) appear on the device / endpoint status.
- **Data**: subscribe to the dataset topic from inside the cluster, or create a data flow from the topic to Event Hubs / Fabric.
- **Schema registry**: one JSON Schema per dataset, derived from the tag definitions.
- **Logs**: `kubectl logs -n azure-iot-operations <pod whose name starts with the template name> -f`. Healthy lines: `Starting in s7 mode (endpoint type Custom.SiemensS7)`, `Connected to S7 PLC host:102 (rack 0, slot 1)` and `Asset …, dataset …: N data point(s) from host:port in M request(s) every … ms`.

## Updating

- Changing the asset (add/edit tags) re-creates the samplers for that asset; no restart is needed.
- New connector version: bump `Version` in the csproj files and `version` in the metadata, run `Publish-Connector.ps1 -Protocol s7 -Version x.y.z`, then update or recreate the connector template with the new metadata reference.
