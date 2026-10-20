# Smart-X IoT Mesh Gateway

Smart-X is a simulated ESP32 ingestion gateway and operator dashboard. It accepts mixed telemetry (soil moisture as `float`, active power as `int`, valve/actuator state as `bool`), validates nested campus locations, and stores optional hardware logs with AES-256 encryption.

This repository is one solution covering **Part 1** (ingestion) and **Part 2** (real-time processing, command stream, and recommendations). **Network Topology and Mesh Routing is not implemented.** The home menu shows that pillar as planned for the final POE.

There is no database. Runtime queues, live state, history, commands, faults, and recommendations exist only in process memory. Stopping the API discards that state (a bounded drain runs on graceful shutdown).

---

## Scope

### Part 1 — implemented

- Blazor home menu and **Sensor Data Ingestion and Telemetry** (`/ingestion`, `/register`, `/tools`).
- Minimal API ingest for `TelemetryPacket<float|int|bool>` and a combined `/api/telemetry/ingest` body.
- Custom bounded `IngestionBuffer<T>` for recent packets.
- Sensor registration with recursive `Facility -> Zone -> Sub-Zone` path checks.
- Multipart log/photo upload, encrypted on disk (AES-256).
- Operator overloads on `PowerMetric`, jagged batch history flattened to `List<double>`.
- Mesh health constellation on the ingest page (`GET /api/gateway/integrity`).
- Startup seed: 10 simulated nodes × 80 packets (`MeshSeedHostedService`, RNG seed 7321).

### Part 2 — implemented

- FIFO processing pipeline (`Queue<T>`) plus hosted worker.
- Priority alerts (`PriorityQueue<TElement,TPriority>`).
- Live device dictionary, timestamp-ordered history, unique faults, command undo stack.
- Command Stream UI at `/command-stream` (poll every 2 seconds; no SignalR).
- Deterministic `GET /api/runtime/recommendations` and **Suggested Actions** on the command stream page.

### Not implemented

- Network topology / mesh routing UI or algorithms.
- SignalR / WebSockets.
- Durable storage, authentication, or physical hardware.
- Automatic command execution from recommendations.

---

## Solution structure

Solution file: `SmartX.slnx`


| Project         | Role                                                                                          |
| --------------- | --------------------------------------------------------------------------------------------- |
| `SmartX.Api`    | ASP.NET Core Minimal API (`net10.0`), OpenAPI, hosted seed + processing worker                |
| `SmartX.Client` | Blazor WebAssembly dashboard (`net10.0`)                                                      |
| `SmartX.Shared` | Shared models (`TelemetryPacket<T>`, runtime DTOs, `IngestionBuffer<T>`, recommendation DTO)  |
| `SmartX.Tests`  | xUnit tests (API + shared logic; `Microsoft.AspNetCore.Mvc.Testing`)                          |


Client pages (actual `@page` routes):

- `/` — three-pillar home
- `/ingestion` — Part 1 telemetry
- `/register` — sensors and encrypted upload
- `/tools` and `/gateway-lab` — meters / deployment lab
- `/command-stream` — Part 2 dashboard
- `/not-found`

---

## Technologies (from project files)

- Target framework: **net10.0**
- `Microsoft.AspNetCore.OpenApi` **10.0.12** (API)
- `Microsoft.AspNetCore.Components.WebAssembly` **10.0.12** (client)
- xUnit **2.9.3**, `Microsoft.NET.Test.Sdk` **17.14.1**, `xunit.runner.visualstudio` **3.1.4**
- `Microsoft.AspNetCore.Mvc.Testing` **10.0.12**

Prerequisite: [.NET 10 SDK](https://dotnet.microsoft.com/download). Optional: Docker Desktop if you use the compose files.

---

## Restore, build, test

From the repository root:

```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
dotnet test SmartX.Tests/SmartX.Tests.csproj
```

Filter recommendation tests only:

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter RecommendationEngineTests
```

---

## Run locally

Verified from `SmartX.Api/Properties/launchSettings.json`, `SmartX.Client/Properties/launchSettings.json`, and `SmartX.Client/wwwroot/appsettings.json`.

| Process | `http` profile URL |
| ------- | ------------------ |
| API     | [http://localhost:5057](http://localhost:5057) |
| Client  | [http://localhost:5197](http://localhost:5197) |

HTTPS profiles also exist (`https://localhost:7204` API, `https://localhost:7091` client). CORS allows `http://localhost:5197`, `https://localhost:7091`, and `http://localhost:8088`.

**Terminal 1 — API**

```bash
dotnet run --project SmartX.Api/SmartX.Api.csproj --launch-profile http
```

Wait for `Now listening on: http://localhost:5057`.

OpenAPI: [http://localhost:5057/openapi/v1.json](http://localhost:5057/openapi/v1.json)

**Terminal 2 — client**

```bash
dotnet run --project SmartX.Client/SmartX.Client.csproj --launch-profile http
```

Open [http://localhost:5197](http://localhost:5197). The WASM app calls the API at `http://localhost:5057` (`ApiBaseUrl`). Start the API first.

If the API port changes, edit `SmartX.Client/wwwroot/appsettings.json` and restart the client.

---

## How to demonstrate the application

1. Open the home page. **Sensor Data Ingestion** and **Real-Time Command Stream** are enabled. **Network Topology and Mesh Routing** is labelled *Planned for Final POE — not implemented* and is not a link. Ingestion, Sensors, and Meters also share short in-module links (`PartLinks`); Command Stream is reached from Home.
2. **Ingest** (`/ingestion`): pick a seeded node, send moisture (0–100), power (0–20000 W), or valve state. The mesh map and “Latest packets” update. Invalid moisture is rejected in the browser and by the API.
3. **Sensors** (`/register`): register MAC, location (`Facility A -> Zone 1 -> Sub-Zone B`), category. Optional file upload (max 10 MB).
4. **Meters** (`/tools`): `Meter1 + Meter2`, recursive location check, jagged batch flatten.
5. **Command stream** (`/command-stream`):
   - Runtime stats (pending/capacity, processed, rejected/errors, dead-lettered, faults, device count).
   - **Suggested Actions** from `/api/runtime/recommendations`.
   - Search name/MAC; filter category, seen/unseen/alerting, actuator ON/OFF.
   - Live devices: last seen, reading, commanded vs reported actuator, alert flag.
   - Simulated actuator ON/OFF via `POST /api/commands` (actuators only). The UI states there is no physical pump.
   - Command audit and **Undo last** (`POST /api/commands/undo`). Empty/stale undo shows the API `outcome`.
   - Processed history from `GET /api/runtime/history` (not Part 1 `/api/telemetry/history`).
   - Active faults (`CODE:MAC`) with Resolve; processing journal (backend order; the UI does not run the priority queue).
   - Polls about every 2 seconds while the page is open; failed refresh keeps the last successful snapshot and shows a stale banner.

**Trigger a recommendation:** after the API is running, POST a **current-UTC** `%VWC` reading of `12` for `24:6F:28:A1:B2:C3` (NFT-Gutter-A). If the zone solenoid is OFF, wait for the worker then open Command Stream or `GET /api/runtime/recommendations`. Use a timestamp that is not older than the device’s last-seen time, or live state will not update.

HTTP 200 on ingest means the packet was accepted into the Part 1 buffer **and** the FIFO. HTTP **429** means the processing queue is full; the packet is not stored.

---

## Part 2 practical assessment guide

Copy-and-paste steps for a marker. JSON property names match ASP.NET Core’s default camelCase serialisation. Enums such as `category` on live devices serialise as numbers (`0` Environmental, `1` PowerConsumption, `2` Actuator) unless a converter is added (none is registered).

**Seeded demonstration MACs** (from `MeshSeedHostedService`; these exist after API start):

| MAC | Role |
| --- | --- |
| `24:6F:28:A1:B2:C3` | NFT-Gutter-A moisture (Facility A → Zone 1) |
| `24:6F:28:A1:B2:C4` | NFT-Gutter-B moisture (same zone) |
| `24:6F:28:A1:B2:D1` | Irrigation solenoid (same zone) |
| `30:AE:A4:10:01:01` | Bay-1 CT clamp |
| `30:AE:A4:10:01:02` | Bay-2 CT clamp |

Unit-test-only MACs such as `24:6F:28:AA:00:01` are **not** seeded. Do not use them against a running API unless you register them first.

Page refresh does **not** clear API memory. Restart the API process to reset queues, history, faults, commands, and recommendations. After any source change, stop and start the API again so you are not assessing an old process.

### 1. Environment and setup

Prerequisite: .NET 10 SDK. Solution: `SmartX.slnx`. Projects: `SmartX.Api`, `SmartX.Client`, `SmartX.Shared`, `SmartX.Tests`.

```bash
dotnet restore SmartX.slnx
dotnet build SmartX.slnx
dotnet test SmartX.Tests/SmartX.Tests.csproj
```

**Terminal 1 — API** (`SmartX.Api/Properties/launchSettings.json` profile `http`):

```bash
dotnet run --project SmartX.Api/SmartX.Api.csproj --launch-profile http
```

Wait for `Now listening on: http://localhost:5057`.

**Terminal 2 — Blazor client** (`SmartX.Client/Properties/launchSettings.json` profile `http`; `SmartX.Client/wwwroot/appsettings.json` `ApiBaseUrl` is `http://localhost:5057`):

```bash
dotnet run --project SmartX.Client/SmartX.Client.csproj --launch-profile http
```

Client: [http://localhost:5197](http://localhost:5197). Start the API first. HTTPS profiles: API `https://localhost:7204`, client `https://localhost:7091`.

Ready check:

```bash
curl -sS http://localhost:5057/
```

Expect HTTP 200 and JSON with `"status":"ready"`.

### 2. FIFO `Queue<T>`

**Primary proof (repeatable):** unit tests. The hosted worker drains the queue continuously, and startup seed enqueues 800 packets, so a live journal is **not** a reliable FIFO proof by itself.

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~PendingQueueTests
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Ingest_throws_when_the_processing_queue_is_full
```

`PendingQueueTests.Fifo_order_is_preserved` enqueues A, B, C and dequeues in that order from `Queue<TelemetryWorkItem>`. `PendingQueueTests.Full_queue_rejects_without_discarding_existing_items` and `Concurrent_enqueue_never_exceeds_capacity` cover capacity. `CommandAndIngestionTests.Ingest_throws_when_the_processing_queue_is_full` uses a harness queue of capacity **1** and expects `QueueFullException`.

**HTTP ingest routes** (`Program.cs`):

- `POST /api/telemetry/ingest-float` — body `TelemetryPacket<float>`: `deviceId`, `timestamp`, `value`, `unit`, `metricName` (optional `rssiDbm`).
- `POST /api/telemetry/ingest-int` — `value` is watts (`int`).
- `POST /api/telemetry/ingest-bool` — `value` is boolean.
- `POST /api/telemetry/ingest` — `TelemetryIngestRequest`: `deviceId`, `valueKind` (`float` / `int` / `bool`), `numericValue` and/or `booleanValue`, `metricName`, `unit`. Combined ingest stamps `timestamp` as **server UTC now**.

Fixture-style float body (same shape as `Part1ApiContractTests` / `SmartX.Api.http`):

```bash
curl -sS -w "\nHTTP:%{http_code}\n" -H 'Content-Type: application/json' \
  -d '{"deviceId":"24:6F:28:A1:B2:C4","timestamp":"'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'","value":45.0,"metricName":"soil_moisture","unit":"%VWC"}' \
  http://localhost:5057/api/telemetry/ingest-float
```

HTTP **200** means the packet was accepted into the Part 1 buffer **and** `PendingTelemetryQueue`. It does **not** mean the worker has finished `ApplyTelemetry`.

**HTTP 429:** `TryIngest` maps `QueueFullException` to status 429 and JSON `message`: `The telemetry processing queue is full. The packet was not accepted.` Production capacity is **2048**. The worker is always draining, so filling the live API to 429 is not a practical marker step. Use the unit tests above as the 429/capacity evidence.

**Optional live journal inspect** (ambiguous if `pendingCount` is not 0, or if seed traffic is still draining):

```bash
curl -sS http://localhost:5057/api/runtime/stats
```

Wait until `"pendingCount":0`. Then POST three **in-band** packets (avoid extra faults) in this order, using **current UTC** so live state accepts them:

```bash
TS=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"30:AE:A4:10:01:01","timestamp":"'"$TS"'","value":1111,"metricName":"active_power","unit":"W"}' \
  http://localhost:5057/api/telemetry/ingest-int
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"30:AE:A4:10:01:02","timestamp":"'"$TS"'","value":1222,"metricName":"active_power","unit":"W"}' \
  http://localhost:5057/api/telemetry/ingest-int
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"24:6F:28:A1:B2:C4","timestamp":"'"$TS"'","value":45.0,"metricName":"soil_moisture","unit":"%VWC"}' \
  http://localhost:5057/api/telemetry/ingest-float
```

```bash
curl -sS http://localhost:5057/api/runtime/journal
```

Each processed packet appends `kind` `TELEMETRY` and `detail` `{sequence}:{NORMALISED-MAC}` (see `TelemetryProcessingEngine.ApplyTelemetry`). The journal list is oldest-first and capped at **256** (`ProcessingJournal`). After seed, early entries have already fallen off. Compare only the **new** `TELEMETRY` lines for those three MACs: sequence numbers should increase in ingest order. If the worker processed each packet before the next POST returned, that is still FIFO, but you did not observe a backed-up queue.

### 3. Critical-alert `PriorityQueue`

There is **no** public HTTP route to inject an `AlertWorkItem`. Do not invent one.

**Rules** (`AlertPriority` + `AlertPriorityInbox` + `ThresholdEvaluator`):

- Critical = `0`, warning = `1` (min-heap: lower number first).
- Equal priority keeps enqueue sequence.
- `ProcessOnce` dequeues the alert inbox **before** the telemetry FIFO. It does **not** interrupt an `ApplyTelemetry` / `ApplyAlert` already running.
- VWC below 28% or above 82% (unit must contain `VWC`) → critical `VWC-LOW` / `VWC-HIGH`. Power above 3500 W → critical `POWER-CRITICAL`; above 2800 W → warning `POWER-HIGH`.

**Primary proof:**

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~PriorityAlertTests
```

`Critical_alert_is_processed_before_pending_fifo_telemetry` enqueues two FIFO items, then an alert, then `Drain()`. Journal kinds must have `ALERT` at a lower index than `TELEMETRY`. `Critical_dequeues_ahead_of_warning` and `Equal_priority_alerts_keep_enqueue_order` cover ordering inside the priority queue.

**Journal fields:** `atUtc`, `kind`, `detail`. Alert apply records `kind` `ALERT` and `detail` `{priority}:{faultId}:{alertSequence}` (example: `0:VWC-LOW:24:6F:28:A1:B2:C3:1`). Telemetry records `TELEMETRY` and `{packetSequence}:{mac}`.

On a **running** API, ingesting a dry `%VWC` packet both enqueues FIFO work and may enqueue an alert at ingest time (`OfferToPipeline`). You may see an `ALERT` line near a `TELEMETRY` line, but seed traffic and the 256-entry cap make this a weak live demo. Use `PriorityAlertTests` as the ordering proof.

### 4. Stack-based command undo

DTO `CommandRequest`: `macAddress`, `actuatorOn`, `note`. Actuators only (solenoid MAC below is seeded as `Actuator`).

Inspect before:

```bash
curl -sS http://localhost:5057/api/runtime/devices/24:6F:28:A1:B2:D1
```

Note `actuatorOn`, `commandedActuatorOn`, `reportedActuatorOn`.

Command ON (HTTP 200 `accepted` true, or 400 with the same `CommandResult` shape if rejected):

```bash
curl -sS -w "\nHTTP:%{http_code}\n" -H 'Content-Type: application/json' \
  -d '{"macAddress":"24:6F:28:A1:B2:D1","actuatorOn":true,"note":"assessment-open"}' \
  http://localhost:5057/api/commands
```

GET the device again. Expect `commandedActuatorOn` / `actuatorOn` true and `latestDisplay` `ON`. Commands **bypass** the telemetry FIFO.

```bash
curl -sS http://localhost:5057/api/commands
curl -sS http://localhost:5057/api/runtime/journal
```

Audit list includes the command (`isUndo` false). Journal `kind` `COMMAND`, `detail` `{mac}:{previous}->{True}` (Boolean.ToString).

Undo is `POST /api/commands/undo` with **no body**:

```bash
curl -sS -w "\nHTTP:%{http_code}\n" -X POST http://localhost:5057/api/commands/undo
```

Expect `outcome` `Undone.`, `command.isUndo` true. Device should return to the previous commanded state. Journal `kind` `UNDO`. Empty stack returns HTTP **400** and `accepted` false (`No command available to undo.`).

**Tests:**

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Undo_restores_recorded_previous_actuator_state
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Rejected_commands_do_not_enter_the_undo_stack
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Stale_device_state_blocks_undo_without_dropping_the_stack
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Overlapping_undos_cannot_consume_the_same_record
```

(`Overlapping_undos_cannot_consume_the_same_record` is in `CorrectnessFixesTests`.)

### 5. Dictionary live-device lookup

```bash
curl -sS http://localhost:5057/api/runtime/devices
curl -sS http://localhost:5057/api/runtime/devices/24:6F:28:A1:B2:C3
curl -sS -w "\nHTTP:%{http_code}\n" http://localhost:5057/api/runtime/devices/24-6f-28-a1-b2-c3
```

`SensorRegistry.NormalizeMac` trims, replaces `-` with `:`, and uppercases. Both MAC spellings should return the same `macAddress` `24:6F:28:A1:B2:C3` (HTTP 200). Unknown MAC: HTTP **404**.

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Dictionary_lookup_uses_normalised_mac
```

### 6. SortedDictionary history

`GET /api/runtime/history?take=10&mac=24:6F:28:A1:B2:C3`

Query parameters actually bound: `take` (default 40, clamped 1–500) and optional `mac`. This is **not** `GET /api/telemetry/history` (Part 1 jagged-batch flatten).

Response items: `timestampUtc`, `deviceId`, `valueKind`, `displayValue`, `metricName`, `unit`. `Latest` returns newest-first. Store order is `HistoryKey` (UTC timestamp, then monotonic sequence). Capacity **2000**; oldest keys dropped. Live dictionary ignores older-than-last-seen packets; history still stores them.

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~History_orders_by_timestamp_then_sequence
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~History_bounds_retained_entries
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Older_readings_remain_in_timestamp_ordered_history
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Newer_live_state_is_not_overwritten_by_older_telemetry
```

### 7. HashSet duplicate-fault suppression

Use **current UTC** timestamps (seeded last-seen is “now” at boot; older packets will not update live state or raise from `ApplyTelemetry`).

```bash
NOW=$(date -u +%Y-%m-%dT%H:%M:%SZ)
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"24:6F:28:A1:B2:C3","timestamp":"'"$NOW"'","value":12.0,"metricName":"soil_moisture","unit":"%VWC"}' \
  http://localhost:5057/api/telemetry/ingest-float
sleep 1
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"24:6F:28:A1:B2:C3","timestamp":"'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'","value":10.0,"metricName":"soil_moisture","unit":"%VWC"}' \
  http://localhost:5057/api/telemetry/ingest-float
sleep 1
curl -sS http://localhost:5057/api/runtime/faults
```

Expect a **single** `VWC-LOW:24:6F:28:A1:B2:C3` string (HashSet key `CODE:MAC`). A second dry packet must not add a second identical key. `IRR-DEADLOCK:24:6F:28:A1:B2:C3` may also appear if the zone solenoid is OFF (coupling, still unique). Repeated telemetry still increments processed/history counts; only the fault set is deduplicated.

Resolve so later demos are not stuck on these keys (`FaultResolveRequest`: `code`, `macAddress`):

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"code":"VWC-LOW","macAddress":"24:6F:28:A1:B2:C3"}' \
  http://localhost:5057/api/runtime/faults/resolve
curl -sS -H 'Content-Type: application/json' \
  -d '{"code":"IRR-DEADLOCK","macAddress":"24:6F:28:A1:B2:C3"}' \
  http://localhost:5057/api/runtime/faults/resolve
```

HTTP 200 body: `resolved` (bool), `active` (remaining keys). If the next processed packet is still dry and the valve is still OFF, the engine may raise the faults again. To isolate, ingest in-band VWC (for example `50`) after resolve, or restart the API.

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~HashSet_prevents_duplicate_active_faults_and_allows_resolve
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~Engine_does_not_raise_the_same_fault_twice
```

### 8. Predictive Action and Recommendation Engine

`GET /api/runtime/recommendations` returns an array of `RecommendationDto`: `id`, `category`, `severity`, `deviceId`, `relatedDeviceId`, `title`, `explanation`, `suggestedAction`, `evidence`, `evidenceUtc`, `evidenceCount`.

This is **not** machine learning and does **not** use Command Stream search text. Insights never call `POST /api/commands`.

| `category` | Meaning |
| --- | --- |
| `threshold` | **Current** live reading vs bands. Skipped if that `CODE:MAC` is already in the HashSet, or last-seen older than 6 hours |
| `coupling` | **Current** dry VWC and a same-zone actuator that is OFF |
| `diagnostic` | Dry VWC with **no** zone-paired actuator |
| `trend` | Last 4 numeric history points (VWC span ≥ 5 or rising power) |
| `pattern` | **Historical:** ≥ 3 accepted non-undo ON commands while a zone-paired VWC was below 28% at that command timestamp |

**Reset:** restart the API. Refreshing [http://localhost:5197/command-stream](http://localhost:5197/command-stream) only reloads the WASM page.

**Coupling / threshold (live state), after API restart:**

```bash
curl -sS -H 'Content-Type: application/json' \
  -d '{"deviceId":"24:6F:28:A1:B2:C3","timestamp":"'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'","value":12.0,"metricName":"soil_moisture","unit":"%VWC"}' \
  http://localhost:5057/api/telemetry/ingest-float
sleep 2
curl -sS http://localhost:5057/api/runtime/recommendations
curl -sS http://localhost:5057/api/runtime/devices/24:6F:28:A1:B2:D1
```

If the solenoid is OFF, expect `id` `coupling-irr:24:6F:28:A1:B2:C3:24:6F:28:A1:B2:D1`, `severity` `critical`, evidence naming VWC and the actuator, `suggestedAction` telling the operator to use the command panel. `threshold:VWC-LOW:…` appears only when `VWC-LOW` is **not** already an active fault (resolve first, then GET recommendations while the live reading is still 12).

**Command-pattern (historical) — three qualifying ON commands:**

Keep Gutter-A dry, then command the solenoid ON three times. Pattern matching joins `GET /api/runtime/history` by timestamp (`LatestNumericAt`); each ON needs a dry `%VWC` sample **at or before** `appliedAtUtc`.

```bash
for i in 1 2 3; do
  curl -sS -H 'Content-Type: application/json' \
    -d '{"deviceId":"24:6F:28:A1:B2:C3","timestamp":"'"$(date -u +%Y-%m-%dT%H:%M:%SZ)"'","value":15.0,"metricName":"soil_moisture","unit":"%VWC"}' \
    http://localhost:5057/api/telemetry/ingest-float
  sleep 1
  curl -sS -H 'Content-Type: application/json' \
    -d '{"macAddress":"24:6F:28:A1:B2:D1","actuatorOn":true,"note":"pattern-'"$i"'"}' \
    http://localhost:5057/api/commands
  sleep 1
done
curl -sS http://localhost:5057/api/runtime/recommendations
```

Look for `id` `pattern-on-when-dry:24:6F:28:A1:B2:D1`, `category` `pattern`, `evidenceCount` ≥ 3. Suggested action is diagnostic (“operator habit”), not an automatic command. Coupling will **not** apply while the valve remains ON.

Undo or command `actuatorOn` false when finished. Restart the API if you need a clean slate.

```bash
dotnet test SmartX.Tests/SmartX.Tests.csproj --filter FullyQualifiedName~RecommendationEngineTests
```

Includes `Three_dry_then_on_commands_produce_a_pattern_insight`, `Zone_paired_dry_bed_and_closed_valve_suggests_a_reviewed_command`, `Out_of_band_vwc_produces_threshold_insight`, `Falling_vwc_trend_is_detected`, `Evaluate_does_not_mutate_runtime_stores`. `Part1ApiContractTests.Recommendations_endpoint_returns_an_array` only asserts HTTP 200 and a JSON array.

### 9. UI walkthrough

1. Open [http://localhost:5197](http://localhost:5197) with the API already on 5057.
2. Home: **Sensor Data Ingestion and Telemetry** → `/ingestion`. **Real-Time Command Stream and History** → `/command-stream`. **Network Topology and Mesh Routing** is labelled planned and is not a link.
3. Ingestion: choose a seeded sensor, send moisture 0–100, power 0–20000, or valve bool. Part 1 links: Ingest / Sensors (`/register`) / Meters (`/tools`).
4. Command Stream: stats tiles (FIFO pending/capacity, processed, faults, device count). **Suggested Actions** from `/api/runtime/recommendations`. Search name or MAC (`oninput`). Category / status (`seen`, `unseen`, `alerting`) / actuator (`on`, `off`, `none`). Clear resets those fields. Filters are client-side (`DeviceDashboardFilter`); they do not call a filter API.
5. Command panel: pick the irrigation solenoid, ON/OFF, send (`note` `operator-ui`). Undo last. Confirm audit and journal in the same page (poll ~2 s).
6. History dropdown uses `GET /api/runtime/history`. Faults list `CODE:MAC` with Resolve (`POST /api/runtime/faults/resolve`).
7. Stale handling: stop the API while Command Stream is open. After a failed silent refresh, expect banner `Showing last successful snapshot. Cannot reach the command-stream API. Start SmartX.Api on port 5057.` First load with no snapshot shows the same message as `loadError`. Restart the API; the next successful poll clears the banner.

There is **no** automated browser suite.

### 10. Assessment troubleshooting

- Wrong order: client on 5197 with API down → ingest/command-stream errors.
- Stale API process after rebuild: stop the old `dotnet run` and start again.
- Empty or missing recommendations: in-band reading, evidence older than 6 hours, or ingest timestamp older than `lastSeenUtc`.
- Journal missing seed-era FIFO: cap 256 vs 800 seed packets.
- Topology is not implemented. Do not mark it as a working module.

---

## API reference

Part 1:

| Method | Route |
| ------ | ----- |
| GET | `/` |
| GET | `/api/sensors`, `/api/sensors/{mac}` |
| POST | `/api/sensors/register` |
| POST | `/api/sensors/{mac}/upload` |
| POST | `/api/telemetry/ingest-float`, `/ingest-int`, `/ingest-bool`, `/ingest` |
| GET | `/api/telemetry/recent` |
| POST | `/api/telemetry/batch` |
| GET | `/api/telemetry/history` (jagged-batch flatten — **not** runtime history) |
| POST | `/api/metrics/aggregate` |
| GET | `/api/deployment/tree` |
| POST | `/api/deployment/validate` |
| GET | `/api/gateway/integrity` |

Part 2:

| Method | Route |
| ------ | ----- |
| GET | `/api/runtime/stats` |
| GET | `/api/runtime/devices`, `/api/runtime/devices/{mac}` |
| GET | `/api/runtime/history?take=&mac=` |
| GET | `/api/runtime/faults` |
| POST | `/api/runtime/faults/resolve` |
| GET | `/api/runtime/journal` |
| GET | `/api/runtime/recommendations` |
| GET | `/api/commands` |
| POST | `/api/commands` |
| POST | `/api/commands/undo` |

Typical ingest 200 / 400 / 429. Commands 200 accepted or 400 with `CommandResult`. Unknown device 404.

---

## Six required data structures (runtime path)

| Structure | Class | Behaviour |
| --------- | ----- | --------- |
| `Queue<T>` | `PendingTelemetryQueue` | Bounded FIFO (capacity 2048). HTTP enqueues; worker dequeues. Not `ConcurrentQueue<T>`. |
| `PriorityQueue<TElement,TPriority>` | `AlertPriorityInbox` | Critical (0) before warning (1). Equal priority keeps enqueue sequence. Does not preempt an apply already in progress. |
| `Stack<T>` | `CommandUndoStore` | Undoable commands. Audit list is separate so undo does not erase visible history. Lease so two undos cannot consume one record. |
| `Dictionary<TKey,TValue>` | `LiveDeviceStore` | Live state keyed by `SensorRegistry.NormalizeMac`. Snapshots are DTOs. |
| `SortedDictionary<TKey,TValue>` | `TelemetryHistoryStore` | UTC timestamp + sequence (`HistoryKey`). Cap 2000. Older packets still recorded; live fields only move forward. |
| `HashSet<T>` | `ActiveFaultSet` | Unique `CODE:MAC`. Repeat observations are not new faults. |

`IngestionBuffer<T>` remains the Part 1 custom collection. `SensorRegistry` uses `ConcurrentDictionary` for profiles; it is not a substitute for `LiveDeviceStore`.

Counters on `GET /api/runtime/stats`: `pendingCount`, `dequeuedCount` (attempts), `processedCount` (successful apply), `rejectedCount` (never accepted), `processingErrors`, `deadLetteredCount`. Failed items retry once at the FIFO tail, then a bounded in-memory dead-letter list (64).

---

## Recommendation engine

`RecommendationService` only reads live state, history, faults, and command audit. It does not mutate those stores or call `CommandService`.

| Rule | Evidence | Minimum | Not emitted when |
| ---- | -------- | ------- | ---------------- |
| Threshold | Live numeric + `ThresholdEvaluator` (VWC 28–82% if unit contains `VWC`; power 2800 / 3500 W for power devices) | 1 fresh reading | Active `CODE:MAC` already raised; last-seen older than 6 hours |
| Trend | Last 4 numeric history points in time order | 4 readings; VWC span ≥ 5 or rising power | Bool/non-numeric; moisture without `VWC` in unit; last point > 6 hours old |
| Coupling | Dry VWC and an actuator sharing `Facility -> Zone` | Both | No shared zone (emits **diagnostic** with no command target) |
| Pattern | ON commands while a zone-paired VWC was below 28% at that timestamp | 3 matches | No paired moisture sensor |

Ids are stable (for example `threshold:VWC-LOW:{mac}`, `coupling-irr:{bed}:{valve}`). Re-evaluating unchanged evidence does not add extra rows.

Suggested Actions never send `POST /api/commands`. If a coupling insight names a valve, the operator must apply it in the command panel.

Limitations: pairing is deployment path, not similar names. Command records do not store the sensor value at click time; pattern analysis joins history by timestamp. EC (`mS/cm`) is not treated as VWC.

---

## Tests actually present

xUnit project `SmartX.Tests`. Areas covered in source:

- FIFO order, capacity rejection, concurrent enqueue (`PendingQueueTests`)
- Priority and equal-priority alert order (`PriorityAlertTests`)
- MAC-normalised dictionary, history order/bounds, HashSet raise/resolve (`DeviceHistoryFaultTests`)
- Undo previous state, rejected commands, stale undo, Part 1 buffer, queue-full throw, worker stop (`CommandAndIngestionTests`)
- Timestamp guard, command vs older telemetry, counters/dead-letter, concurrent undo, shutdown drain (`CorrectnessFixesTests`)
- Client-side device filters (`DeviceDashboardFilterTests`)
- Recommendation rules listed above (`RecommendationEngineTests`)
- HTTP ingest 200, stats, recommendations array (`Part1ApiContractTests`)

There is no automated browser/E2E suite in the repository. Copy-and-paste `--filter` commands for each structure are in **Part 2 practical assessment guide**. Re-run `dotnet test SmartX.Tests/SmartX.Tests.csproj` during assessment rather than relying on a previous run.

---

## Limitations and risks

- In-memory only. Process exit loses queues, history, undo, faults, and recommendations. Graceful stop drains up to 2 seconds or 2048 items; leftovers are discarded.
- Mesh seed enqueues 800 packets at startup; the worker processes them asynchronously.
- Live state ignores telemetry older than last-seen (history still stores it).
- Commanded vs reported actuator state can differ.
- No authentication. A development attachment-encryption key is stored in `SmartX.Api/appsettings.json`; do not treat it as a production secret.
- Client polling is not a live push channel.
- Topology is a locked home-page placeholder only.

---

## Docker

`docker-compose.yml`, `SmartX.Api/Dockerfile`, `SmartX.Client/Dockerfile`, `SmartX.Client/nginx.conf`, and `.dockerignore` are in the tree. Compose maps API **5057→8080** and client **5197→80**. The WASM `ApiBaseUrl` remains `http://localhost:5057`, which is intended for the **browser** calling the published API port on the host.

This README update did not re-run `docker compose up`. If containers fail to build, use the local `dotnet run` instructions above.

```bash
docker compose up --build
```

API-only image:

```bash
docker build -f SmartX.Api/Dockerfile -t smartx-api .
docker run --rm -p 5057:8080 smartx-api
```

---

## Troubleshooting

- **Cannot reach the API** — start `SmartX.Api` first; CORS must include the client origin.
- **WASM page blank** — hard refresh after `dotnet build SmartX.Client/SmartX.Client.csproj`.
- **Upload fails** — register the sensor first; max 10 MB.
- **No recommendations** — reading in-band, evidence older than 6 hours, or live state not updated because the packet timestamp was older than last-seen.

---

## Submission file-copy checklist

Copy the solution as source. Do **not** copy `bin/`, `obj/`, `.vs/`, `Uploads/`, or `.DS_Store`.

**Required source**

- `SmartX.slnx`
- `SmartX.Api/` (`.cs`, `.csproj`, `appsettings*.json`, `Properties/launchSettings.json`, `SmartX.Api.http`, `Dockerfile` if required by the brief)
- `SmartX.Client/` (`.razor`, `.cs`, `.csproj`, `wwwroot/`, `Properties/`, `nginx.conf` if using Docker)
- `SmartX.Shared/`
- `SmartX.Tests/`

**Recommended documentation / config**

- `README.md` (this file)
- `docker-compose.yml`, `.dockerignore` (optional; Docker is extra)
- `.gitignore`

After copy, from that folder: `dotnet restore SmartX.slnx`, `dotnet build SmartX.slnx`, `dotnet test SmartX.Tests/SmartX.Tests.csproj`.
