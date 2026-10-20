# Smart-X IoT Mesh Gateway

Smart-X is an ingestion gateway for simulated ESP32 nodes. It accepts mixed telemetry (soil moisture as `float`, active power as `int`, valve state as `bool`), validates nested campus locations, and stores hardware logs with AES-256 encryption.

Projects:


| Folder          | Role                                                                     |
| --------------- | ------------------------------------------------------------------------ |
| `SmartX.Api`    | ASP.NET Core Minimal API (telemetry receiver)                            |
| `SmartX.Client` | Blazor WebAssembly dashboard                                             |
| `SmartX.Shared` | Shared types (`TelemetryPacket<T>`, `PowerMetric`, `IngestionBuffer<T>`) |


Default URLs:

- API: [http://localhost:5057](http://localhost:5057)
- Dashboard: [http://localhost:5197](http://localhost:5197)

---



## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- Optional: [Docker Desktop](https://www.docker.com/products/docker-desktop/)

---



## Restore dependencies

From the repository root:

```bash
dotnet restore SmartX.slnx
```



## Compile

```bash
dotnet build SmartX.slnx
```



## Run the backend API

Terminal 1:

```bash
dotnet run --project SmartX.Api/SmartX.Api.csproj --launch-profile http
```

Wait until the console shows `Now listening on: http://localhost:5057`.

OpenAPI document: [http://localhost:5057/openapi/v1.json](http://localhost:5057/openapi/v1.json)

## Run the client dashboard

Terminal 2:

```bash
dotnet run --project SmartX.Client/SmartX.Client.csproj --launch-profile http
```

Open [http://localhost:5197](http://localhost:5197) in a browser.

If the API is not on port 5057, edit `SmartX.Client/wwwroot/appsettings.json` (`ApiBaseUrl`) and restart the client.

---



## Docker (API + client)

```bash
docker compose up --build
```

- API container: [http://localhost:5057](http://localhost:5057)
- Client container (nginx): [http://localhost:5197](http://localhost:5197)

The browser still calls the API at `http://localhost:5057` (see `appsettings.json`).

API image only:

```bash
docker build -f SmartX.Api/Dockerfile -t smartx-api .
docker run --rm -p 5057:8080 smartx-api
```

---



## How to use the dashboard

1. Landing page — three pillars. Only **Sensor data ingestion and telemetry** is enabled. The other two are disabled.
2. **Ingest** — pick a seeded ESP32, send moisture / power / valve packets. The mesh map updates immediately.
3. **Sensors** — register MAC, location (`Facility A -> Zone 1 -> Sub-Zone B`), category, and attach a log or photo (stored AES-256 encrypted).
4. **Meters** — `Meter1 + Meter2`, recursive location check, jagged batch history flattened into `IngestionBuffer<T>` then `List<T>`.

Invalid moisture (outside 0–100) is rejected in the browser and by the API.

---



## Troubleshooting

- **Cannot reach the API** — start `SmartX.Api` first; confirm CORS origins include [http://localhost:5197](http://localhost:5197).
- **WASM page blank** — hard refresh after a rebuild (`dotnet build SmartX.Client`).
- **File upload fails** — register the sensor first; max size 10 MB.

