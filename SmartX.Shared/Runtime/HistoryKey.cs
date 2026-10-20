namespace SmartX.Shared.Runtime;

/// <summary>
/// SortedDictionary key: UTC timestamp, then a monotonic sequence so identical timestamps stay ordered.
/// </summary>
public readonly record struct HistoryKey(DateTimeOffset TimestampUtc, long Sequence) : IComparable<HistoryKey>
{
    public int CompareTo(HistoryKey other)
    {
        var time = TimestampUtc.CompareTo(other.TimestampUtc);
        return time != 0 ? time : Sequence.CompareTo(other.Sequence);
    }
}
