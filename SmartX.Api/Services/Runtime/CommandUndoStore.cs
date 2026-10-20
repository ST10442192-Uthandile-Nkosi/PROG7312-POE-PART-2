using SmartX.Shared.Commands;

namespace SmartX.Api.Services;

/// <summary>
/// Stack&lt;T&gt; holds undoable commands. A separate audit list is the operator-visible history.
/// At most one undo lease is outstanding so overlapping undos cannot consume the same record.
/// </summary>
public sealed class CommandUndoStore
{
    private readonly Stack<CommandRecord> _undo = new();
    private readonly List<CommandRecord> _audit = [];
    private readonly object _gate = new();
    private CommandRecord? _leased;

    public void RecordApplied(CommandRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        lock (_gate)
        {
            _audit.Add(record);
            if (!record.IsUndo)
            {
                _undo.Push(record);
            }
        }
    }

    public bool TryLeaseUndo(out CommandRecord? record)
    {
        lock (_gate)
        {
            if (_leased is not null || _undo.Count == 0)
            {
                record = null;
                return false;
            }

            record = _undo.Pop();
            _leased = record;
            return true;
        }
    }

    public void RollbackLease(CommandRecord record)
    {
        lock (_gate)
        {
            if (_leased is null || _leased.Id != record.Id)
            {
                return;
            }

            _undo.Push(record);
            _leased = null;
        }
    }

    public void CompleteLease(CommandRecord original, CommandRecord undoAudit)
    {
        lock (_gate)
        {
            if (_leased is null || _leased.Id != original.Id)
            {
                return;
            }

            _leased = null;
            _audit.Add(undoAudit);
        }
    }

    public bool TryPeekUndo(out CommandRecord? record)
    {
        lock (_gate)
        {
            if (_undo.Count == 0)
            {
                record = null;
                return false;
            }

            record = _undo.Peek();
            return true;
        }
    }

    public IReadOnlyList<CommandRecord> AuditSnapshot()
    {
        lock (_gate)
        {
            return _audit.ToList();
        }
    }

    public int UndoDepth
    {
        get
        {
            lock (_gate)
            {
                return _undo.Count + (_leased is null ? 0 : 1);
            }
        }
    }
}
