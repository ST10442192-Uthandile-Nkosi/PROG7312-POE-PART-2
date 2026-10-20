namespace SmartX.Api.Services;

/// <summary>
/// Unique active faults. Key is CODE:MAC so repeats of the same condition are not new faults.
/// </summary>
public sealed class ActiveFaultSet
{
    private readonly HashSet<string> _faults = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _gate = new();

    public static string Key(string code, string mac) =>
        $"{code}:{SensorRegistry.NormalizeMac(mac)}";

    public int Count
    {
        get { lock (_gate) return _faults.Count; }
    }

    public bool Raise(string code, string mac)
    {
        lock (_gate)
        {
            return _faults.Add(Key(code, mac));
        }
    }

    public bool Resolve(string code, string mac)
    {
        lock (_gate)
        {
            return _faults.Remove(Key(code, mac));
        }
    }

    public bool Contains(string code, string mac)
    {
        lock (_gate)
        {
            return _faults.Contains(Key(code, mac));
        }
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_gate)
        {
            return _faults.OrderBy(x => x, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }
}
