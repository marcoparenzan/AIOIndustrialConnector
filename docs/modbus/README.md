# Modbus TCP mode

The Modbus mode polls **Modbus TCP** devices (PLCs, energy meters, drives, TCP-to-RTU gateways) and publishes each asset dataset as a JSON object to the AIO MQTT broker or the broker state store.

```text
 Modbus TCP device ──(FC 1/2/3/4)──▶ connector pod (CONNECTOR_PROTOCOL=modbus) ──MQTT──▶ AIO broker
                                          ▲
                  Azure Device Registry: Device (Custom.ModbusTcp) + Asset (datasets, registers)
```

| | |
| --- | --- |
| Endpoint type | `Custom.ModbusTcp`, version `1.0` |
| Container image | `aio-modbus-connector:<version>` (same executable as S7, `CONNECTOR_PROTOCOL=modbus` baked in) |
| Connector metadata | [deploy/modbus/connector-metadata.json](../../deploy/modbus/connector-metadata.json) |
| Library | [src/Modbus/IndustrialConnector.Modbus](../../src/Modbus/IndustrialConnector.Modbus), built on [NModbus](https://github.com/NModbus/NModbus) (MIT) |
| Simulator | [src/Modbus/ModbusSimulator](../../src/Modbus/ModbusSimulator) (library + console/container app) |
| Tests | [tests/Modbus/IndustrialConnector.Modbus.Tests](../../tests/Modbus/IndustrialConnector.Modbus.Tests) |
| Manifests and scripts | [deploy/modbus/](../../deploy/modbus) |

## What it does

- Reads **coils, discrete inputs, input registers and holding registers**.
- Decodes **Boolean, Int16/UInt16, Int32/UInt32, Int64/UInt64, Float32/Float64, ASCII String**, single **bits** of a register, with configurable **byte order** and **word order** and linear **scale/offset**.
- Merges adjacent data points into as few read requests as possible (configurable limits and gap).
- One shared TCP connection per device, reconnect on failure, per-request timeouts, partial results (`null` for points the device rejects with a Modbus exception).
- Validates the whole configuration up front and reports every error on the device status.
- Registers a JSON Schema per dataset and ships portal form definitions in the connector metadata.

## Documents

| Document | For |
| --- | --- |
| [CONFIGURATION.md](CONFIGURATION.md) | How to map registers: addresses, data types, byte/word order, all settings. |
| [PORTAL.md](PORTAL.md) | Registering the Modbus connector in Azure and configuring devices/assets in the operations experience. |
| [LOCAL-SIMULATION.md](LOCAL-SIMULATION.md) | Running the simulator and the connector code locally, without a cluster. |
| [../AIO-Industrial-Connector.pptx](../AIO-Industrial-Connector.pptx) | Presentation: shared architecture, Part 1 Modbus, Part 2 S7. |

Shared by all protocols: [../../ARCHITECTURE.md](../../ARCHITECTURE.md), [../../USAGE.md](../../USAGE.md), [../../AGENT.md](../../AGENT.md).

## Limits to know

Read-only; Modbus TCP only (no RTU framing, no TLS); polling only. See [../../OPENPOINTS.md](../../OPENPOINTS.md).
