using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Default single-frame scoped implementation of <see cref="IRuntimeConsumedSchedulerWorkClaimAccessor"/>. A drain
/// dispatches one work item at a time within its scope, so a single mutable frame is sufficient: <see cref="Begin"/>
/// stages the claim and clears the durably-consumed flag; disposing the returned handle clears the frame.
/// </summary>
public sealed class RuntimeConsumedSchedulerWorkClaimAccessor : IRuntimeConsumedSchedulerWorkClaimAccessor
{
    private ConsumedSchedulerWorkItem? _pending;
    private bool _consumed;

    public ConsumedSchedulerWorkItem? PendingConsume => _pending;

    public bool WasConsumedDurably => _consumed;

    public IDisposable Begin(ConsumedSchedulerWorkItem consume)
    {
        ArgumentNullException.ThrowIfNull(consume);
        _pending = consume;
        _consumed = false;
        return new Frame(this);
    }

    public void MarkConsumedDurably(string workItemId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workItemId);
        if (_pending is { } pending && StringComparer.Ordinal.Equals(pending.WorkItemId, workItemId))
            _consumed = true;
    }

    private sealed class Frame(RuntimeConsumedSchedulerWorkClaimAccessor accessor) : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            accessor._pending = null;
            accessor._consumed = false;
            _disposed = true;
        }
    }
}
