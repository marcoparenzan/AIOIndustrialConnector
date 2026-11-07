# Roadmap

## 0.2.0 — current (preview): unified architecture + Siemens S7

- Shared `Core` library; Modbus refactored onto it (behaviour unchanged, all 72 Modbus tests pass).
- New S7 mode: data blocks, M/I/Q areas, Siemens absolute addresses, Boolean/Byte/Int/DInt/LInt/Real/LReal/STRING, bits, scale/offset, request merging with PDU splitting.
- One executable with per-instance protocol selection; two images, metadata artifacts and templates.
- Independent S7 simulator (library + container); probe and scripts take `-Protocol`.
- Documentation split per protocol.

## Next — standalone ("bare metal") execution outside AIO

Goal: run the same sampling code on a plain machine or VM, with configuration and output adapted to the environment. The core is ready (sampling does not depend on ADR/MQTT, see [ARCHITECTURE.md](ARCHITECTURE.md#running-outside-aio-design-seam-not-implemented)); what is missing is a host with three replaceable parts. **Rough effort, to be confirmed once the configuration approach is decided:**

| Part | Content | Effort |
| --- | --- | --- |
| Configuration source | Read devices/assets from YAML/JSON files (move the probe's `AdrYamlLoader` into a library; same schema as the ADR manifests, so a map can move between standalone and AIO unchanged), validate with the existing planners, reload on file change. | ~1 day |
| Run loop | One timer per dataset, lifetime/shutdown, per-dataset error reporting to log (probe's `SampleLoopAsync` is the base). | ~0.5 day |
| Sink | Console/file (trivial); plain MQTT 3.1.1/5 broker with host/credentials/TLS (library choice + reconnect/backpressure policy). | ~0.5 day / ~1–2 days |
| Packaging | Windows service / systemd unit, self-contained publish, settings via file + environment. | ~0.5–1 day |
| Tests + docs | End to end with the simulators; `docs/standalone`. | ~1 day |

About **4–6 days** for file configuration and MQTT/console output. Not included: central configuration management (a database or API as configuration source), schema registry and state store equivalents, remote status reporting, leader election between several bare-metal instances. These depend on how configuration will be handled outside AIO, which is the open decision.

## Later — validation on a live instance

- Run the full portal flow ([Modbus](docs/modbus/PORTAL.md), [S7](docs/s7/PORTAL.md)) on a real AIO instance; fix what differs.
- Try the S7 mode on S7-1200/1500 and S7-300 hardware; confirm the PUT/GET and non-optimized DB guidance.
- CI pipeline (build, test, image build) and multi-architecture images (`linux/arm64`).
- Optional value rounding (`decimals`) to avoid binary floating point artifacts such as `51.400000000000006`.

## Later — protocol coverage

Both protocols:

- Management actions (writes) through AIO management groups.
- Event groups: publish on change (dead band) instead of every interval.

Modbus:

- Modbus RTU over TCP (RTU framing on a TCP socket), Modbus/TCP Security (TLS, port 802).
- Bit-packed and BCD data types, `UInt8` high/low byte extraction.

S7:

- Counters and timers, `WSTRING`, `TIME`/`DATE_AND_TIME`/`S5TIME` conversion.
- Multi-item read requests (several ranges in one PDU) instead of one request per block.
- Optimized-DB symbolic access / S7CommPlus (S7-1200/1500) — depends on a suitable open library.
- Further protocols fit the same module structure (see [ARCHITECTURE.md](ARCHITECTURE.md#adding-a-protocol)): e.g. OPC DA/other fieldbuses, Allen-Bradley EtherNet/IP, Mitsubishi MC.

Both:

- Device discovery creating discovered assets in ADR; register/tag-map import from CSV in the probe.
