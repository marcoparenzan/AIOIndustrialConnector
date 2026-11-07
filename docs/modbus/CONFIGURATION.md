# Configuration reference

The **Modbus mode** of the connector is configured entirely through Azure Device Registry resources — the same fields whether you use the operations experience, the Azure CLI, Bicep, or Kubernetes manifests.

| ADR field | Level | What the connector reads |
| --- | --- | --- |
| Device → inbound endpoint **`endpointType`** | endpoint | Must be `Custom.ModbusTcp` (version `1.0`). |
| Device → inbound endpoint **`address`** | endpoint | Modbus TCP server. |
| Device → inbound endpoint **`additionalConfiguration`** | endpoint | JSON: unit id, timeouts, byte/word order defaults. Portal: *Advanced* tab of the endpoint. |
| Asset **`defaultDatasetsConfiguration`** | asset | JSON: defaults for every dataset of the asset. |
| Dataset **`datasetConfiguration`** | dataset | JSON: sampling interval, unit id, request sizing. |
| Dataset **`destinations`** | dataset | MQTT topic or broker state store key. Handled by the SDK. |
| Data point **`name`** | data point | Property name in the JSON payload. Must be unique in the dataset. |
| Data point **`dataSource`** | data point | Modbus address. |
| Data point **`dataPointConfiguration`** | data point | JSON: data type and decoding. |

## Precedence

Every JSON configuration uses the same property names. The most specific level wins:

```text
data point  >  dataset  >  asset default  >  device endpoint  >  built-in default
```

Example: `unitId` set to 2 on the endpoint applies to every point, unless a dataset or a single data point sets its own `unitId`.

## Endpoint address

| Accepted form | Meaning |
| --- | --- |
| `modbus-tcp://192.168.1.10:502` | Recommended. |
| `modbus://host:502`, `tcp://host:502` | Also accepted. |
| `192.168.1.10:1502`, `plc01` | Scheme optional; port defaults to **502**. |
| `[fd00::10]:502` | IPv6 literal, bracketed. |

Host names are resolved by the cluster DNS of the connector pod.

## Data point address (`dataSource`)

| Form | Table | Function code | Example |
| --- | --- | --- | --- |
| `hr:N`, `holding:N`, `4x:N` | Holding register | 3 | `hr:0` |
| `ir:N`, `input:N`, `3x:N` | Input register | 4 | `ir:10` |
| `coil:N`, `co:N`, `0x:N` | Coil | 1 | `coil:3` |
| `di:N`, `discrete:N`, `1x:N` | Discrete input | 2 | `di:7` |
| `4xxxx` / `4xxxxx` | Holding register, **one-based** Modicon | 3 | `40001` = `hr:0` |
| `3xxxx` / `3xxxxx` | Input register, one-based | 4 | `30010` = `ir:9` |
| `1xxxx` / `1xxxxx` | Discrete input, one-based | 2 | `10001` = `di:0` |
| `0xxxx` / `0xxxxx` | Coil, one-based | 1 | `00005` = `coil:4` |

`N` is the **zero-based protocol address** (0–65535), decimal or hex (`hr:0x10`). Device manuals mix both conventions: if a manual says "register 40001", use `40001` or `hr:0`; if it says "address 0", use `hr:0`. When values look shifted by one register, this is the first thing to check.

A multi-register value starts at `dataSource` and occupies the following registers (2 for 32-bit, 4 for 64-bit types, `length` for strings).

## Data point configuration (`dataPointConfiguration`)

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `dataType` | enum | `UInt16` for registers, `Boolean` for coils/discrete inputs | See table below. |
| `byteOrder` | `BigEndian` \| `LittleEndian` | `BigEndian` | Byte order inside each register. |
| `wordOrder` | `BigEndian` \| `LittleEndian` | `BigEndian` | Register order of 32/64-bit values. `LittleEndian` = "word swapped" (low word first). |
| `scale`, `offset` | number | none | Published value = raw × scale + offset. Numeric types only; the value becomes a floating point number. |
| `bit` | 0–15 | none | Read one bit of a register as Boolean (0 = least significant bit). |
| `length` | 1–125 | — | Registers of a `String` (2 ASCII characters each). Required for strings. |
| `unitId` | 0–255 | inherited | Modbus unit id for this point. |

### Data types

| `dataType` | Registers | JSON type | Notes |
| --- | --- | --- | --- |
| `Boolean` | 1 bit / 1 register | boolean | On a register: `bit` if set, otherwise *register ≠ 0*. |
| `Int16`, `UInt16` | 1 | integer | |
| `Int32`, `UInt32` | 2 | integer | |
| `Int64`, `UInt64` | 4 | integer | |
| `Float32` | 2 | number | IEEE 754. NaN/∞ are published as `null`. |
| `Float64` | 4 | number | IEEE 754. |
| `String` | `length` | string | Latin-1, trailing NULs and spaces trimmed. Word order does not apply. |

### Byte and word order cheat sheet

For a 32-bit value with bytes A B C D (A most significant):

| Device sends registers as | `byteOrder` | `wordOrder` | Common names |
| --- | --- | --- | --- |
| `AB CD` | BigEndian | BigEndian | ABCD, big endian (Modbus standard) |
| `CD AB` | BigEndian | LittleEndian | CDAB, word swap (very common in PLCs and meters) |
| `BA DC` | LittleEndian | BigEndian | BADC, byte swap |
| `DC BA` | LittleEndian | LittleEndian | DCBA, little endian |

If a float shows absurd values (e.g. `1.4E-41` or `-2.3E+38`), try `wordOrder: LittleEndian` first.

## Dataset configuration (`datasetConfiguration`, asset `defaultDatasetsConfiguration`)

| Property | Default | Meaning |
| --- | --- | --- |
| `samplingIntervalMs` | 1000 | Sampling period (min 10). Each dataset has its own timer; group points by required rate. |
| `unitId` | inherited | Unit id for all points of the dataset. |
| `byteOrder`, `wordOrder` | inherited | Defaults for all points of the dataset. |
| `maxRegistersPerRead` | 125 | Largest register request (protocol max 125). Lower it for devices with small buffers. |
| `maxBitsPerRead` | 2000 | Largest coil/discrete-input request (protocol max 2000). |
| `maxGap` | 0 | Unused addresses the connector may read to merge two points into one request. Keep 0 if the device rejects reads of unmapped addresses (exception 2). |

## Endpoint configuration (`additionalConfiguration`)

| Property | Default | Meaning |
| --- | --- | --- |
| `unitId` | 1 | Default unit id. For a TCP-to-RTU gateway use the RTU slave address. |
| `timeoutMs` | 3000 | Response timeout per request. |
| `connectTimeoutMs` | 5000 | TCP connect timeout. |
| `byteOrder`, `wordOrder` | BigEndian | Device-wide defaults. |

## Payload and errors

- Each sample publishes one flat JSON object; property order follows the data point order.
- If the device answers a request with a Modbus **exception** (e.g. *illegal data address*), the points of that request are published as `null` and the other points are still published. If **every** request is rejected, the sample fails.
- If the **connection** fails or times out, the connector retries once on a new connection; if that fails, the sample fails. Failed samples are reported on the device status in ADR (portal: device → *Status*), and the connector keeps trying at each interval.
- **Configuration errors** (bad address, unknown data type, `bit` on a coil, missing `length`…) are all collected and reported together on the device status; the dataset is not sampled until fixed. Changing the asset re-creates the sampler; no restart needed.

## Full example (Kubernetes manifest form)

See [deploy/modbus/k8s/asset.yaml](../../deploy/modbus/k8s/asset.yaml). The same in CLI form: [deploy/modbus/New-SampleAsset.ps1](../../deploy/modbus/New-SampleAsset.ps1).

The S7 protocol has its own reference: [../s7/CONFIGURATION.md](../s7/CONFIGURATION.md). The cascade, the payload shape and the error handling below are shared by all protocols (see [ARCHITECTURE.md](../../ARCHITECTURE.md)).
