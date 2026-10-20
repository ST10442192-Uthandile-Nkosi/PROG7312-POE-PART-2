using System.Diagnostics;

namespace SmartX.Api.Services;

public sealed class TelemetryProcessingWorker : BackgroundService
{
    public static readonly TimeSpan ShutdownDrainBudget = TimeSpan.FromSeconds(2);

    private readonly PendingTelemetryQueue _pending;
    private readonly AlertPriorityInbox _alerts;
    private readonly TelemetryProcessingEngine _engine;
    private readonly ILogger<TelemetryProcessingWorker> _logger;

    public TelemetryProcessingWorker(
        PendingTelemetryQueue pending,
        AlertPriorityInbox alerts,
        TelemetryProcessingEngine engine,
        ILogger<TelemetryProcessingWorker> logger)
    {
        _pending = pending;
        _alerts = alerts;
        _engine = engine;
        _logger = logger;
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        _pending.Pulse();
        await base.StopAsync(cancellationToken);
        DrainOnShutdown();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Telemetry processing worker started.");
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var hadWork = false;
                try
                {
                    hadWork = _engine.ProcessOnce();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Telemetry processing step failed.");
                }

                if (!hadWork)
                {
                    try
                    {
                        _pending.WaitForWork(50, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }

                    if (!_alerts.HasPending && _pending.PendingCount == 0)
                    {
                        await Task.Delay(25, stoppingToken);
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            // shutdown
        }

        DrainOnShutdown();
        _logger.LogInformation("Telemetry processing worker stopping.");
    }

    private void DrainOnShutdown()
    {
        var clock = Stopwatch.StartNew();
        var drained = 0;
        while (clock.Elapsed < ShutdownDrainBudget &&
               drained < TelemetryProcessingEngine.ShutdownDrainLimit &&
               (_pending.PendingCount > 0 || _alerts.HasPending))
        {
            if (!_engine.ProcessOnce())
            {
                break;
            }

            drained++;
        }

        var leftover = _pending.PendingCount + _alerts.PendingCount;
        if (leftover > 0)
        {
            _logger.LogWarning(
                "Graceful shutdown drain processed {Drained} items in {Elapsed}. {Leftover} in-memory items remain and will be lost. Delivery is not durable.",
                drained,
                clock.Elapsed,
                leftover);
        }
        else
        {
            _logger.LogInformation(
                "Graceful shutdown drain processed {Drained} items. Queue empty.",
                drained);
        }
    }
}
