using System.Collections.Concurrent;
using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Bounded process-local cache for immutable executable artifacts stored by a durable provider.
/// </summary>
public sealed class CachingWorkflowExecutableStore : IWorkflowExecutableStore
{
    private readonly IWorkflowExecutableStore _inner;
    private readonly int _capacity;
    private readonly ConcurrentDictionary<string, LoadOperation> _inFlight = new(StringComparer.Ordinal);
    private readonly Dictionary<string, LinkedListNode<CacheEntry>> _entries = new(StringComparer.Ordinal);
    private readonly LinkedList<CacheEntry> _recency = new();
    private readonly Lock _gate = new();

    public CachingWorkflowExecutableStore(IWorkflowExecutableStore inner, WorkflowExecutableCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(inner);
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.Enabled)
            throw new ArgumentException("The cache decorator cannot be constructed when workflow executable caching is disabled.", nameof(options));

        _inner = inner;
        _capacity = options.Capacity;
    }

    public async ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(executable);
        ArgumentException.ThrowIfNullOrWhiteSpace(executable.Identity.ArtifactId);

        await _inner.SaveAsync(executable, cancellationToken);
        InvalidateAndEvict(executable.Identity.ArtifactId, WorkflowExecutableCacheTelemetry.SaveReason);
    }

    public async ValueTask<bool> DeleteAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        var deleted = await _inner.DeleteAsync(artifactId, cancellationToken);
        InvalidateAndEvict(artifactId, WorkflowExecutableCacheTelemetry.DeleteReason);
        return deleted;
    }

    public async ValueTask<WorkflowExecutable?> FindAsync(string artifactId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactId);

        if (TryGetCached(artifactId, out var cached))
        {
            RecordRequest(WorkflowExecutableCacheTelemetry.HitResult);
            return cached;
        }

        RecordRequest(WorkflowExecutableCacheTelemetry.MissResult);
        var operation = new LoadOperation();
        var sharedOperation = _inFlight.GetOrAdd(artifactId, operation);

        if (ReferenceEquals(sharedOperation, operation))
        {
            if (TryGetCached(artifactId, out cached))
            {
                _inFlight.TryRemove(new KeyValuePair<string, LoadOperation>(artifactId, operation));
                operation.Completion.TrySetResult(cached);
            }
            else
            {
                _ = LoadProviderAsync(artifactId, operation);
            }
        }

        return await sharedOperation.Completion.Task.WaitAsync(cancellationToken);
    }

    public ValueTask<IReadOnlyCollection<WorkflowExecutable>> ListAsync(CancellationToken cancellationToken = default) =>
        _inner.ListAsync(cancellationToken);

    private async Task LoadProviderAsync(string artifactId, LoadOperation operation)
    {
        try
        {
            var mutationVersion = operation.Version;
            var executable = await LoadProviderOnceAsync(artifactId);

            // A lookup that started before a mutation may complete for its original caller, but it must not
            // repopulate cache state after the provider-authoritative save/delete linearization point.
            if (executable is not null)
                AdmitIfCurrent(artifactId, executable, operation, mutationVersion);

            RemoveInFlight(artifactId, operation);
            operation.Completion.TrySetResult(executable);
        }
        catch (OperationCanceledException exception)
        {
            RemoveInFlight(artifactId, operation);
            operation.Completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            RemoveInFlight(artifactId, operation);
            operation.Completion.TrySetException(exception);
        }
    }

    private void RemoveInFlight(string artifactId, LoadOperation operation) =>
        _inFlight.TryRemove(new KeyValuePair<string, LoadOperation>(artifactId, operation));

    private async Task<WorkflowExecutable?> LoadProviderOnceAsync(string artifactId)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = WorkflowExecutableCacheTelemetry.NotFoundOutcome;

        try
        {
            var executable = await _inner.FindAsync(artifactId, CancellationToken.None);
            outcome = executable is null
                ? WorkflowExecutableCacheTelemetry.NotFoundOutcome
                : WorkflowExecutableCacheTelemetry.FoundOutcome;
            return executable;
        }
        catch (OperationCanceledException)
        {
            outcome = WorkflowExecutableCacheTelemetry.CancelledOutcome;
            throw;
        }
        catch
        {
            outcome = WorkflowExecutableCacheTelemetry.FailedOutcome;
            throw;
        }
        finally
        {
            try
            {
                var tags = new TagList { { WorkflowExecutableCacheTelemetry.OutcomeTag, outcome } };
                WorkflowExecutableCacheTelemetry.ProviderLoadDuration.Record(
                    Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                    tags);
            }
            catch (Exception)
            {
                // Diagnostics are observational and must never change executable-store behavior.
            }
        }
    }

    private bool TryGetCached(string artifactId, out WorkflowExecutable? executable)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(artifactId, out var node))
            {
                executable = null;
                return false;
            }

            _recency.Remove(node);
            _recency.AddFirst(node);
            executable = node.Value.Executable;
            return true;
        }
    }

    private void AdmitIfCurrent(
        string artifactId,
        WorkflowExecutable executable,
        LoadOperation operation,
        long mutationVersion)
    {
        var evictedForCapacity = false;

        lock (_gate)
        {
            if (mutationVersion != operation.Version
                || !_inFlight.TryGetValue(artifactId, out var currentOperation)
                || !ReferenceEquals(operation, currentOperation))
                return;

            if (_entries.TryGetValue(artifactId, out var existing))
            {
                _recency.Remove(existing);
                existing.Value = new CacheEntry(artifactId, executable);
                _recency.AddFirst(existing);
                return;
            }

            var node = _recency.AddFirst(new CacheEntry(artifactId, executable));
            _entries.Add(artifactId, node);

            if (_entries.Count > _capacity)
            {
                var leastRecentlyUsed = _recency.Last!;
                _recency.RemoveLast();
                _entries.Remove(leastRecentlyUsed.Value.ArtifactId);
                evictedForCapacity = true;
            }
        }

        if (evictedForCapacity)
            RecordEviction(WorkflowExecutableCacheTelemetry.CapacityReason);
    }

    private void InvalidateAndEvict(string artifactId, string reason)
    {
        var removed = false;

        lock (_gate)
        {
            if (_inFlight.TryRemove(artifactId, out var operation))
                operation.Invalidate();

            if (_entries.Remove(artifactId, out var node))
            {
                _recency.Remove(node);
                removed = true;
            }
        }

        if (removed)
            RecordEviction(reason);
    }

    private static void RecordRequest(string result)
    {
        try
        {
            var tags = new TagList { { WorkflowExecutableCacheTelemetry.ResultTag, result } };
            WorkflowExecutableCacheTelemetry.Requests.Add(1, tags);
        }
        catch (Exception)
        {
            // Diagnostics are observational and must never change executable-store behavior.
        }
    }

    private static void RecordEviction(string reason)
    {
        try
        {
            var tags = new TagList { { WorkflowExecutableCacheTelemetry.ReasonTag, reason } };
            WorkflowExecutableCacheTelemetry.Evictions.Add(1, tags);
        }
        catch (Exception)
        {
            // Diagnostics are observational and must never change executable-store behavior.
        }
    }

    private sealed class LoadOperation
    {
        private long _version;

        public TaskCompletionSource<WorkflowExecutable?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Version => Volatile.Read(ref _version);

        public void Invalidate() => Interlocked.Increment(ref _version);
    }

    private sealed record CacheEntry(string ArtifactId, WorkflowExecutable Executable);
}
