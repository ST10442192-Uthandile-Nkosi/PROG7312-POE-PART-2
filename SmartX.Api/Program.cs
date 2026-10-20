using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using SmartX.Api.Services;
using SmartX.Shared.Commands;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

var builder = WebApplication.CreateBuilder(args);

// Allow the Blazor dashboard to call this API from localhost.
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowBlazorClient", policy =>
    {
        policy.WithOrigins(
                "https://localhost:7091",
                "http://localhost:5197",
                "http://localhost:8088")
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 10 * 1024 * 1024);
builder.Services.AddOpenApi();
builder.Services.AddSingleton<SensorRegistry>();
builder.Services.AddSingleton<PendingTelemetryQueue>();
builder.Services.AddSingleton<AlertPriorityInbox>();
builder.Services.AddSingleton<ThresholdEvaluator>();
builder.Services.AddSingleton<LiveDeviceStore>();
builder.Services.AddSingleton<TelemetryHistoryStore>();
builder.Services.AddSingleton<ActiveFaultSet>();
builder.Services.AddSingleton<CommandUndoStore>();
builder.Services.AddSingleton<SearchObservationStore>();
builder.Services.AddSingleton<ProcessingJournal>();
builder.Services.AddSingleton<TelemetryProcessingEngine>();
builder.Services.AddSingleton<TelemetryIngestionService>();
builder.Services.AddSingleton<CommandService>();
builder.Services.AddSingleton<RecommendationService>();
builder.Services.AddSingleton<TelemetryBatchProcessor>();
builder.Services.AddSingleton<DeploymentValidator>();
builder.Services.AddSingleton<AttachmentStore>();
builder.Services.AddSingleton<GatewayIntegrityService>();
builder.Services.AddHostedService<MeshSeedHostedService>();
builder.Services.AddHostedService<TelemetryProcessingWorker>();

var app = builder.Build();

app.UseCors("AllowBlazorClient");
app.MapOpenApi();

// --- Sensors ---
app.MapGet("/", () => Results.Ok(new { Service = "Smart-X Gateway", Status = "ready" }));

app.MapGet("/api/sensors", (SensorRegistry registry) => Results.Ok(registry.All()));

app.MapGet("/api/sensors/{mac}", (string mac, SensorRegistry registry) =>
{
    var sensor = registry.Get(mac);
    return sensor is null ? Results.NotFound() : Results.Ok(sensor);
});

app.MapPost("/api/sensors/register", (
    SensorRegistrationRequest request,
    SensorRegistry registry,
    DeploymentValidator validator) =>
{
    // Recursive check: Facility -> Zone -> Sub-Zone
    var validation = validator.ValidateNodePath(request.Location, request.Category.ToString());
    if (!validation.IsValid)
    {
        return Results.BadRequest(new { Message = "Deployment path failed recursive validation.", validation });
    }

    try
    {
        var profile = registry.Register(request);
        return Results.Ok(profile);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { ex.Message });
    }
});

// Multipart log/photo upload — encrypted on disk (AES-256).
app.MapPost("/api/sensors/{mac}/upload", async (
    string mac,
    IFormFile file,
    SensorRegistry registry,
    AttachmentStore store,
    CancellationToken cancellationToken) =>
{
    if (registry.Get(mac) is null)
    {
        return Results.NotFound(new { Message = "Register the sensor first." });
    }

    if (file.Length == 0)
    {
        return Results.BadRequest("No file uploaded.");
    }

    var attachment = await store.SaveAsync(mac, file, cancellationToken);
    registry.AddAttachment(mac, attachment);
    return Results.Ok(attachment);
}).DisableAntiforgery();

// Typed ingest: TelemetryPacket<float>, <int>, and <bool> (no boxing).
// 200 = accepted into IngestionBuffer and the FIFO processing queue. 429 = queue full.
app.MapPost("/api/telemetry/ingest-float", (TelemetryPacket<float> packet, TelemetryIngestionService telemetry) =>
    TryIngest(() => telemetry.IngestFloat(packet)));

app.MapPost("/api/telemetry/ingest-int", (TelemetryPacket<int> packet, TelemetryIngestionService telemetry) =>
    TryIngest(() => telemetry.IngestInt(packet)));

app.MapPost("/api/telemetry/ingest-bool", (TelemetryPacket<bool> packet, TelemetryIngestionService telemetry) =>
    TryIngest(() => telemetry.IngestBool(packet)));

app.MapPost("/api/telemetry/ingest", (TelemetryIngestRequest request, TelemetryIngestionService telemetry) =>
{
    if (string.IsNullOrWhiteSpace(request.DeviceId))
    {
        return Results.BadRequest(new { Message = "Device is required." });
    }

    var kind = request.ValueKind.Trim().ToLowerInvariant();
    return kind switch
    {
        "float" or "double" or "moisture" => IngestMoisture(request, telemetry),
        "int" or "power" or "watts" => IngestPower(request, telemetry),
        "bool" or "actuator" => TryIngest(() => telemetry.IngestBool(new TelemetryPacket<bool>
        {
            DeviceId = request.DeviceId,
            Timestamp = DateTimeOffset.UtcNow,
            Value = request.BooleanValue ?? ((request.NumericValue ?? 0) >= 1),
            MetricName = string.IsNullOrWhiteSpace(request.MetricName) ? "actuator_state" : request.MetricName,
            Unit = "state"
        })),
        _ => Results.BadRequest(new { Message = "Type must be float, int, or bool." })
    };
});

app.MapGet("/api/telemetry/recent", (TelemetryIngestionService telemetry, [FromQuery] int take = 40) =>
    Results.Ok(telemetry.Recent(take)));

app.MapPost("/api/telemetry/batch", ([FromBody] double[] newBatch, TelemetryBatchProcessor processor, [FromQuery] int facility = 0) =>
{
    processor.AddBatch(newBatch, facility);
    return Results.Ok(new { Samples = newBatch.Length });
});

app.MapGet("/api/telemetry/history", (TelemetryBatchProcessor processor) =>
    Results.Ok(processor.FlattenToOptimizedList())); // jagged bursts -> List<double>

// Meter3 = Meter1 + Meter2 (overloaded + and >).
app.MapPost("/api/metrics/aggregate", ([FromBody] MetricAggregateRequest request) =>
{
    var combined = request.Metric1 + request.Metric2;
    var delta = request.Metric1 - request.Metric2;
    var limit = new PowerMetric { Watts = request.TransformerLimitWatts, MeterId = "TX" };
    return Results.Ok(new MetricAggregateResponse
    {
        AggregatedWatts = combined.Watts,
        DeltaWatts = delta.Watts,
        ExceedsTransformer = combined > limit,
        CombinedMeterId = combined.MeterId
    });
});

app.MapGet("/api/deployment/tree", (DeploymentValidator validator) =>
    Results.Ok(new { validator.Root, NodeCount = validator.CountNodes() }));

app.MapPost("/api/deployment/validate", ([FromBody] SmartX.Shared.Deployment.ValidationRequest request, DeploymentValidator validator) =>
    Results.Ok(validator.ValidateNodePath(request.TargetPath, request.SensorCategory)));

app.MapGet("/api/gateway/integrity", (GatewayIntegrityService integrity) =>
    Results.Ok(integrity.Build()));

app.MapGet("/api/runtime/stats", (TelemetryProcessingEngine engine) =>
    Results.Ok(engine.Stats()));

app.MapGet("/api/runtime/devices", (LiveDeviceStore devices) =>
    Results.Ok(devices.Snapshot()));

app.MapGet("/api/runtime/devices/{mac}", (string mac, LiveDeviceStore devices) =>
    devices.TryGet(mac, out var state) ? Results.Ok(state) : Results.NotFound());

app.MapGet("/api/runtime/history", (TelemetryHistoryStore history, [FromQuery] int take = 40, [FromQuery] string? mac = null) =>
    Results.Ok(history.Latest(take, mac)));

app.MapGet("/api/runtime/faults", (ActiveFaultSet faults) =>
    Results.Ok(faults.Snapshot()));

app.MapPost("/api/runtime/faults/resolve", (FaultResolveRequest request, ActiveFaultSet faults, LiveDeviceStore devices) =>
{
    if (string.IsNullOrWhiteSpace(request.Code) || string.IsNullOrWhiteSpace(request.MacAddress))
    {
        return Results.BadRequest(new { Message = "Code and MAC are required." });
    }

    var removed = faults.Resolve(request.Code, request.MacAddress);
    if (removed)
    {
        var stillAlerting = faults.Snapshot().Any(f =>
            f.EndsWith(":" + SensorRegistry.NormalizeMac(request.MacAddress), StringComparison.OrdinalIgnoreCase));
        devices.SetAlertFlag(request.MacAddress, stillAlerting);
    }

    return Results.Ok(new { Resolved = removed, Active = faults.Snapshot() });
});

app.MapGet("/api/runtime/journal", (ProcessingJournal journal) =>
    Results.Ok(journal.Snapshot()));

app.MapGet("/api/runtime/recommendations", (RecommendationService recommendations) =>
    Results.Ok(recommendations.Evaluate()));

app.MapPost("/api/runtime/search-observations", (SearchObservationRequest request, SearchObservationStore searches) =>
{
    if (string.IsNullOrWhiteSpace(request.Term))
    {
        return Results.BadRequest(new { Message = "Term is required." });
    }

    searches.Record(request.Term);
    return Results.Ok(new { searches.Count });
});

app.MapGet("/api/commands", (CommandService commands) =>
    Results.Ok(commands.History()));

app.MapPost("/api/commands", (CommandRequest request, CommandService commands) =>
{
    var result = commands.Apply(request);
    return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
});

app.MapPost("/api/commands/undo", (CommandService commands) =>
{
    var result = commands.Undo();
    return result.Accepted ? Results.Ok(result) : Results.BadRequest(result);
});

app.Run();

static IResult IngestMoisture(TelemetryIngestRequest request, TelemetryIngestionService telemetry)
{
    var value = (float)(request.NumericValue ?? 0);
    if (value is < 0 or > 100)
    {
        return Results.BadRequest(new { Message = "Soil moisture must be between 0 and 100 %VWC." });
    }

    return TryIngest(() => telemetry.IngestFloat(new TelemetryPacket<float>
    {
        DeviceId = request.DeviceId,
        Timestamp = DateTimeOffset.UtcNow,
        Value = value,
        MetricName = string.IsNullOrWhiteSpace(request.MetricName) ? "soil_moisture" : request.MetricName,
        Unit = string.IsNullOrWhiteSpace(request.Unit) ? "%VWC" : request.Unit
    }));
}

static IResult IngestPower(TelemetryIngestRequest request, TelemetryIngestionService telemetry)
{
    var value = (int)(request.NumericValue ?? 0);
    if (value is < 0 or > 20000)
    {
        return Results.BadRequest(new { Message = "Power must be between 0 and 20000 W." });
    }

    return TryIngest(() => telemetry.IngestInt(new TelemetryPacket<int>
    {
        DeviceId = request.DeviceId,
        Timestamp = DateTimeOffset.UtcNow,
        Value = value,
        MetricName = string.IsNullOrWhiteSpace(request.MetricName) ? "active_power" : request.MetricName,
        Unit = string.IsNullOrWhiteSpace(request.Unit) ? "W" : request.Unit
    }));
}

static IResult TryIngest(Func<object> ingest)
{
    try
    {
        return Results.Ok(ingest());
    }
    catch (QueueFullException ex)
    {
        return Results.Json(new { ex.Message }, statusCode: StatusCodes.Status429TooManyRequests);
    }
}

public sealed record FaultResolveRequest(string Code, string MacAddress);

public partial class Program;
