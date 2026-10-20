using Microsoft.Extensions.Logging.Abstractions;
using SmartX.Api.Services;
using SmartX.Shared.Runtime;
using SmartX.Shared.Sensors;
using SmartX.Shared.Telemetry;

namespace SmartX.Tests;

internal sealed class RuntimeHarness
{
    public SensorRegistry Registry { get; }
    public PendingTelemetryQueue Pending { get; }
    public AlertPriorityInbox Alerts { get; }
    public ThresholdEvaluator Thresholds { get; }
    public LiveDeviceStore Devices { get; }
    public TelemetryHistoryStore History { get; }
    public ActiveFaultSet Faults { get; }
    public CommandUndoStore Undo { get; }
    public ProcessingJournal Journal { get; }
    public TelemetryProcessingEngine Engine { get; }
    public TelemetryIngestionService Ingestion { get; }
    public CommandService Commands { get; }

    private RuntimeHarness(
        SensorRegistry registry,
        PendingTelemetryQueue pending,
        AlertPriorityInbox alerts,
        ThresholdEvaluator thresholds,
        LiveDeviceStore devices,
        TelemetryHistoryStore history,
        ActiveFaultSet faults,
        CommandUndoStore undo,
        ProcessingJournal journal,
        TelemetryProcessingEngine engine,
        TelemetryIngestionService ingestion,
        CommandService commands)
    {
        Registry = registry;
        Pending = pending;
        Alerts = alerts;
        Thresholds = thresholds;
        Devices = devices;
        History = history;
        Faults = faults;
        Undo = undo;
        Journal = journal;
        Engine = engine;
        Ingestion = ingestion;
        Commands = commands;
    }

    public static RuntimeHarness Create(int queueCapacity = 32, int historyCapacity = 200)
    {
        var registry = new SensorRegistry();
        var pending = new PendingTelemetryQueue(queueCapacity);
        var alerts = new AlertPriorityInbox();
        var thresholds = new ThresholdEvaluator();
        var devices = new LiveDeviceStore(registry);
        var history = new TelemetryHistoryStore(historyCapacity);
        var faults = new ActiveFaultSet();
        var undo = new CommandUndoStore();
        var journal = new ProcessingJournal();
        var engine = new TelemetryProcessingEngine(
            pending, alerts, devices, history, faults, journal, thresholds, registry);
        var ingestion = new TelemetryIngestionService(pending, alerts, thresholds, registry, faults);
        var commands = new CommandService(registry, devices, undo, ingestion, history, journal);
        return new RuntimeHarness(
            registry, pending, alerts, thresholds, devices, history, faults, undo, journal, engine, ingestion, commands);
    }

    public TelemetryProcessingWorker CreateWorker() =>
        new(Pending, Alerts, Engine, NullLogger<TelemetryProcessingWorker>.Instance);

    public SensorProfile Register(string mac, SensorCategory category, string name, string location) =>
        Registry.Register(new SensorRegistrationRequest
        {
            MacAddress = mac,
            DisplayName = name,
            Location = location,
            Category = category
        });

    public TelemetryWorkItem Item(
        string mac,
        double value,
        string unit = "%VWC",
        string kind = "float",
        DateTimeOffset? at = null,
        long sequence = 0) =>
        new()
        {
            Sequence = sequence,
            DeviceId = mac,
            TimestampUtc = at ?? DateTimeOffset.UnixEpoch.AddHours(1),
            ValueKind = kind,
            NumericValue = value,
            MetricName = kind == "int" ? "active_power" : "soil_moisture",
            Unit = unit,
            KnownCategory = Registry.Get(mac)?.Category
        };
}

internal static class TestMacs
{
    public const string Moisture = "24:6F:28:AA:00:01";
    public const string MoistureAlt = "24:6F:28:AA:00:11";
    public const string Valve = "24:6F:28:AA:00:02";
    public const string Clamp = "30:AE:A4:AA:00:03";
}

internal static class TestPackets
{
    public static TelemetryPacket<float> Float(string mac, float value, string unit = "%VWC") => new()
    {
        DeviceId = mac,
        Timestamp = DateTimeOffset.UnixEpoch.AddHours(2),
        Value = value,
        MetricName = "soil_moisture",
        Unit = unit
    };

    public static TelemetryPacket<int> Int(string mac, int watts) => new()
    {
        DeviceId = mac,
        Timestamp = DateTimeOffset.UnixEpoch.AddHours(2),
        Value = watts,
        MetricName = "active_power",
        Unit = "W"
    };

    public static TelemetryPacket<bool> Bool(string mac, bool on) => new()
    {
        DeviceId = mac,
        Timestamp = DateTimeOffset.UnixEpoch.AddHours(2),
        Value = on,
        MetricName = "actuator_state",
        Unit = "state"
    };
}
