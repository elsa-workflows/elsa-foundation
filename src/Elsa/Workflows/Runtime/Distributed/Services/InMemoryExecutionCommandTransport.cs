using System.Collections.Concurrent;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// In-memory <see cref="IExecutionCommandTransport"/> for single-process composition and the two-node test harness. A
/// single instance shared by every node container is the cluster's command inbox; lease/ack are serialized under a lock
/// so a command leased by one node is invisible to another until acked or expired. A durable (Groundwork) implementation
/// backed by the <see cref="DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind"/> document kind is
/// a named follow-up; this in-memory store already froze the wire shape via the committed golden fixture.
/// </summary>
public sealed class InMemoryExecutionCommandTransport : IExecutionCommandTransport
{
    private readonly object _syncRoot = new();
    private readonly ConcurrentDictionary<string, List<ExecutionCommandTransportItem>> _inboxByExecutionId = new(StringComparer.Ordinal);
    private long _sequence;

    public ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var sequence = ++_sequence;
            var item = new ExecutionCommandTransportItem(
                transportItemId: $"transport:{workflowExecutionId}:{sequence}",
                workflowExecutionId: workflowExecutionId,
                envelope: envelope,
                sequence: sequence,
                enqueuedAt: now);

            var inbox = _inboxByExecutionId.GetOrAdd(workflowExecutionId, _ => new List<ExecutionCommandTransportItem>());
            inbox.Add(item);
            return new ValueTask<ExecutionCommandTransportItem>(item);
        }
    }

    public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        cancellationToken.ThrowIfCancellationRequested();

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        if (maxItems <= 0)
            throw new ArgumentOutOfRangeException(nameof(maxItems), "Max items must be greater than zero.");

        lock (_syncRoot)
        {
            if (!_inboxByExecutionId.TryGetValue(workflowExecutionId, out var inbox))
                return new ValueTask<IReadOnlyList<ExecutionCommandTransportItem>>(Array.Empty<ExecutionCommandTransportItem>());

            var leaseExpiresAt = now + leaseDuration;
            var leased = new List<ExecutionCommandTransportItem>();

            for (var i = 0; i < inbox.Count && leased.Count < maxItems; i++)
            {
                var item = inbox[i];
                if (!item.IsVisible(now))
                    continue;

                var leasedItem = item.Lease(ownerId, leaseExpiresAt);
                inbox[i] = leasedItem;
                leased.Add(leasedItem);
            }

            return new ValueTask<IReadOnlyList<ExecutionCommandTransportItem>>(leased);
        }
    }

    public ValueTask<bool> AckAsync(string workflowExecutionId, string transportItemId, string ownerId, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            if (!_inboxByExecutionId.TryGetValue(workflowExecutionId, out var inbox))
                return new ValueTask<bool>(false);

            var index = inbox.FindIndex(item => string.Equals(item.TransportItemId, transportItemId, StringComparison.Ordinal));
            if (index < 0)
                return new ValueTask<bool>(false);

            var current = inbox[index];

            // Only the node that currently holds a live lease may ack. If the lease expired and another node re-leased
            // it, the superseded node's ack is refused so the survivor stays responsible for the command.
            if (!string.Equals(current.LeasedByOwnerId, ownerId, StringComparison.Ordinal) || current.IsVisible(now))
                return new ValueTask<bool>(false);

            inbox.RemoveAt(index);
            if (inbox.Count == 0)
                _inboxByExecutionId.TryRemove(KeyValuePair.Create(workflowExecutionId, inbox));

            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            IReadOnlyCollection<string> ids = _inboxByExecutionId
                .Where(pair => pair.Value.Any(item => item.IsVisible(now)))
                .Select(pair => pair.Key)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<string>>(ids);
        }
    }

    public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowExecutionId);
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var count = _inboxByExecutionId.TryGetValue(workflowExecutionId, out var inbox) ? inbox.Count : 0;
            return new ValueTask<int>(count);
        }
    }
}
