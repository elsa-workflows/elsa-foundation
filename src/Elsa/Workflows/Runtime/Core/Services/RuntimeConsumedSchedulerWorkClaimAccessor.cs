using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default single-frame scoped implementation of <see cref="IRuntimeConsumedSchedulerWorkClaimAccessor"/>. A drain
/// dispatches one work item at a time within its scope, so a single mutable frame is sufficient: <see cref="Begin"/>
/// stages the claim and clears the durably-consumed flag; disposing the returned handle clears the frame.
/// </summary>
/// <remarks>
/// The frame is written by the dispatch thread and read by the drainer's claim-renewal loop, which runs on its own
/// task for the duration of the dispatch (#1254). Only the two consume signals cross that boundary, so they are the
/// only members that need the volatile read/interlocked write.
/// </remarks>
public sealed class RuntimeConsumedSchedulerWorkClaimAccessor : IRuntimeConsumedSchedulerWorkClaimAccessor
{
    private ConsumedSchedulerWorkItem? _pending;
    private volatile bool _consumed;
    private int _consumeAttempts;

    public ConsumedSchedulerWorkItem? PendingConsume => _pending;

    public bool WasConsumedDurably => _consumed;

    public IDisposable Begin(ConsumedSchedulerWorkItem consume)
    {
        ArgumentNullException.ThrowIfNull(consume);
        _pending = consume;
        Reset();
        return new Frame(this);
    }

    public void MarkConsumedDurably(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        if (IsPending(workItemId))
            _consumed = true;
    }

    public IDisposable BeginConsumeAttempt(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        if (!IsPending(workItemId))
            return NoopAttempt.Instance;
        Interlocked.Increment(ref _consumeAttempts);
        return new Attempt(this);
    }

    public bool IsConsumeInFlightOrDurable(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        return IsPending(workItemId) && (_consumed || Volatile.Read(ref _consumeAttempts) > 0);
    }

    private bool IsPending(string workItemId) =>
        _pending is { } pending && StringComparer.Ordinal.Equals(pending.WorkItemId, workItemId);

    private void Reset()
    {
        _consumed = false;
        Volatile.Write(ref _consumeAttempts, 0);
    }

    private sealed class Frame(RuntimeConsumedSchedulerWorkClaimAccessor accessor) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            accessor._pending = null;
            accessor.Reset();
            _disposed = true;
        }
    }

    private sealed class Attempt(RuntimeConsumedSchedulerWorkClaimAccessor accessor) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            Interlocked.Decrement(ref accessor._consumeAttempts);
            _disposed = true;
        }
    }

    private sealed class NoopAttempt : IDisposable
    {
        public static readonly NoopAttempt Instance = new();

        public void Dispose()
        {
        }
    }
}
