using System.Collections.Concurrent;
using System.Diagnostics;
using Elsa.Workflows.Runtime.Core.Diagnostics;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Bounded cache state shared by scoped executable-store adapters in one application process.
/// </summary>
public sealed class WorkflowExecutableCache
{
    private readonly int _capacity;
    private readonly ConcurrentDictionary<CacheKey, LoadOperation> _inFlight = new();
    private readonly Dictionary<CacheKey, LinkedListNode<CacheEntry>> _entries = [];
    private readonly LinkedList<CacheEntry> _recency = [];
    private readonly Lock _gate = new();

    public WorkflowExecutableCache(WorkflowExecutableCacheOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        options.Validate();
        if (!options.Enabled)
        {
            throw new ArgumentException(
                "Workflow executable cache state cannot be constructed when caching is disabled.",
                nameof(options));
        }

        _capacity = options.Capacity;
    }

    internal async ValueTask<WorkflowExecutable?> FindAsync(
        string partition,
        string artifactId,
        Func<string, CancellationToken, ValueTask<WorkflowExecutable?>> load,
        CancellationToken cancellationToken)
    {
        var key = new CacheKey(partition, artifactId);
        if (TryGetCached(key, out var cached))
        {
            RecordRequest(WorkflowExecutableCacheTelemetry.HitResult);
            return cached;
        }

        RecordRequest(WorkflowExecutableCacheTelemetry.MissResult);
        var operation = new LoadOperation();
        var sharedOperation = _inFlight.GetOrAdd(key, operation);

        if (ReferenceEquals(sharedOperation, operation))
        {
            if (TryGetCached(key, out cached))
            {
                _inFlight.TryRemove(new KeyValuePair<CacheKey, LoadOperation>(key, operation));
                operation.Completion.TrySetResult(cached);
            }
            else
            {
                _ = LoadProviderAsync(key, operation, load);
            }
        }

        return await sharedOperation.Completion.Task.WaitAsync(cancellationToken);
    }

    internal void Invalidate(string partition, string artifactId, string reason)
    {
        var key = new CacheKey(partition, artifactId);
        var removed = false;

        lock (_gate)
        {
            if (_inFlight.TryRemove(key, out var operation))
                operation.Invalidate();

            if (_entries.Remove(key, out var node))
            {
                _recency.Remove(node);
                removed = true;
            }
        }

        if (removed)
            RecordEviction(reason);
    }

    internal void InvalidateAllPartitions(string artifactId, string reason)
    {
        List<CacheKey>? residentKeys = null;

        lock (_gate)
        {
            foreach (var pair in _inFlight)
            {
                if (!StringComparer.Ordinal.Equals(pair.Key.ArtifactId, artifactId))
                    continue;

                if (_inFlight.TryRemove(pair))
                    pair.Value.Invalidate();
            }

            foreach (var pair in _entries)
            {
                if (!StringComparer.Ordinal.Equals(pair.Key.ArtifactId, artifactId))
                    continue;

                residentKeys ??= [];
                residentKeys.Add(pair.Key);
            }

            if (residentKeys is not null)
            {
                foreach (var key in residentKeys)
                {
                    var node = _entries[key];
                    _entries.Remove(key);
                    _recency.Remove(node);
                }
            }
        }

        if (residentKeys is not null)
        {
            foreach (var _ in residentKeys)
                RecordEviction(reason);
        }
    }

    private async Task LoadProviderAsync(
        CacheKey key,
        LoadOperation operation,
        Func<string, CancellationToken, ValueTask<WorkflowExecutable?>> load)
    {
        try
        {
            var mutationVersion = operation.Version;
            var executable = await LoadProviderOnceAsync(key.ArtifactId, load);
            if (executable is not null)
                AdmitIfCurrent(key, executable, operation, mutationVersion);

            RemoveInFlight(key, operation);
            operation.Completion.TrySetResult(executable);
        }
        catch (OperationCanceledException exception)
        {
            RemoveInFlight(key, operation);
            operation.Completion.TrySetCanceled(exception.CancellationToken);
        }
        catch (Exception exception)
        {
            RemoveInFlight(key, operation);
            operation.Completion.TrySetException(exception);
        }
    }

    private void RemoveInFlight(CacheKey key, LoadOperation operation) =>
        _inFlight.TryRemove(new KeyValuePair<CacheKey, LoadOperation>(key, operation));

    private static async Task<WorkflowExecutable?> LoadProviderOnceAsync(
        string artifactId,
        Func<string, CancellationToken, ValueTask<WorkflowExecutable?>> load)
    {
        var started = Stopwatch.GetTimestamp();
        var outcome = WorkflowExecutableCacheTelemetry.NotFoundOutcome;

        try
        {
            var executable = await load(artifactId, CancellationToken.None);
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

    private bool TryGetCached(CacheKey key, out WorkflowExecutable? executable)
    {
        lock (_gate)
        {
            if (!_entries.TryGetValue(key, out var node))
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
        CacheKey key,
        WorkflowExecutable executable,
        LoadOperation operation,
        long mutationVersion)
    {
        var evictedForCapacity = false;

        lock (_gate)
        {
            if (mutationVersion != operation.Version
                || !_inFlight.TryGetValue(key, out var currentOperation)
                || !ReferenceEquals(operation, currentOperation))
                return;

            if (_entries.TryGetValue(key, out var existing))
            {
                _recency.Remove(existing);
                existing.Value = new CacheEntry(key, executable);
                _recency.AddFirst(existing);
                return;
            }

            var node = _recency.AddFirst(new CacheEntry(key, executable));
            _entries.Add(key, node);

            if (_entries.Count > _capacity)
            {
                var leastRecentlyUsed = _recency.Last!;
                _recency.RemoveLast();
                _entries.Remove(leastRecentlyUsed.Value.Key);
                evictedForCapacity = true;
            }
        }

        if (evictedForCapacity)
            RecordEviction(WorkflowExecutableCacheTelemetry.CapacityReason);
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

    private readonly record struct CacheKey(string Partition, string ArtifactId);

    private sealed class LoadOperation
    {
        private long _version;

        public TaskCompletionSource<WorkflowExecutable?> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public long Version => Volatile.Read(ref _version);

        public void Invalidate() => Interlocked.Increment(ref _version);
    }

    private sealed record CacheEntry(CacheKey Key, WorkflowExecutable Executable);
}
