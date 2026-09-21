namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The burst-scoped reconstructible cache for one drain of one workflow execution (ADR 0031 item b, spec 111). While a
/// burst (a sticky single-writer drain) owns an execution, activities and drain-path handlers may publish and read
/// heavy in-memory objects — a loaded model, an open client, a parsed document, or (the first consumer) the pinned
/// executable artifact — through this cache instead of rebuilding or re-reading them on every hop.
/// </summary>
/// <remarks>
/// <para>
/// <b>The load-bearing rule: memory is cache, never a correctness dependency.</b> Every entry MUST be reconstructible
/// from durable state. A lost cache — the burst crashes, is evicted, or never forms at all — reproduces byte-identical
/// results by replaying the same durable path. Nothing that must survive a suspend, crash, eviction, or scale-out hop
/// belongs here: that is a <see cref="DurableValueState"/> (inline) or a
/// <see cref="DurableValueExternalReference"/> (external), written to durable storage. The cache is a lifetime-bounded
/// accelerator over durable state, never the home of record for any value.
/// </para>
/// <para>
/// <b>Lifetime.</b> One scope per drain, established by <c>WorkflowSchedulerCommandRouter</c> around the drain and
/// disposed at quiescence. Entries are evicted (and disposed, if disposable) at scope end, so nothing leaks across
/// drains and the table never grows unbounded. Two sequential drains of the same execution never share entries.
/// </para>
/// <para>
/// <b>Concurrency.</b> Single-writer-per-execution is the ratified concurrency envelope (ADR 0031): one burst drains
/// one execution with no concurrent writer, so get-or-add needs no locking and single-flight is trivial. This type is
/// NOT thread-safe and must not be shared across executions.
/// </para>
/// </remarks>
public sealed class WorkflowBurstScope(string workflowExecutionId) : IAsyncDisposable
{
    private readonly Dictionary<string, object?> _entries = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>The workflow execution this burst owns.</summary>
    public string WorkflowExecutionId { get; } = !string.IsNullOrWhiteSpace(workflowExecutionId)
        ? workflowExecutionId
        : throw new ArgumentException("Workflow execution id is required.", nameof(workflowExecutionId));

    /// <summary>Total get-or-add calls served by this scope (diagnostics; hits + misses).</summary>
    public int ReadCount { get; private set; }

    /// <summary>Cache misses that fell through to the factory (diagnostics; the durable reads the cache did NOT save).</summary>
    public int MissCount { get; private set; }

    /// <summary>
    /// Returns the cached value for <paramref name="key"/>, or invokes <paramref name="factory"/> once and caches its
    /// result. The factory MUST produce a value that is fully reconstructible from durable state — see the type remarks;
    /// caching a value that must survive the run is a correctness bug (guarded in DEBUG builds).
    /// </summary>
    public async ValueTask<T> GetOrAddAsync<T>(string key, Func<CancellationToken, ValueTask<T>> factory, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(factory);
        ObjectDisposedException.ThrowIf(_disposed, this);

        ReadCount++;
        if (_entries.TryGetValue(key, out var existing))
            return (T)existing!;

        MissCount++;
        var value = await factory(cancellationToken);
        AssertReconstructible(value);
        // Single-writer: no concurrent GetOrAdd for the same key can have raced in during the await, so a plain assign
        // is correct. Re-check TryGetValue would be dead code under the ratified single-writer envelope.
        _entries[key] = value;
        return value;
    }

    /// <summary>
    /// Test/reconstructibility seam: evict (and dispose) every entry mid-drain WITHOUT disposing the scope, so the next
    /// read misses and reconstructs from durable state. Proves the cache is never a correctness dependency.
    /// </summary>
    public void Clear()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        DisposeEntries();
    }

    /// <summary>Deterministic disposal at burst end: dispose disposable entries and clear the table.</summary>
    public ValueTask DisposeAsync()
    {
        if (_disposed)
            return ValueTask.CompletedTask;
        _disposed = true;
        DisposeEntries();
        return ValueTask.CompletedTask;
    }

    private void DisposeEntries()
    {
        foreach (var entry in _entries.Values)
        {
            switch (entry)
            {
                // IAsyncDisposable entries are disposed synchronously here to keep Clear()/DisposeAsync cheap; a heavy
                // async-disposable consumer that needs true async teardown can expose its own drain hook. No shipped
                // consumer (the executable reader caches an immutable, non-disposable artifact) needs this today.
                case IDisposable disposable:
                    disposable.Dispose();
                    break;
            }
        }

        _entries.Clear();
    }

    private static void AssertReconstructible(object? value)
    {
#if DEBUG
        // The must-survive-⇒-durable-value rule (ADR 0031). Durable value carriers are the home of record for values
        // that outlive a burst; caching one here would let a caller mistake the cache for durable storage. Cheap
        // reference check, DEBUG-only, zero release cost.
        if (value is DurableValueState or DurableValueExternalReference)
            throw new InvalidOperationException(
                "A value that must survive the run (DurableValueState/DurableValueExternalReference) was published to the burst-scoped cache. " +
                "The cache is reconstructible-only; write must-survive values to durable storage instead (ADR 0031).");
#endif
    }
}
