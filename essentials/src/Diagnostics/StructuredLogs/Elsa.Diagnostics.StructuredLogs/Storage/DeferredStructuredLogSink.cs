using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// Defers construction of the real sink until the first captured event. This lets the logging provider be
/// constructed while the persistent store's own logger is still being resolved, without creating the
/// provider -> sink -> store -> logger dependency cycle.
/// </summary>
/// <remarks>
/// The first event arrives while a shell activates, so construction can fail before the shell is ready. A failure is
/// not cached: the next event constructs again, where a cached failure would disable capture for the life of the
/// process. Events already waiting on the attempt that failed drop their entry rather than each retrying in turn,
/// and an event logged by the construction itself is dropped rather than re-entering it. The failure reaches the
/// capturing logger, which swallows it; it is never logged, because this sink would capture that log.
/// </remarks>
internal sealed class DeferredStructuredLogSink(Func<IStructuredLogSink> sinkFactory) : IStructuredLogSink
{
    private readonly object _constructionGate = new();
    private IStructuredLogSink? _sink;
    private int _constructingThreadId;
    private int _failedConstructions;

    public void Emit(StructuredLogEntry entry) => (Volatile.Read(ref _sink) ?? Construct())?.Emit(entry);

    private IStructuredLogSink? Construct()
    {
        // The gate is reentrant, so only this check keeps a construction that logs from constructing again.
        if (Volatile.Read(ref _constructingThreadId) == Environment.CurrentManagedThreadId)
            return null;

        var failuresBeforeWaiting = Volatile.Read(ref _failedConstructions);
        lock (_constructionGate)
        {
            if (_sink is not null)
                return _sink;
            if (_failedConstructions != failuresBeforeWaiting)
                return null;

            Volatile.Write(ref _constructingThreadId, Environment.CurrentManagedThreadId);
            try
            {
                var sink = sinkFactory();
                Volatile.Write(ref _sink, sink);
                return sink;
            }
            catch
            {
                Interlocked.Increment(ref _failedConstructions);
                throw;
            }
            finally
            {
                Volatile.Write(ref _constructingThreadId, 0);
            }
        }
    }
}
