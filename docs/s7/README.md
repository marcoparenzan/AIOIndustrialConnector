# Siemens S7 mode

The S7 mode polls **Siemens S7 PLCs** (S7-300, S7-400, S7-1200, S7-1500; S7-200 Smart and LOGO! 0BA8 are exposed by the client library but untested here) over ISO-on-TCP, port 102, using the classic S7comm *read variable* service, and publishes each asset dataset as a JSON object to the AIO MQTT broker or the broker state store.

```text
 S7 PLC ──(ISO-on-TCP, S7comm read)──▶ connector pod (CONNECTOR_PROTOCOL=s7) ──MQTT──▶ AIO broker
                                            ▲
                  Azure Device Registry: Device (Custom.SiemensS7) + Asset (datasets, tags)
```

| | |
| --- | --- |
| Endpoint type | `Custom.SiemensS7`, version `1.0` |
| Container image | `aio-s7-connector:<version>` (same executable as Modbus, `CONNECTOR_PROTOCOL=s7` baked in) |
| Connector metadata | [deploy/s7/connector-metadata.json](../../deploy/s7/connector-metadata.json) |
| Library | [src/S7/IndustrialConnector.S7](../../src/S7/IndustrialConnector.S7), built on [S7netplus](https://github.com/S7NetPlus/s7netplus) (MIT) |
| Simulator | [src/S7/S7Simulator](../../src/S7/S7Simulator) (library + console/container app) |
| Tests | [tests/S7/IndustrialConnector.S7.Tests](../../tests/S7/IndustrialConnector.S7.Tests) |
| Manifests and scripts | [deploy/s7/](../../deploy/s7) |

## What it does

- Reads **data blocks, memory (M), inputs (I) and outputs (Q)** with Siemens absolute addresses (`DB1.DBD0`, `MW10`, `Q0.1`, German `E`/`A` accepted).
- Decodes **Boolean, Byte/SByte, Int16/UInt16, Int32/UInt32, Int64/UInt64, REAL, LREAL and STRING[n]**, single bits of byte/word/dword values, with linear **scale/offset**. Siemens type names (`Real`, `DInt`, `Word`, …) are accepted alongside the common ones.
- Merges adjacent data points of one data block into as few reads as possible; large reads are split by the client to the negotiated PDU size.
- One shared connection per PLC, reconnect on failure, per-request timeouts, partial results (`null` for points the PLC rejects).
- Validates the whole configuration up front and reports every error on the device status.
- Registers a JSON Schema per dataset and ships portal form definitions in the connector metadata.

## Documents

| Document | For |
| --- | --- |
| [CONFIGURATION.md](CONFIGURATION.md) | PLC preparation, addresses, data types, all settings, troubleshooting. |
| [PORTAL.md](PORTAL.md) | Registering the S7 connector in Azure and configuring a PLC in the operations experience. |
| [LOCAL-SIMULATION.md](LOCAL-SIMULATION.md) | Running the simulator and the connector code locally, without a cluster. |

Shared by all protocols: [../../ARCHITECTURE.md](../../ARCHITECTURE.md), [../../USAGE.md](../../USAGE.md), [../../AGENT.md](../../AGENT.md).

## Limits to know

Read-only; no counters/timers/`WSTRING`/date-time types; classic S7comm only (no S7CommPlus, no TLS) so the PLC must permit PUT/GET and use non-optimized data blocks; the endpoint advertises anonymous authentication only. See [../../OPENPOINTS.md](../../OPENPOINTS.md).
