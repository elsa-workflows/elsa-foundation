using Elsa.Diagnostics.StructuredLogs.Core.Contracts;
using Elsa.Diagnostics.StructuredLogs.Core.Models;

namespace Elsa.Diagnostics.StructuredLogs.Storage;

/// <summary>
/// Defers construction of the real sink until the first captured event. This lets the logging provider be
/// constructed while the persistent store's own logger is still being resolved, without creating the
/// provider -> sink -> store -> logger dependency cycle.
/// </summary>
internal sealed class DeferredStructuredLogSink(Func<IStructuredLogSink> sinkFactory) : IStructuredLogSink
{
    private readonly Lazy<IStructuredLogSink> _sink = new(sinkFactory, LazyThreadSafetyMode.ExecutionAndPublication);

    public void Emit(StructuredLogEntry entry) => _sink.Value.Emit(entry);
}
