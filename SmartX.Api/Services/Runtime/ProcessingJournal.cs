using SmartX.Shared.Runtime;

namespace SmartX.Api.Services;

/// <summary>
/// Diagnostic log so tests can prove an alert was handled before pending FIFO items.
/// </summary>
public sealed class ProcessingJournal
{
    private readonly List<ProcessingJournalEntry> _entries = [];
    private readonly object _gate = new();
    private readonly int _capacity;

    public ProcessingJournal(int capacity = 256)
    {
        _capacity = capacity;
    }

    public void Record(string kind, string detail)
    {
        lock (_gate)
        {
            _entries.Add(new ProcessingJournalEntry
            {
                AtUtc = DateTimeOffset.UtcNow,
                Kind = kind,
                Detail = detail
            });
            if (_entries.Count > _capacity)
            {
                _entries.RemoveAt(0);
            }
        }
    }

    public IReadOnlyList<ProcessingJournalEntry> Snapshot()
    {
        lock (_gate)
        {
            return _entries.ToList();
        }
    }
}
