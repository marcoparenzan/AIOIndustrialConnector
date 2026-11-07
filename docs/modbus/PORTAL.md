# Registering the connector and configuring registers in the portal

This guide makes the **Modbus mode** of the connector available on an Azure IoT Operations instance and configures a device and its registers in the **operations experience** (<https://iotoperations.azure.com>). It follows the official flow in [Build and deploy custom Akri connectors](https://learn.microsoft.com/en-us/azure/iot-operations/develop-edge-apps/howto-develop-akri-connectors).

> Status: the steps follow the Microsoft documentation as of November 2026. They have **not yet been executed against a live instance** with this connector (see [OPENPOINTS.md](../../../OPENPOINTS.md)). Screens and labels may differ slightly between AIO releases.

The same executable also runs in S7 mode with its own image, metadata and template; that flow is in [../s7/PORTAL.md](../s7/PORTAL.md). Nothing below is shared state: the two modes are installed, configured and scaled independently.

## How the pieces fit

| Piece | Where it lives | Who creates it |
| --- | --- | --- |
| Connector container image `aio-modbus-connector:<ver>` | Your Azure Container Registry | `Publish-Connector.ps1` (`dotnet publish -t:PublishContainer`) |
| Connector metadata `aio-modbus-connector-metadata:<ver>` | Same ACR, OCI artifact with media type `application/vnd.microsoft.akri-connector.v1+json` | `Publish-Connector.ps1` (`oras push`) |
| **Connector template** (`akriConnectorTemplates` resource) | AIO instance in Azure, synced to the cluster | Azure portal or `az iot ops connector template create` |
| **Device** with a `Custom.ModbusTcp` inbound endpoint | Azure Device Registry namespace | Operations experience or `az iot ops ns device …` |
| **Asset** with datasets and data points (= registers) | Azure Device Registry namespace | Operations experience or `az iot ops ns asset custom …` |
| Connector pod | Cluster, created by the Akri operator | Automatic, once an asset uses the endpoint type |

The **metadata file** ([deploy/modbus/connector-metadata.json](../../deploy/modbus/connector-metadata.json)) drives the portal UI: its `additionalConfigurationSchema`, `datasetConfigurationSchema` and `dataPointConfigurationSchema` become the form fields you see when creating endpoints, datasets and data points. That is why the portal can offer a *Data type* drop-down, *Byte order*, *Word order*, *Scale*, *Bit*, *Unit id* and *Sampling interval (ms)* for this connector.

## Prerequisites

- An Azure IoT Operations instance (release 2512 or later recommended; earlier releases have a known issue with `RegistryEndpoint` resources for Akri connectors).
- An Azure Container Registry that the cluster can pull from. The simplest option is **anonymous pull**; otherwise configure an image pull secret on the connector template. Microsoft's known-issues page recommends putting registry details in the `ContainerRegistry` settings of the template rather than referencing a `RegistryEndpoint` resource.
- Azure CLI with the `azure-iot-ops` extension, ORAS CLI, .NET SDK 10+. See [USAGE.md](../../USAGE.md#1-installation).

## Step 1 — Publish image and metadata

```powershell
az login
./deploy/Publish-Connector.ps1 -Protocol modbus -AcrName <acr> -ResourceGroup <rg> -Instance <aio-instance> -IncludeSimulator -SkipTemplate
```

This pushes `<acr>.azurecr.io/aio-modbus-connector:0.2.0`, optionally `<acr>.azurecr.io/aio-modbus-simulator:0.2.0`, and the metadata artifact `<acr>.azurecr.io/aio-modbus-connector-metadata:0.2.0` (with `imageName` rewritten to your registry).

Manual equivalent of the metadata push (bash):

```bash
sed "s#__REGISTRY__#<acr>.azurecr.io#" deploy/modbus/connector-metadata.json > connector-metadata.json
oras push --config /dev/null:application/vnd.microsoft.akri-connector.v1+json \
  <acr>.azurecr.io/aio-modbus-connector-metadata:0.2.0 connector-metadata.json:application/json
```

Without the `--config …:application/vnd.microsoft.akri-connector.v1+json` media type the instance does not recognize the artifact as connector metadata.

## Step 2 — Create the connector template

### Azure portal

1. Azure portal → your **Azure IoT Operations instance** → **Components** → **Connector templates** → **+ Create a connector template**.
2. The list shows connector types found in the registry. Select **ModbusTcpConnector** and check its status is **Valid**. Select **Metadata >**.
3. Name the template, e.g. `modbus-tcp-connector` (it becomes the prefix of the connector pod name). Select **Device inbound endpoint type >**.
4. The endpoint type shown is **`Custom.ModbusTcp`** (from the metadata). Continue through **Diagnostics configurations** and **Runtime configuration** (keep 1 replica) to **Review**, then **Create**.

### Azure CLI

```bash
az iot ops connector template create --name modbus-tcp-connector \
  --resource-group <rg> --instance <aio-instance> \
  --connector-metadata-ref <acr>.azurecr.io/aio-modbus-connector-metadata:0.2.0 \
  --replicas 1 --log-level info
az iot ops connector template list --resource-group <rg> --instance <aio-instance> -o table
```

If the connector type does not appear in the portal list, the metadata artifact was not discovered: check the registry endpoint of the instance, the artifact media type, and that the metadata validates against the [schema](https://json.schemastore.org/aio-connector-metadata-11.0-preview.json).

## Step 3 — (Optional) run the simulator in the cluster

To test without a real PLC, deploy the simulator next to AIO (needs kubectl access to the cluster, e.g. through `az connectedk8s proxy`):

```bash
sed "s#image: aio-modbus-simulator:0.2.0#image: <acr>.azurecr.io/aio-modbus-simulator:0.2.0#" deploy/modbus/k8s/simulator.yaml | kubectl apply -f -
```

Its address is `modbus-tcp://modbus-simulator.azure-iot-operations.svc.cluster.local:502`. Register map: [LOCAL-SIMULATION.md](LOCAL-SIMULATION.md#register-map).

## Step 4 — Create the device (operations experience)

1. Operations experience → your instance → **Devices** → **+ Create new** → **Device**. Name it, e.g. `line1-plc`.
2. On the **ModbusTcpConnector** tile select **+ New** to add an inbound endpoint:
   - **Endpoint name**: e.g. `plc-modbus`.
   - **Address**: `modbus-tcp://192.168.1.10:502` (or the simulator address above).
   - **Authentication**: *Anonymous* (Modbus TCP has no authentication).
   - **Advanced** tab (from `additionalConfigurationSchema`): **Unit id** (1 for most devices, the RTU address behind a gateway), **Response timeout (ms)**, **Connect timeout (ms)**, **Byte order**, **Word order**.
3. **Save**, **Next** through *Additional info*, **Create**.

CLI equivalent:

```bash
az iot ops ns device create --name line1-plc -i <aio-instance> -g <rg>
az iot ops ns device endpoint inbound add custom --device line1-plc --name plc-modbus \
  --endpoint-type Custom.ModbusTcp --version 1.0 --endpoint-address modbus-tcp://192.168.1.10:502 \
  --additional-config '{"unitId": 1, "timeoutMs": 2000}' -i <aio-instance> -g <rg>
```

## Step 5 — Create the asset and map registers

1. **Assets** → **+ Create new** → **Asset**. Select the inbound endpoint `plc-modbus`, give the asset a name (e.g. `line1-oven`) and optionally attributes. On the asset defaults you can set **Sampling interval (ms)** for all datasets.
2. **Datasets** → **Create dataset**:
   - **Dataset name**: e.g. `process`. Leave **Data source** empty (not used by this connector).
   - **Destination**: MQTT topic, e.g. `plant/line1/oven/process`, QoS 1, Retain *Never*; or a broker state store key.
   - **Sampling interval (ms)**, and optionally **Unit id**, **Byte/Word order**, **Max registers per request**, **Max bits per request**, **Max address gap**.
3. **List of registers** (the metadata renames *data points* to *registers*) → **+ Add register** for each value:

| Register name | Data source | Data type | Other fields |
| --- | --- | --- | --- |
| `temperature` | `hr:0` (or `40001`) | Float32 | Word order *LittleEndian* if the device is word-swapped |
| `humidity` | `hr:2` | UInt16 | Scale `0.1` |
| `running` | `hr:6` | Boolean | Bit `0` |
| `serialNumber` | `hr:10` | String | String length `8` |
| `pumpOn` | `coil:0` | Boolean | — |
| `flowRate` | `ir:0` | Float32 | Word order *LittleEndian* |

   Addressing rules (zero-based `hr:N` vs one-based `40001`), all data types and byte/word order are in [CONFIGURATION.md](CONFIGURATION.md).
4. **Save**, **Next**, **Create**.

Within a few seconds the Akri operator starts a pod named `modbus-tcp-connector-…` that samples the asset. With many registers, prefer the CLI import (`az iot ops ns asset custom datapoint import --input-file …`, JSON/YAML/CSV) or [deploy/modbus/New-SampleAsset.ps1](../../deploy/modbus/New-SampleAsset.ps1) as a template.

## Step 6 — Verify

- **Device status** in the operations experience: errors from the connector (unreachable device, configuration errors with the full list of problems) appear on the device / endpoint status.
- **Data**: subscribe to the dataset topic from inside the cluster, or create a data flow from the topic to Event Hubs / Fabric as in the Microsoft tutorial.
- **Schema registry**: the connector registers one JSON Schema per dataset, derived from the register definitions.
- **Logs**: `kubectl logs -n azure-iot-operations -l <connector pod label> -f`, or the pod whose name starts with the template name. Look for `Asset …, dataset …: N data point(s) from host:port in M request(s)`.

## Updating

- Changing the asset (add/edit registers) re-creates the samplers for that asset; no restart is needed.
- New connector version: bump `Version` in the csproj files and `version` in the metadata, run `Publish-Connector.ps1 -Protocol modbus -Version x.y.z`, then update or recreate the connector template with the new metadata reference.
