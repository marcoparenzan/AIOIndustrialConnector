# Open points

Honest list of what is unverified, limited, or still to decide.

## Not yet verified

| # | Item | Detail |
| --- | --- | --- |
| 1 | **No run on a live AIO cluster** | Code, tests, probe and container image builds were verified locally (Windows, .NET SDK 11 RC3 building `net10.0`, .NET 10.0.14 runtime). The connector host has not been started by the Akri operator, has not published to a real MQTT broker and has not reported status to ADR. Only the mode selection of the host was run (missing/unknown protocol exits with an error). The refactoring also changed the host wiring (module facade, leader election lock provider) that was not exercised against the SDK worker. |
| 2 | **S7 never tried on real hardware** | The S7 mode is tested against an *own* simulator written from the S7comm wire format. S7netplus talks to it correctly, but real CPUs differ (PDU sizes, connection resources, protection levels, optimized DBs). Treat the PLC-preparation notes in [docs/s7/CONFIGURATION.md](docs/s7/CONFIGURATION.md) as unverified until tried on S7-300/1200/1500. The simulator offers PDU 240; larger PDUs are negotiated by the library but not tested. |
| 3 | **How the mode reaches the container in a template** | The images bake in `CONNECTOR_PROTOCOL`, so no template setting is needed. Whether a connector template can pass environment variables/arguments (needed only if one image is reused for both modes) was not verified. |
| 4 | Portal flow | [Modbus](docs/modbus/PORTAL.md) and [S7](docs/s7/PORTAL.md) follow the Microsoft Learn article and CLI reference; the screens were not opened with these connectors. The S7 metadata (`deploy/s7/connector-metadata.json`) was not validated against the JSON schema (the Modbus one was in 0.1.0) — validate it before publishing (see AGENT.md step 4). |
| 5 | Scripts against Azure | `Publish-Connector.ps1` (now with `-Protocol`), both `New-SampleAsset.ps1` and `deploy-k3d.sh` parse cleanly but were not run against a subscription or cluster. The S7 container image tarball was built and its entrypoint and `CONNECTOR_PROTOCOL=s7` checked; the Modbus image was not rebuilt after the refactoring. |
| 6 | Pull secrets | The exact `az iot ops connector template create` flag for image pull secrets was not verified; use `--help`. The scripts assume ACR with anonymous pull. |
| 7 | Presentation | [docs/AIO-Industrial-Connector.pptx](docs/AIO-Industrial-Connector.pptx) was regenerated for 0.2.0 and checked by exporting every slide to images with PowerPoint; it was not proof-read slide by slide. The old Modbus-only deck was moved to `D:\Dev\BuildArtifacts/trash/AIOIndustrialConnector`. |

## Limitations (by design for 0.2.0)

| # | Item | Detail |
| --- | --- | --- |
| 8 | Read-only | No writes for either protocol; management actions not implemented. |
| 9 | Modbus TCP only / classic S7comm only | No RTU over TCP framing, no serial, no Modbus/TCP Security. S7: no S7CommPlus, no TLS, so the PLC must allow PUT/GET and use non-optimized data blocks. The metadata advertises only `anonymous` authentication; credentials configured on an endpoint are ignored with a warning. |
| 10 | S7 type coverage | No counters/timers, `WSTRING`, `TIME`/`DATE_AND_TIME`/`S5TIME` conversion (read raw as integers). S7-1500 64-bit types need a `DBB` address. |
| 11 | S7 error classification depends on S7netplus wording | A PLC item error arrives as a plain `Exception` with a message starting `Received error from PLC`; the client treats exactly that as "device rejected, connection fine". A change in the library wording would turn rejections into communication failures (connection reset + retry), degrading but not breaking behaviour. Covered by a test. |
| 12 | S7 cancellation and connect | S7netplus cancellation is advisory; a cancelled read waits up to `timeoutMs`+1 s before the connection is reset. Connecting is bounded with `WaitAsync(connectTimeoutMs)`; the abandoned attempt is closed. |
| 13 | One request per block (S7) | Several ranges are not packed into one S7 request, so a dataset with many separate small ranges costs many round trips; use `maxGap` to merge. |
| 14 | Polling only | Every sample is published, even if unchanged; no events/report-by-exception. |
| 15 | Floating point artifacts | Scaled values are plain doubles (e.g. 514 × 0.1 = `51.400000000000006`). Consumers should round; a `decimals` option is on the roadmap. |
| 16 | Clients are never evicted | The client pools keep one client per endpoint/connection-setting combination for the life of the process. Changing a device address leaves the old (idle) client in memory. Negligible for normal fleets. |
| 17 | Sequential reads per device | Requests to one device are serialized. A device with many datasets at short intervals can make sampling slower than the interval; the SDK `PeriodicTimer` then skips ticks. |
| 18 | Event schemas | `GetMessageSchemaAsync` for events returns null (no events). |
| 19 | `localhost` on Windows | The simulators listen on IPv4 only; use `127.0.0.1` locally. |
| 20 | Modbus data type validation changed slightly | The shared `DataType` enum adds `Byte`/`SByte` (needed by S7). On Modbus they are rejected with a clear message; all previously valid Modbus configurations behave as before. The Modbus portal schema still lists only the old types. |
| 21 | **Not runnable outside AIO yet** | The connector host needs the AIO environment (Akri-provided MQTT credentials and ADR service). The probe runs the sampling code anywhere but only prints to the console. Standalone execution with its own configuration source is planned: [ROADMAP.md](ROADMAP.md#next--standalone-bare-metal-execution-outside-aio) (effort estimate included). |

## Decisions to confirm

| # | Question | Current choice |
| --- | --- | --- |
| 22 | Endpoint type names | `Custom.ModbusTcp` and `Custom.SiemensS7`, version `1.0`. Changing them later requires recreating devices. |
| 23 | Image / metadata artifact names | `aio-modbus-connector` / `aio-s7-connector` and `<acr>/aio-<protocol>-connector-metadata:<version>`. Renamed from the single-protocol 0.1.0 layout only in the S7 addition (Modbus names unchanged). |
| 24 | Payload shape | Flat `{name: value}` object, no timestamp or quality fields (the SDK adds CloudEvents metadata on MQTT). |
| 25 | Target framework | `net10.0` (explicit project decision of the original repository, an exception to the net11-only rule of the Science suite; the Azure IoT Operations SDK and the container base image are .NET 10). |
| 26 | Configuration outside AIO | Open, to be analysed (see #21). |

## Tooling notes

- Tests require Microsoft.Testing.Platform mode of `dotnet test` (set in `global.json`), because xUnit v3 no longer supports the VSTest target on SDK 10+.
- `dotnet sln add` mis-handled dotted test project names; `AIOIndustrialConnector.slnx` is written by hand.
