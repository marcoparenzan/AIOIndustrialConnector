# S7 configuration reference

The **S7 mode** of the connector reads Siemens S7 PLCs (S7-300, S7-400, S7-1200, S7-1500) over ISO-on-TCP. It is configured entirely through Azure Device Registry resources — the same fields whether you use the operations experience, the Azure CLI, Bicep, or Kubernetes manifests.

| ADR field | Level | What the connector reads |
| --- | --- | --- |
| Device → inbound endpoint **`endpointType`** | endpoint | Must be `Custom.SiemensS7` (version `1.0`). |
| Device → inbound endpoint **`address`** | endpoint | The PLC's IP address or host name. |
| Device → inbound endpoint **`additionalConfiguration`** | endpoint | JSON: CPU type, rack, slot, timeouts. Portal: *Advanced* tab of the endpoint. |
| Asset **`defaultDatasetsConfiguration`** | asset | JSON: defaults for every dataset of the asset. |
| Dataset **`datasetConfiguration`** | dataset | JSON: sampling interval, request sizing. |
| Dataset **`destinations`** | dataset | MQTT topic or broker state store key. Handled by the SDK. |
| Data point **`name`** | data point | Property name in the JSON payload. Must be unique in the dataset. |
| Data point **`dataSource`** | data point | S7 absolute address (`DB1.DBD0`). |
| Data point **`dataPointConfiguration`** | data point | JSON: data type and decoding. |

The cascade, the payload shape and the error handling are shared with the Modbus mode (see [ARCHITECTURE.md](../../ARCHITECTURE.md)); Modbus has its own reference in [../modbus/CONFIGURATION.md](../modbus/CONFIGURATION.md).

## Precedence

Every JSON configuration uses the same property names. The most specific level wins:

```text
data point  >  dataset  >  asset default  >  device endpoint  >  built-in default
```

## PLC preparation (read this first)

S7netplus talks the classic S7comm protocol. A PLC only answers it when:

| CPU | Required setting |
| --- | --- |
| S7-1200 / S7-1500 | TIA Portal → CPU properties → *Protection & Security* → *Connection mechanisms*: **Permit access with PUT/GET communication from remote partner**. Access level must allow it (full access, or at least without password for PUT/GET). |
| S7-1200 / S7-1500 | Every data block you read must be **non-optimized**: DB properties → *Attributes* → untick **Optimized block access**. Optimized DBs have no fixed byte offsets, so absolute addressing cannot work (the PLC answers with an error such as *object does not exist*). |
| S7-300 / S7-400 | Nothing special for PUT/GET on CPUs with an Ethernet interface (CP 343/443 or integrated PN). Make sure the connection resource is not exhausted by other partners. |

The connector is **read-only**: it never writes to the PLC.

## Endpoint address

| Accepted form | Meaning |
| --- | --- |
| `s7://192.168.0.1:102` | Recommended. |
| `s7-tcp://host`, `siemens-s7://host`, `tcp://host:102` | Also accepted. |
| `192.168.0.1`, `plc01:102` | Scheme optional; port defaults to **102** (ISO-on-TCP). |
| `[fd00::10]:102` | IPv6 literal, bracketed. |

Host names are resolved by the cluster DNS of the connector pod.

## Endpoint configuration (`additionalConfiguration`)

| Property | Default | Meaning |
| --- | --- | --- |
| `cpuType` | `S71500` | `S7200`, `S7200Smart`, `Logo0BA8`, `S7300`, `S7400`, `S71200`, `S71500`. Selects how the connection (TSAP) is built. |
| `rack` | `0` | Rack number of the CPU (0–7). |
| `slot` | `2` for `S7300`, otherwise `1` | Slot number of the CPU (0–31). |
| `timeoutMs` | 3000 | Response timeout per request. |
| `connectTimeoutMs` | 5000 | Timeout for establishing the connection. |

Typical rack/slot values:

| PLC | rack | slot |
| --- | --- | --- |
| S7-1200 / S7-1500 | 0 | 1 (0 also works) |
| S7-300 | 0 | 2 |
| S7-400 | depends on the hardware configuration (CPU slot) | e.g. 3 |
| S7-200 Smart / LOGO! 0BA8 | — (uses fixed TSAPs) | — |

Each distinct combination of address, CPU type, rack, slot and timeouts gets **one shared connection**, used by all datasets and assets of that PLC. S7 CPUs have a small, fixed number of connection resources shared with HMIs, TIA Portal and other clients, so this matters.

## Data point address (`dataSource`)

Siemens absolute notation as shown in TIA Portal / STEP 7 (case-insensitive):

| Form | Area | Width | Example |
| --- | --- | --- | --- |
| `DB<n>.DBX<byte>.<bit>` | Data block `n` | 1 bit | `DB1.DBX12.0` |
| `DB<n>.DBB<byte>` | Data block | byte (also: untyped start of 64-bit values and strings) | `DB1.DBB20` |
| `DB<n>.DBW<byte>` | Data block | word (2 bytes) | `DB1.DBW4` |
| `DB<n>.DBD<byte>` | Data block | double word (4 bytes) | `DB1.DBD0` |
| `M<byte>.<bit>`, `MB`, `MW`, `MD` | Memory (flags) | bit / byte / word / double word | `M0.1`, `MW10` |
| `I<byte>.<bit>`, `IB`, `IW`, `ID` | Inputs (process image) | same | `I0.0`, `IW2` |
| `Q<byte>.<bit>`, `QB`, `QW`, `QD` | Outputs (process image) | same | `Q0.1`, `QD4` |

- The German mnemonics `E…` (inputs) and `A…` (outputs) are accepted for `I…` and `Q…`.
- Offsets are **zero-based byte offsets**, exactly as in the PLC program. Bit numbers are 0–7.
- Counters (`C`) and timers (`T`) are not supported.
- The address width fixes the default type and which types fit it:

| Address width | Default `dataType` | Allowed `dataType` |
| --- | --- | --- |
| `X` / `M0.1` (bit) | `Boolean` | `Boolean` |
| `B` | `Byte` | anything — `B` is also the untyped start address for `Float64`, `Int64`, `UInt64` and `String` |
| `W` | `UInt16` | `Int16`, `UInt16`, `Boolean` (with `bit`) |
| `D` | `UInt32` | `Int32`, `UInt32`, `Float32`, `Boolean` (with `bit`) |

A type that does not fit its address (for example `Float32` on `DBW`) is a configuration error, not a silent reinterpretation.

## Data point configuration (`dataPointConfiguration`)

| Property | Type | Default | Meaning |
| --- | --- | --- | --- |
| `dataType` | string | from the address width | See table below. Both the common names and the Siemens names are accepted, case-insensitive. |
| `scale`, `offset` | number | none | Published value = raw × scale + offset. Numeric types only; the value becomes a floating point number. |
| `bit` | 0–31 | none | Read one bit of a byte/word/double word as Boolean (0 = least significant bit). Not combinable with a bit address. |
| `length` | 1–254 | — | Declared size *n* of an S7 `STRING[n]`. **Required** for `String`. |

There is no byte-order or word-order setting: S7 is always big endian.

### Data types

| `dataType` (aliases) | S7 type | Bytes | JSON type | Notes |
| --- | --- | --- | --- | --- |
| `Boolean` (`Bool`) | BOOL | bit / 1–4 | boolean | On `B`/`W`/`D`: the `bit` if set, otherwise *value ≠ 0*. |
| `Byte` (`USInt`) | BYTE, USINT | 1 | integer | |
| `SByte` (`SInt`) | SINT | 1 | integer | |
| `Int16` (`Int`) | INT | 2 | integer | |
| `UInt16` (`UInt`, `Word`) | UINT, WORD | 2 | integer | |
| `Int32` (`DInt`) | DINT | 4 | integer | |
| `UInt32` (`UDInt`, `DWord`) | UDINT, DWORD | 4 | integer | |
| `Int64` (`LInt`) | LINT | 8 | integer | S7-1500; address with `DBB`. Exact (no double rounding). |
| `UInt64` (`ULInt`, `LWord`) | ULINT, LWORD | 8 | integer | S7-1500; address with `DBB`. |
| `Float32` (`Real`) | REAL | 4 | number | IEEE 754. NaN/∞ are published as `null`. |
| `Float64` (`LReal`) | LREAL | 8 | number | IEEE 754; address with `DBB`. |
| `String` | STRING[n] | n + 2 | string | Reads the 2-byte header (maximum length, current length) and publishes the current characters (Latin-1). `WSTRING`, `TIME`, `DATE_AND_TIME` etc. are not supported; read them as integers if you need the raw value. |

`TIME` is milliseconds as `DInt`; `S5TIME`, `DATE` and `TOD` can be read as `Word`/`DWord` and converted downstream.

## Dataset configuration (`datasetConfiguration`, asset `defaultDatasetsConfiguration`)

| Property | Default | Meaning |
| --- | --- | --- |
| `samplingIntervalMs` | 1000 | Sampling period (min 10). Each dataset has its own timer; group points by required rate. |
| `maxBytesPerRead` | 200 | Largest merged request. Larger requests are fine: the client splits anything above the negotiated PDU size (typically 240 bytes on S7-300, 480 or more on S7-1200/1500) into several S7 requests transparently. |
| `maxGap` | 0 | Unused bytes the connector may read to merge two points into one request. Unlike Modbus, reading a gap **inside an existing data block** is harmless on S7, so a value like 16–64 usually saves requests. Reading beyond the end of a DB is an error. |

The connector merges points of the same area/data block that are adjacent or overlapping (or within `maxGap`) into one read. One request per `(area, DB)` group at least.

## Payload and errors

- Each sample publishes one flat JSON object; property order follows the data point order.
- If the PLC answers a request with an **error code** (data block does not exist, address beyond the DB size, access denied), the points of that request are published as `null` and the other points are still published. If **every** request is rejected, the sample fails.
- If the **connection** fails or times out, the connector retries once on a new connection; if that fails, the sample fails. Failed samples are reported on the device status in ADR (portal: device → *Status*), and the connector keeps trying at each interval.
- **Configuration errors** (bad address, type that does not fit the width, missing string `length`…) are all collected and reported together on the device status; the dataset is not sampled until fixed. Changing the asset re-creates the sampler; no restart needed.

## Troubleshooting

| Symptom | Likely cause |
| --- | --- |
| `Cannot connect to S7 PLC … (rack 0, slot 1)` | Wrong rack/slot/cpuType, port 102 blocked, or the PLC has no free connection resource. Try `slot` 0, 1 or 2. |
| `Received error from PLC: Object does not exist` (or an address-range error) | The data block does not exist, is shorter than the address, or is *optimized* (S7-1200/1500). Untick *Optimized block access* and recompile. |
| Connection succeeds but every read fails with `Received error from PLC: …` | PUT/GET not permitted in the CPU's protection settings (S7-1200/1500), or the access level of the CPU blocks it. |
| Values look wrong | Check the offset in the PLC (byte offsets, not word numbers) and the type; remember `DBW4` is bytes 4–5. |
| Connector works, TIA Portal is "kicked off" | Connection resources are exhausted; keep a single connector replica (leader election already ensures one active poller). |

## Full example (Kubernetes manifest form)

See [deploy/s7/k8s/asset.yaml](../../deploy/s7/k8s/asset.yaml). The same in CLI form: [deploy/s7/New-SampleAsset.ps1](../../deploy/s7/New-SampleAsset.ps1).
