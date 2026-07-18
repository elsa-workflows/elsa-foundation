using System.Collections.Concurrent;
using Elsa.Persistence.Core;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;

namespace Elsa.Workflows.Runtime.Distributed.Services;

/// <summary>
/// In-memory <see cref="IExecutionCommandTransport"/> for single-process composition and the two-node test harness.
/// Scoped, partition-bound adapters coordinate through singleton shared inbox state; lease and acknowledgement are
/// serialized under a lock so a command leased by one node is invisible to another until acked or expired. The opt-in
/// Groundwork persistence feature replaces this adapter with a durable scoped implementation backed by the
/// <see cref="DistributedRuntimeStorageManifest.ExecutionCommandTransportDocumentKind"/> document kind and the same
/// committed golden-fixture wire shape.
/// </summary>
public sealed class InMemoryExecutionCommandTransport : IExecutionCommandTransport
{
    private readonly InMemoryExecutionCommandTransportState _state;
    private readonly string _partition;

    public InMemoryExecutionCommandTransport()
        : this(new InMemoryExecutionCommandTransportState(), new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue))
    {
    }

    internal InMemoryExecutionCommandTransport(
        InMemoryExecutionCommandTransportState state,
        IPersistenceAccessContextAccessor accessContextAccessor)
        : this(state, InMemoryExecutionPartitionResolver.Resolve(accessContextAccessor))
    {
    }

    private InMemoryExecutionCommandTransport(
        InMemoryExecutionCommandTransportState state,
        WorkflowExecutionPartition partition)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(partition);
        _state = state;
        _partition = partition.Value;
    }

    public ValueTask<ExecutionCommandTransportItem> SendAsync(string workflowExecutionId, WorkflowExecutionCommandEnvelope envelope, DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentNullException.ThrowIfNull(envelope);
        cancellationToken.ThrowIfCancellationRequested();
        if (!StringComparer.Ordinal.Equals(envelope.Partition.Value, _partition))
            throw new InvalidOperationException("The command envelope does not belong to the current execution partition.");

        lock (_state.SyncRoot)
        {
            var key = Key(workflowExecutionId);
            _state.SequencesByInbox.TryGetValue(key, out var currentSequence);
            var sequence = currentSequence + 1;
            _state.SequencesByInbox[key] = sequence;
            var item = new ExecutionCommandTransportItem(
                transportItemId: $"transport:{workflowExecutionId}:{sequence}",
                workflowExecutionId: workflowExecutionId,
                envelope: envelope,
                sequence: sequence,
                enqueuedAt: now);

            var inbox = _state.Inboxes.GetOrAdd(key, _ => new List<ExecutionCommandTransportItem>());
            inbox.Add(item);
            return new ValueTask<ExecutionCommandTransportItem>(item);
        }
    }

    public ValueTask<IReadOnlyList<ExecutionCommandTransportItem>> LeaseAsync(string workflowExecutionId, string ownerId, DateTimeOffset now, TimeSpan leaseDuration, int maxItems, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        cancellationToken.ThrowIfCancellationRequested();

        if (leaseDuration <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(leaseDuration), "Lease duration must be positive.");
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));

        lock (_state.SyncRoot)
        {
            if (!_state.Inboxes.TryGetValue(Key(workflowExecutionId), out var inbox))
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
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        ArgumentException.ThrowIfNullOrWhiteSpace(transportItemId);
        DistributedRuntimeIdentityConstraints.Validate(ownerId, nameof(ownerId));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var key = Key(workflowExecutionId);
            if (!_state.Inboxes.TryGetValue(key, out var inbox))
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
                _state.Inboxes.TryRemove(KeyValuePair.Create(key, inbox));

            return new ValueTask<bool>(true);
        }
    }

    public ValueTask<IReadOnlyCollection<string>> ListPendingExecutionIdsAsync(
        DateTimeOffset now,
        int maxItems,
        CancellationToken cancellationToken = default)
    {
        DistributedRuntimeQueryLimits.ValidateTake(maxItems, nameof(maxItems));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            IReadOnlyCollection<string> ids = _state.Inboxes
                .Where(pair => StringComparer.Ordinal.Equals(pair.Key.Partition, _partition))
                .Where(pair => pair.Value.Any(item => item.IsVisible(now)))
                .Select(pair => pair.Key.WorkflowExecutionId)
                .OrderBy(id => id, StringComparer.Ordinal)
                .Take(maxItems)
                .ToArray();

            return new ValueTask<IReadOnlyCollection<string>>(ids);
        }
    }

    public ValueTask<int> CountPendingAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
    {
        DistributedRuntimeIdentityConstraints.Validate(workflowExecutionId, nameof(workflowExecutionId));
        cancellationToken.ThrowIfCancellationRequested();

        lock (_state.SyncRoot)
        {
            var count = _state.Inboxes.TryGetValue(Key(workflowExecutionId), out var inbox) ? inbox.Count : 0;
            return new ValueTask<int>(count);
        }
    }

    private InMemoryExecutionCommandTransportKey Key(string workflowExecutionId) => new(_partition, workflowExecutionId);
}

internal sealed class InMemoryExecutionCommandTransportState
{
    public object SyncRoot { get; } = new();
    public ConcurrentDictionary<InMemoryExecutionCommandTransportKey, List<ExecutionCommandTransportItem>> Inboxes { get; } = new();
    public Dictionary<InMemoryExecutionCommandTransportKey, long> SequencesByInbox { get; } = new();
}

internal readonly record struct InMemoryExecutionCommandTransportKey(string Partition, string WorkflowExecutionId);
