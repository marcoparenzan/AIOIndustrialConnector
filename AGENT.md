# AGENT.md — deploying the industrial connector (Modbus or S7) to an Azure IoT Operations instance with `az`

Instructions for an automated agent. Follow the steps in order; stop and report at the first failure. Every step lists a verification command. Human-oriented background: [docs/modbus/PORTAL.md](docs/modbus/PORTAL.md) and [docs/s7/PORTAL.md](docs/s7/PORTAL.md); configuration references: [docs/modbus/CONFIGURATION.md](docs/modbus/CONFIGURATION.md) and [docs/s7/CONFIGURATION.md](docs/s7/CONFIGURATION.md).

The repository contains **one executable that runs one protocol per instance**. Each protocol is deployed on its own: its own image, metadata artifact, connector template, devices and assets. To deploy both, run steps 3–9 twice, once per protocol. Do not mix values between the two.

## 0. Facts about this repository

Values that depend on the protocol are marked with `<P>` = `modbus` or `s7`.

| Item | Modbus | S7 |
| --- | --- | --- |
| Mode (`CONNECTOR_PROTOCOL`, baked into the image) | `modbus` | `s7` |
| Image | `aio-modbus-connector:<version>` | `aio-s7-connector:<version>` |
| Simulator image | `aio-modbus-simulator:<version>` | `aio-s7-simulator:<version>` |
| Simulator project | `src/Modbus/ModbusSimulator/ModbusSimulator.csproj` | `src/S7/S7Simulator/S7Simulator.csproj` |
| Metadata file | `deploy/modbus/connector-metadata.json` | `deploy/s7/connector-metadata.json` |
| Metadata artifact | `<acr>.azurecr.io/aio-modbus-connector-metadata:<version>` | `<acr>.azurecr.io/aio-s7-connector-metadata:<version>` |
| Endpoint type | `Custom.ModbusTcp`, version `1.0` | `Custom.SiemensS7`, version `1.0` |
| Template name (default) | `modbus-tcp-connector` | `s7-connector` |
| Default device port | 502 | 102 |
| Sample manifests | `deploy/modbus/k8s/*.yaml` | `deploy/s7/k8s/*.yaml` |
| Sample asset script | `deploy/modbus/New-SampleAsset.ps1` | `deploy/s7/New-SampleAsset.ps1` |

| Common item | Value |
| --- | --- |
| Connector project (both protocols) | `src/Host/IndustrialConnector/IndustrialConnector.csproj` (.NET 10, Worker SDK); the protocol is chosen with `-p:ConnectorProtocol=<P>` at publish time, which names the image `aio-<P>-connector` and bakes in `CONNECTOR_PROTOCOL=<P>` |
| Version | `0.2.0` (csproj `<Version>`, metadata `version` and `tag`) |
| Metadata placeholder | `__REGISTRY__` must be replaced with `<acr>.azurecr.io` |
| Metadata artifact config media type | `application/vnd.microsoft.akri-connector.v1+json` |
| Scripted path | `deploy/Publish-Connector.ps1 -Protocol <P>`, `deploy/<P>/New-SampleAsset.ps1` (PowerShell 7) |
| Docker | Not required. Images are built and pushed by `dotnet publish -t:PublishContainer`. |

If the process starts without a protocol it exits with `No protocol selected. Available: modbus, s7.` That means an image or template was built or configured without the mode; fix the image (rebuild with `-p:ConnectorProtocol=<P>`), do not edit the running pod.

## 1. Inputs to collect

Ask the user for anything you cannot discover. Do not guess subscription or resource names.

| Variable | Meaning | Discovery |
| --- | --- | --- |
| `PROTOCOL` | `modbus` or `s7` (or both, one after the other) | Ask |
| `SUBSCRIPTION_ID` | Subscription of the AIO instance | `az account show --query id -o tsv` |
| `RESOURCE_GROUP` | Resource group of the AIO instance | `az iot ops list -o table` |
| `AIO_INSTANCE` | AIO instance name | `az iot ops list -o table` |
| `ACR_NAME` | Container registry the cluster pulls from (name without `.azurecr.io`) | Ask; check `az acr show -n $ACR_NAME` |
| `VERSION` | Image/metadata version | Default `0.2.0`; must be new if the content changed |
| `DEVICE_ADDRESS` | Device endpoint address | Ask. Modbus, e.g. `modbus-tcp://192.168.1.10:502`; S7, e.g. `s7://192.168.0.1:102`. Defaults are the in-cluster simulators `modbus-tcp://modbus-simulator.azure-iot-operations.svc.cluster.local:502` and `s7://s7-simulator.azure-iot-operations.svc.cluster.local:102` |
| `S7_CPU_TYPE`, `S7_RACK`, `S7_SLOT` | S7 only: CPU family and location | Ask. S7-1200/1500: `S71200`/`S71500`, rack 0, slot 1. S7-300: `S7300`, rack 0, slot 2 |

For S7, also confirm with the user that the PLC allows PUT/GET access and that the data blocks to read are **non-optimized** ([docs/s7/CONFIGURATION.md](docs/s7/CONFIGURATION.md#plc-preparation-read-this-first)). You cannot verify this with `az`.

## 2. Preflight checks

```bash
dotnet --list-sdks                         # need 10.0.x or later
az version --query '"azure-cli"'           # need 2.70+
az extension add --upgrade --name azure-iot-ops
oras version
az account set --subscription "$SUBSCRIPTION_ID"
az iot ops show --name "$AIO_INSTANCE" --resource-group "$RESOURCE_GROUP" --query "{name:name, version:properties.version, state:properties.provisioningState}" -o table
az acr show --name "$ACR_NAME" --query "{loginServer:loginServer, anonymousPull:anonymousPullEnabled}" -o table
```

Stop if the instance is not found or not `Succeeded`. If `anonymousPull` is false, the cluster needs an image pull secret for the template: inspect `az iot ops connector template create --help` for the pull-secret option and ask the user which secret to use.

Also run the local tests once; do not deploy a build that fails them:

```bash
dotnet test --solution AIOIndustrialConnector.slnx
```

## 3. Build and push the images

```bash
TOKEN=$(az acr login --name "$ACR_NAME" --expose-token --query accessToken -o tsv)
export DOTNET_CONTAINER_REGISTRY_UNAME=00000000-0000-0000-0000-000000000000
export DOTNET_CONTAINER_REGISTRY_PWORD="$TOKEN"

# Connector image for the chosen protocol (same project for both; the property selects the mode and the image name)
dotnet publish src/Host/IndustrialConnector/IndustrialConnector.csproj -c Release -r linux-x64 \
  -t:PublishContainer -p:ConnectorProtocol="$PROTOCOL" \
  -p:ContainerRegistry="$ACR_NAME.azurecr.io" -p:ContainerImageTag="$VERSION"

# Optional test device (pick the project of the protocol)
SIM_PROJECT=src/Modbus/ModbusSimulator/ModbusSimulator.csproj   # or src/S7/S7Simulator/S7Simulator.csproj
dotnet publish "$SIM_PROJECT" -c Release -r linux-x64 \
  -t:PublishContainer -p:ContainerRegistry="$ACR_NAME.azurecr.io" -p:ContainerImageTag="$VERSION"
```

Use `-t:` (not `/t:`) so Git Bash does not rewrite the switch as a path.

Verify:

```bash
az acr repository show-tags --name "$ACR_NAME" --repository "aio-$PROTOCOL-connector" -o tsv | grep -x "$VERSION"
```

## 4. Push the connector metadata

```bash
WORK=$(mktemp -d)
sed -e "s#__REGISTRY__#$ACR_NAME.azurecr.io#" -e "s#\"tag\": \"[^\"]*\"#\"tag\": \"$VERSION\"#" \
  "deploy/$PROTOCOL/connector-metadata.json" > "$WORK/connector-metadata.json"
echo '{}' > "$WORK/empty-config.json"
echo "$TOKEN" | oras login "$ACR_NAME.azurecr.io" --username 00000000-0000-0000-0000-000000000000 --password-stdin
( cd "$WORK" && oras push --config empty-config.json:application/vnd.microsoft.akri-connector.v1+json \
    "$ACR_NAME.azurecr.io/aio-$PROTOCOL-connector-metadata:$VERSION" connector-metadata.json:application/json )
```

Verify:

```bash
oras manifest fetch "$ACR_NAME.azurecr.io/aio-$PROTOCOL-connector-metadata:$VERSION" | grep akri-connector
```

Validate the metadata against <https://json.schemastore.org/aio-connector-metadata-11.0-preview.json> (Draft 7) **before** pushing it. The Modbus file validated with 0 errors in version 0.1.0; the S7 file has **not been validated yet**, so do this for S7 and report the result. If it fails, report the errors and stop; do not edit the metadata without telling the user.

## 5. Create the connector template

```bash
TEMPLATE=modbus-tcp-connector   # or s7-connector
az iot ops connector template create --name "$TEMPLATE" \
  --resource-group "$RESOURCE_GROUP" --instance "$AIO_INSTANCE" \
  --connector-metadata-ref "$ACR_NAME.azurecr.io/aio-$PROTOCOL-connector-metadata:$VERSION" \
  --replicas 1 --log-level info
```

Verify:

```bash
az iot ops connector template list --resource-group "$RESOURCE_GROUP" --instance "$AIO_INSTANCE" -o table
```

If a template with that name already exists, do **not** delete it. Report it and ask whether to update it or use a new name. The mode is baked into the image, so the template needs no environment settings; if the user wants to reuse one image for both protocols, tell them the template would need `CONNECTOR_PROTOCOL` or `--protocol` support, which has not been verified.

Steps 3–5 in one command (PowerShell 7):

```powershell
./deploy/Publish-Connector.ps1 -Protocol $PROTOCOL -AcrName $ACR_NAME -ResourceGroup $RESOURCE_GROUP -Instance $AIO_INSTANCE -Version $VERSION -IncludeSimulator
```

## 6. (Optional) Deploy the simulator in the cluster

Only if the user wants a test device and you have kubectl access (for Arc clusters: `az connectedk8s proxy -n <cluster> -g $RESOURCE_GROUP` in a background shell).

```bash
sed "s#image: aio-$PROTOCOL-simulator:0.2.0#image: $ACR_NAME.azurecr.io/aio-$PROTOCOL-simulator:$VERSION#" "deploy/$PROTOCOL/k8s/simulator.yaml" | kubectl apply -f -
kubectl rollout status "deployment/$PROTOCOL-simulator" -n azure-iot-operations --timeout=120s
```

## 7. Create a device and an asset

Scripted sample (PowerShell 7) matching the simulator map:

```powershell
./deploy/$PROTOCOL/New-SampleAsset.ps1 -ResourceGroup $RESOURCE_GROUP -Instance $AIO_INSTANCE -ModbusAddress $DEVICE_ADDRESS   # modbus
./deploy/$PROTOCOL/New-SampleAsset.ps1 -ResourceGroup $RESOURCE_GROUP -Instance $AIO_INSTANCE -S7Address $DEVICE_ADDRESS       # s7
```

### Modbus manual commands (bash)

```bash
az iot ops ns device create --name line1-plc -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns device endpoint inbound add custom --device line1-plc --name plc-modbus \
  --endpoint-type Custom.ModbusTcp --version 1.0 --endpoint-address "$DEVICE_ADDRESS" \
  --additional-config '{"unitId": 1, "timeoutMs": 2000}' -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom create --name line1-oven --device line1-plc --endpoint plc-modbus \
  -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom dataset add --asset line1-oven --name process \
  --config '{"samplingIntervalMs": 1000}' \
  --destination topic=plant/line1/oven/process qos=Qos1 retain=Never ttl=3600 \
  -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom datapoint add --asset line1-oven --dataset process --name temperature \
  --data-source hr:0 --config '{"dataType": "Float32"}' -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
```

Rules for Modbus data points (validate before creating them):

- `--data-source`: `hr:N`, `ir:N`, `coil:N`, `di:N` (zero-based) or Modicon `40001`/`30001`/`10001`/`00001` (one-based).
- `--config` keys: `dataType` (`Boolean|Int16|UInt16|Int32|UInt32|Int64|UInt64|Float32|Float64|String`), `byteOrder`, `wordOrder` (`BigEndian|LittleEndian`), `scale`, `offset`, `bit` (0–15, Boolean on registers), `length` (required for String), `unitId`.
- Coils and discrete inputs accept only `Boolean`. Names must be unique per dataset.

### S7 manual commands (bash)

```bash
az iot ops ns device create --name line1-plc -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns device endpoint inbound add custom --device line1-plc --name plc-s7 \
  --endpoint-type Custom.SiemensS7 --version 1.0 --endpoint-address "$DEVICE_ADDRESS" \
  --additional-config "{\"cpuType\": \"$S7_CPU_TYPE\", \"rack\": $S7_RACK, \"slot\": $S7_SLOT}" \
  -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom create --name line1-oven --device line1-plc --endpoint plc-s7 \
  -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom dataset add --asset line1-oven --name process \
  --config '{"samplingIntervalMs": 1000}' \
  --destination topic=plant/line1/oven/process qos=Qos1 retain=Never ttl=3600 \
  -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
az iot ops ns asset custom datapoint add --asset line1-oven --dataset process --name temperature \
  --data-source DB1.DBD0 --config '{"dataType": "Real"}' -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP"
```

Rules for S7 data points (validate before creating them):

- `--data-source`: `DB<n>.DBX<byte>.<bit>`, `DB<n>.DBB|DBW|DBD<byte>`, or `M`/`I`/`Q` with a bit (`M0.1`) or width (`MB5`, `MW10`, `QD4`); German `E`/`A` accepted. Offsets are zero-based **byte** offsets. No counters or timers.
- The width must fit the type: `X` Boolean; `W` Int16/UInt16; `D` Int32/UInt32/Float32; `B` Byte/SByte/String and 64-bit types. A mismatch is a configuration error.
- `--config` keys: `dataType` (`Boolean|Byte|SByte|Int16|UInt16|Int32|UInt32|Int64|UInt64|Float32|Float64|String`, or the Siemens names `Bool|USInt|SInt|Int|UInt|Word|DInt|UDInt|DWord|LInt|ULInt|LWord|Real|LReal`), `scale`, `offset`, `bit` (Boolean on a B/W/D address), `length` (required for String: the declared `STRING[n]`, 1–254). No byte or word order: S7 is big endian.
- Names must be unique per dataset.

You can test a map **before** creating it in Azure by writing the device/asset as Kubernetes manifests (see `deploy/<P>/k8s/*.yaml`) and running the probe (the probe needs network access to the device from where it runs):

```bash
dotnet run --project src/Tools/IndustrialProbe -- --protocol "$PROTOCOL" --device my-device.yaml --asset my-asset.yaml --address "$DEVICE_ADDRESS" --count 1
```

## 8. Verify end to end

```bash
az iot ops ns device show --name line1-plc -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP" --query "properties.status" -o json
az iot ops ns asset custom dataset list --asset line1-oven -i "$AIO_INSTANCE" -g "$RESOURCE_GROUP" -o table
# With kubectl access:
kubectl get pods -n azure-iot-operations | grep -E "modbus-tcp-connector|s7-connector"
kubectl logs -n azure-iot-operations <pod> --tail=50
```

Healthy log lines: `Starting in <P> mode (endpoint type …)`, then `Connected to Modbus TCP server host:port` or `Connected to S7 PLC host:port (rack R, slot S)`, and `Asset …, dataset …: N data point(s) from … in M request(s) every … ms`. Configuration errors appear in the device status as `Dataset '…' has K configuration error(s): …` — fix the listed data points; the connector picks up asset changes without a restart.

Typical S7 failures: `Cannot connect to S7 PLC …` (wrong rack/slot/cpuType, port 102 blocked, no free connection resource) and `Received error from PLC: …` on every read (PUT/GET not permitted, optimized data block, data block missing). See the troubleshooting table in [docs/s7/CONFIGURATION.md](docs/s7/CONFIGURATION.md#troubleshooting).

## 9. Rollback

- Remove only what you created in this run, and only after the user confirms: `az iot ops ns asset delete`, `az iot ops ns device delete`, `az iot ops connector template delete` (check `--help` for exact names), `kubectl delete -f deploy/<P>/k8s/simulator.yaml`.
- Do not delete images or metadata artifacts from the registry; report their tags instead.

## Guardrails

- Never run against a subscription, instance or registry the user has not named.
- Never overwrite an existing tag with different content; bump `VERSION`.
- Do not change the endpoint types (`Custom.ModbusTcp`, `Custom.SiemensS7`): existing devices depend on them.
- Never point a Modbus template at the S7 image or the reverse; one protocol per template.
- The connector is read-only towards devices, but polling too fast can overload PLCs (and exhaust S7 connection resources); keep `samplingIntervalMs` ≥ 500 unless the user asks otherwise, and keep one replica per template unless the user asks for failover.
- Do not claim the S7 mode works against real hardware: it has only been tested against a simulator ([OPENPOINTS.md](OPENPOINTS.md)). Report what you observe.
- Report exactly which steps were run, skipped or failed, with command output for failures.
