using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Elsa.Workflows.Runtime.Distributed.Services;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Distributed.Tests;

/// <summary>Drives a placement pump whose dispatches outlast the sweep (#2831).</summary>
internal static class PumpSweeps
{
    /// <summary>Fires the sweep's waits for local work until the sweep returns. A sweep that waits out a held drain never
    /// returns.</summary>
    public static async Task<ExecutionPlacementSweepResult> AdvancingAsync(ExecutionPlacementPumpTask pump, FakeTimeProvider clock)
    {
        var sweep = pump.SweepOnceAsync().AsTask();
        for (var attempt = 0; attempt < 500 && !sweep.IsCompleted; attempt++)
        {
            await Task.Delay(10);
            if (!sweep.IsCompleted)
                clock.Advance(TimeSpan.FromMilliseconds(100));
        }
        Assert.True(sweep.IsCompleted, "The sweep waited for a drain that keeps running.");
        return await sweep;
    }

    /// <summary>Waits for work the sweep left running to reach <paramref name="condition"/>.</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, string because)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        while (!await condition())
        {
            Assert.False(timeout.IsCancellationRequested, because);
            await Task.Delay(10);
        }
    }
}

/// <summary>Forwards to a command transport; a test overrides what it needs to observe or break.</summary>
internal class ForwardingCommandTransport(IExecutionCommandTransport inner) : IExecutionCommandTransport
{
    public virtual ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        inner.SendAsync(workflowExecutionId, envelope, now, cancellationToken);

    public virtual ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default) =>
        inner.LeaseAsync(workflowExecutionId, ownerId, now, leaseDuration, maxItems, cancellationToken);

    public virtual ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        inner.AckAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);

    public virtual ValueTask<bool> ReleaseLeaseAsync(string workflowExecutionId, string transportItemId, string ownerId, long leaseToken, DateTimeOffset now, CancellationToken cancellationToken = default) =>
        inner.ReleaseLeaseAsync(workflowExecutionId, transportItemId, ownerId, leaseToken, now, cancellationToken);

    public virtual ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> PeekAsync(string workflowExecutionId, DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default) =>
        inner.PeekAsync(workflowExecutionId, now, maxItems, cancellationToken);

    public virtual ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> ListLeasedAsync(string ownerId, DateTimeOffset now, int maxItems, string? workflowExecutionId = null, CancellationToken cancellationToken = default) =>
        inner.ListLeasedAsync(ownerId, now, maxItems, workflowExecutionId, cancellationToken);

    public virtual ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, CancellationToken cancellationToken = default) =>
        inner.ListPendingExecutionIdsAsync(now, maxItems, cancellationToken);

    public virtual ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, int maxItems, int skip, CancellationToken cancellationToken = default) =>
        inner.ListPendingExecutionIdsAsync(now, maxItems, skip, cancellationToken);

    public virtual ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
        inner.CountPendingAsync(workflowExecutionId, cancellationToken);
}
