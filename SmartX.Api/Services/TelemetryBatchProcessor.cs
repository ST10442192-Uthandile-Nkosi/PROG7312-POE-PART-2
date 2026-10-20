using SmartX.Shared.Collections;
using SmartX.Shared.Telemetry;

namespace SmartX.Api.Services;

/// <summary>
/// Jagged ESP32 bursts plus a 3×24 hourly grid, then flatten into IngestionBuffer.
/// </summary>
public sealed class TelemetryBatchProcessor
{
    private readonly object _gate = new();
    private readonly double[][] _burstWindow = new double[16][];
    private readonly double[,] _hourlyFacilityLoad = new double[3, 24];
    private readonly int[,] _hourlyCounts = new int[3, 24];
    private int _burstIndex;

    public void AddBatch(double[] newBatch, int facilityIndex = 0)
    {
        ArgumentNullException.ThrowIfNull(newBatch);
        lock (_gate)
        {
            _burstWindow[_burstIndex] = newBatch;
            _burstIndex = (_burstIndex + 1) % _burstWindow.Length;

            var hour = DateTime.UtcNow.Hour;
            var facility = Math.Clamp(facilityIndex, 0, 2);
            foreach (var sample in newBatch)
            {
                _hourlyFacilityLoad[facility, hour] += sample;
                _hourlyCounts[facility, hour]++;
            }
        }
    }

    public HistoryResponse FlattenToOptimizedList()
    {
        lock (_gate)
        {
            var optimized = new IngestionBuffer<double>(2048);
            foreach (var burst in _burstWindow)
            {
                if (burst is { Length: > 0 })
                {
                    optimized.AddRange(burst);
                }
            }

            List<double> optimizedList = optimized.Snapshot(); // transfer into List<T>

            var means = new double[3][];
            for (var f = 0; f < 3; f++)
            {
                means[f] = new double[24];
                for (var h = 0; h < 24; h++)
                {
                    means[f][h] = _hourlyCounts[f, h] == 0
                        ? 0
                        : _hourlyFacilityLoad[f, h] / _hourlyCounts[f, h];
                }
            }

            return new HistoryResponse
            {
                Count = optimizedList.Count,
                Data = optimizedList,
                HourlyFacilityLoad = means,
                BurstWindowsStored = _burstWindow.Count(b => b is { Length: > 0 })
            };
        }
    }
}
