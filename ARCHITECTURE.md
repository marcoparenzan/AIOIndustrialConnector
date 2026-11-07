# Architecture

## Context

```text
 Azure (ARM)                                   Edge cluster (Arc-enabled Kubernetes)
 ┌──────────────────────────────┐              ┌───────────────────────────────────────────────────────────┐
 │ AIO instance                 │   sync       │ Akri operator ── creates ──▶ connector pod (this code)     │
 │  └ connector templates ──────┼─────────────▶│   one per protocol            │      ▲                     │
 │ Device Registry namespace    │              │ ADR service ── devices/assets ┘      │ Modbus TCP | S7     │
 │  ├ Device + endpoint         │──────────────┼▶ (notifications)                     │                     │
 │  └ Asset + datasets + points │              │ MQTT broker ◀── JSON payloads ───────┤                     │
 └──────────────────────────────┘              │ State store ◀───────────────────────┤                     │
                                               │ Schema registry ◀── JSON Schema ─────┘                     │
 ACR: images + metadata artifacts              └───────────────────────────────────────────────────────────┘
                                                                                     │
                                                                          PLC / meter / RTU gateway
```

The Akri operator starts a connector pod from a connector template whenever devices of the template's endpoint type exist (`Custom.ModbusTcp`, `Custom.SiemensS7`). The connector SDK observes devices and assets through the ADR client, and for every asset calls our code to sample its datasets periodically. **Each pod runs exactly one protocol**; the templates are independent.

## Layers and folders

```text
src/
  Core/IndustrialConnector.Core      protocol-neutral: everything that is the same for every protocol
  Modbus/IndustrialConnector.Modbus  Modbus module  ─┐ depend on Core only (and their protocol library)
  S7/IndustrialConnector.S7          S7 module       ─┘
  Modbus/ModbusSimulator, S7/S7Simulator   independent device simulators
  Host/IndustrialConnector           executable: picks a module, wires the SDK worker
  Tools/IndustrialProbe              runs either module against YAML manifests, no cluster
tests/Core, tests/Modbus, tests/S7
```

Dependency rule: **Core knows no protocol; a protocol module knows Core and its own library; modules never reference each other; only the Host and the Probe reference several modules.** Adding a protocol therefore never touches the existing ones.

## What lives in Core, what in a protocol

| Concern | Core (once) | Protocol module (per protocol) |
| --- | --- | --- |
| Settings cascade and JSON reading | `IProtocolSettings<T>`, `SettingsJson` | the settings record (fields differ), `WithFallback` |
| Endpoint address | `TcpEndpoint.Parse(scheme list, default port)` | the scheme list and default port |
| Plan building: cascade, name uniqueness, interval, error collection | `DatasetPlanner<…>` (template method) | `ResolveLimits`, `TryCreatePoint`, `ValidatePoints`, `PlanBlocks` |
| Request merging | `RangePlanner` (group, sort, merge within gap and limit) | grouping key, address range of a point, size limit |
| Sampling: retry once, partial results, all-rejected rule, JSON payload | `PolledDatasetSampler`, `PayloadSerializer` | `BlockReader<TBlock>` (read one block, decode its points) |
| Numeric decoding | `NumericDecoder` (types, scale/offset, NaN → null) | byte/word order (Modbus), STRING framing and bits (S7) |
| Sampler factory (endpoint → plan → shared reader, faulted sampler) | `DatasetSamplerFactoryBase<TSettings,TEndpoint>` | parse hooks and `CreateReader` |
| JSON Schema registration | `MessageSchemaProviderBase`, `JsonSchemaBuilder` | build the plan |
| Connection lifecycle | `SerializedConnection<T>` (lazy connect, reset, one request at a time, timeouts) and `ClientPool` (one client per device) | connect, close, classify errors, the actual protocol calls |
| Errors | `ConfigurationException`, `DeviceRejectedException`, `DeviceCommunicationException` | subclasses with protocol details |
| Mode selection and hosting | `IProtocolModule`, `ProtocolCatalog`, leader election | one `…Protocol` class |

## Design patterns used (and why)

- **Facade — `IProtocolModule`** (`ModbusProtocol`, `S7Protocol`). The host knows one small interface per protocol: name, endpoint type, lock name and `AddServices`. Everything behind it (pool, factory, schema provider, planner) is private to the module. The Host stays a few lines long.
- **Template method — `DatasetPlanner`, `DatasetSamplerFactoryBase`, `MessageSchemaProviderBase`, `SerializedConnection`.** The skeleton (cascade, validation, error collection, logging, lifecycle) is written once and fixed; a protocol overrides only the steps that differ. This is what keeps two protocols behaving identically.
- **Strategy — `IBlockReader`.** The sampler is protocol-neutral and delegates "read this block and decode it" to the protocol's reader.
- **Registry — `ProtocolCatalog`.** Resolves a name or alias to a module; the single place that knows the available modes.
- **Adapter — `NModbusTcpClient`, `S7NetClient`.** Wrap third-party libraries behind `IModbusClient` / `IS7Client`, mapping their exceptions to the shared hierarchy, so nothing else depends on them and tests can use scripted fakes.
- **Pooling — `ClientPool`.** One connection per device, shared by all datasets.

Deliberately **not** done: a common "mega settings" type or one shared data type model. Modbus has byte/word order and unit ids, S7 has racks, slots and typed address widths; forcing one shape would make both worse. Each protocol keeps its own settings and point types and shares only the pipeline around them.

## Components

| Component | File | Responsibility |
| --- | --- | --- |
| Host | [Program.cs](src/Host/IndustrialConnector/Program.cs) | Selects the module, registers common SDK services and the module's services, runs `PollingTelemetryConnectorWorker`. |
| `PollingTelemetryConnectorWorker` | SDK | Leader election, ADR observation, one timer per dataset, forwarding payloads to MQTT/state store, device/asset status and health reporting. |
| `IProtocolModule` / `ProtocolCatalog` | [Hosting](src/Core/IndustrialConnector.Core/Hosting) | The facade and the mode selection (`--protocol`, `CONNECTOR_PROTOCOL`). |
| `DatasetPlanner` | [Planning/DatasetPlanner.cs](src/Core/IndustrialConnector.Core/Planning/DatasetPlanner.cs) | Template for plan building and validation. |
| `RangePlanner` | [Planning/RangePlanner.cs](src/Core/IndustrialConnector.Core/Planning/RangePlanner.cs) | Request merging. |
| `PolledDatasetSampler` | [Sampling/PolledDatasetSampler.cs](src/Core/IndustrialConnector.Core/Sampling/PolledDatasetSampler.cs) | Runs the blocks, retry and partial-failure policy, JSON payload. |
| `SerializedConnection` / `ClientPool` | [Connectivity](src/Core/IndustrialConnector.Core/Connectivity) | Connection lifecycle and sharing. |
| Modbus module | [src/Modbus/IndustrialConnector.Modbus](src/Modbus/IndustrialConnector.Modbus) | `ModbusAddress`, `ModbusSettings`, `ModbusDatasetPlan`, `ModbusValueDecoder`, `NModbusTcpClient`, `ModbusBlockReader`, `ModbusProtocol`. |
| S7 module | [src/S7/IndustrialConnector.S7](src/S7/IndustrialConnector.S7) | `S7Address`, `S7Settings`, `S7DatasetPlan`, `S7ValueDecoder`, `S7NetClient`, `S7BlockReader`, `S7Protocol`. |
| Connector metadata | [deploy/modbus](deploy/modbus/connector-metadata.json), [deploy/s7](deploy/s7/connector-metadata.json) | Endpoint type, supported auth, destinations, and the JSON schemas that drive the portal forms. |

## Sampling sequence (identical for both protocols)

```text
timer tick (per dataset)
  └ PolledDatasetSampler.SampleDatasetAsync
       for each read block (Modbus: unit, table, start, count | S7: area, DB, start byte, count)
         └ protocol IBlockReader → client (semaphore → connect if needed → protocol call → timeout)
              ├ ok                 → decode each point from its slice
              ├ device rejection   → points of the block = null, continue
              └ comm failure       → connection reset, abort the pass
       comm failure → one retry of the whole pass on a fresh connection
       all blocks rejected → throw
  └ SDK forwards bytes to MQTT topic / state store key, updates ADR status and health
```

## Design decisions

- **Configuration in ADR, not in the image.** Everything a user configures lives in endpoint/asset/dataset/data point JSON fields, so the portal and CLI are the only tools needed. One JSON shape per protocol at all levels keeps the cascade simple.
- **One executable, one mode per instance, mode baked into the image.** Keeps a single build and test pipeline, while Akri templates (and therefore pods, replicas, leader election locks and failure domains) stay per protocol. The mode is not inferred from ADR so a misconfigured endpoint type cannot make a pod poll the wrong protocol.
- **Protocol-native address notation.** Modbus: zero-based prefixes plus Modicon. S7: Siemens absolute (`DB1.DBD0`) exactly as shown in TIA Portal.
- **Validate everything up front, report everything at once.** Errors are collected and surfaced through the device status, because in the portal the user cannot see pod logs easily.
- **Request merging with `maxGap = 0` by default** in both protocols, for predictability. On S7, bridging gaps inside a data block is harmless and recommended; on Modbus many devices reject unmapped addresses.
- **One connection per device.** Modbus servers accept 1–4 connections, S7 CPUs a small fixed number; datasets and assets share a client and requests are serialized.
- **Partial results.** One rejected range must not hide all other values; its points become `null` and the schema allows `null`.
- **Values as `object` in a fixed set of CLR types** (`bool`, `long`, `ulong`, `double`, `string`) — 64-bit integers stay exact (a test caught a C# switch-expression widening them to `double`).
- **Libraries.** NModbus (Modbus) and S7netplus (S7), both MIT. Neither async API honours a `CancellationToken` fully, so cancellation abandons the request and resets the connection, and the transport timeout bounds every request.
- **S7 error mapping.** S7netplus reports a PLC item error as a plain `Exception` with a message starting `Received error from PLC`; the S7 client classifies exactly that as a device rejection (connection kept) and everything else as a communication failure (connection reset). This depends on S7netplus 0.20.0 wording; a test covers it.
- **No Docker dependency.** Images are built with the .NET SDK container support; base image `mcr.microsoft.com/dotnet/runtime:10.0`.

## Running outside AIO (design seam, not implemented)

The sampling pipeline (`IDatasetSampler`, plans, readers, clients) does not depend on MQTT, ADR or Akri: the [probe](src/Tools/IndustrialProbe) already runs it from YAML manifests with a console sink. A bare-metal host would add three replaceable parts around the same core, see [ROADMAP.md](ROADMAP.md): a **configuration source** (files instead of ADR, the YAML loader of the probe is the starting point), a **timer loop** (the probe's `SampleLoopAsync`), and a **sink** (console, file, plain MQTT broker). Everything protocol-specific is already shared.

## Adding a protocol

1. `src/<Name>/IndustrialConnector.<Name>`: reference Core and the protocol library.
2. Settings record implementing `IProtocolSettings<T>`; point record implementing `IDataPoint`; block record implementing `IReadBlock`.
3. A planner deriving from `DatasetPlanner` (+ `RangePlanner` for blocks), a `BlockReader<TBlock>`, a client built on `SerializedConnection<T>`, a pool on `ClientPool`.
4. A sampler factory deriving from `DatasetSamplerFactoryBase`, a schema provider deriving from `MessageSchemaProviderBase`, and a `…Protocol : IProtocolModule` facade.
5. Register it in the `ProtocolCatalog` in the Host (and the Probe's `ProbeProtocols`); add `deploy/<name>/` (metadata, manifests), `docs/<name>/`, tests and a simulator.

## Testing strategy

- Core: protocol selection, range planner, numeric decoder, payload serializer.
- Per protocol: address parsing, endpoint parsing, decoder, planner, plan validation, schema.
- Modbus sampler behaviour with a scripted fake client (exception responses, retry, short responses).
- End to end over TCP against **independent** simulators: the NModbus slave (Modbus) and a from-the-wire-format ISO-on-TCP/S7comm server (S7): full maps, shared connection, PDU splitting, rejected requests, unreachable device, device restart.
- `IndustrialProbe` runs the production factory and sampler with the deploy manifests for manual checks against a simulator or real hardware.

What is **not** covered by automated tests: the SDK worker, MQTT publishing, ADR status reporting, the Akri deployment (they need a cluster) and real S7 hardware or real Siemens firmware behaviour (see [OPENPOINTS.md](OPENPOINTS.md)).
