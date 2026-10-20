namespace SmartX.Shared.Runtime;

/// <summary>
/// Lower number is processed first (min-heap). Equal priority uses an enqueue sequence (FIFO).
/// The worker never preempts an item already being processed.
/// </summary>
public static class AlertPriority
{
    public const int Critical = 0;
    public const int Warning = 1;
}
