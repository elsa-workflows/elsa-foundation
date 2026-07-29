using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeQueueDrainWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_queue_contract_and_literal_golden_digest()
    {
        var adapter = new QueueAdapter();
        var result = await new RuntimeQueueDrainWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeQueueDrainWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeQueueDrainWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeQueueDrainWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal(3, adapter.Opened.Count);
        Assert.Equal(3, adapter.Opened.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(RuntimeQueueDrainWorkload.WorkflowCount * RuntimeQueueDrainWorkload.WorkItemsPerWorkflow - 11, adapter.Shared.QueueItemCount);
        Assert.Equal(3, adapter.Shared.Poison.Count);
        Assert.Equal(5, adapter.Shared.StaleAcknowledgements);
        Assert.Equal(RuntimeQueueDrainWorkload.BatchSize * RuntimeQueueDrainWorkload.ConcurrentClaimants, adapter.Shared.ContentionAttempts);
    }

    [Theory]
    [InlineData(QueueFault.AliasInitialClients)]
    [InlineData(QueueFault.SeparateInitialBacking)]
    [InlineData(QueueFault.SameReopenedClient)]
    [InlineData(QueueFault.FreshReopenedBacking)]
    [InlineData(QueueFault.ResponseOnlySeed)]
    [InlineData(QueueFault.UnderfillWorkflowPage)]
    [InlineData(QueueFault.ReverseWorkflowPage)]
    [InlineData(QueueFault.ReturnShiftedClaimTime)]
    [InlineData(QueueFault.StaleAccepted)]
    [InlineData(QueueFault.ResponseOnlyPoison)]
    [InlineData(QueueFault.WrongReopenedQueueHead)]
    public async Task Fails_closed_when_a_public_queue_contract_surface_drifts(QueueFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeQueueDrainWorkload().ExecuteAsync(new QueueAdapter(fault)).AsTask());
    }

    [Fact]
    public async Task Stale_acknowledgements_are_attempted_while_successors_remain_current()
    {
        var adapter = new QueueAdapter();
        await new RuntimeQueueDrainWorkload().ExecuteAsync(adapter);

        Assert.Equal(5, adapter.Shared.StaleAcknowledgements);
        Assert.Equal(5, adapter.Shared.ActiveSuccessorsAtStaleAcknowledgement);
    }

    [Fact]
    public void Public_adapter_surface_contains_only_provider_neutral_runtime_contracts()
    {
        var types = new[] { typeof(IRuntimeQueueDrainWorkloadAdapter), typeof(RuntimeQueueDrainClients), typeof(RuntimeQueueDrainClient) };
        var surface = types.SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IRuntimeQueueDrainWorkloadAdapter).ToString());

        Assert.DoesNotContain(surface, value =>
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class QueueAdapter(QueueFault fault = QueueFault.None) : IRuntimeQueueDrainWorkloadAdapter
    {
        private readonly QueueBacking _secondary = fault == QueueFault.SeparateInitialBacking ? new() : null!;
        public QueueBacking Shared { get; } = new();
        public List<QueuePublicStore> Opened { get; } = [];

        public ValueTask<RuntimeQueueDrainClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open(Shared);
            var secondary = fault == QueueFault.AliasInitialClients ? primary : Open(fault == QueueFault.SeparateInitialBacking ? _secondary : Shared);
            return new(new RuntimeQueueDrainClients(primary.Client, secondary.Client));
        }

        public ValueTask<RuntimeQueueDrainClient> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(fault == QueueFault.SameReopenedClient ? Opened[0].Client : Open(fault == QueueFault.FreshReopenedBacking ? new QueueBacking() : Shared).Client);
        }

        private QueuePublicStore Open(QueueBacking backing)
        {
            var store = new QueuePublicStore(backing, fault);
            Opened.Add(store);
            return store;
        }
    }

    private sealed class QueueBacking
    {
        public InMemoryWorkflowSchedulerWorkQueue Queue { get; } = new();
        public Dictionary<(string WorkflowExecutionId, string WorkItemId), RuntimeSchedulerPoisonRecord> Poison { get; } = [];
        public int ContentionAttempts;
        public int StaleAcknowledgements { get; set; }
        public int ActiveSuccessorsAtStaleAcknowledgement { get; set; }
        public int QueueItemCount => Queue.ListPendingWorkflowExecutionIdsAsync(RuntimeStorePageRequest.MaximumLimit).Result
            .Sum(id => Queue.ListAsync(new RuntimeSchedulerWorkQuery(id, RuntimeStorePageRequest.MaximumLimit)).Result.Items.Count);
    }

    private sealed class QueuePublicStore : IWorkflowSchedulerWorkQueue, IWorkflowSchedulerPoisonStore, IWorkflowSchedulerWorkClaimInspection
    {
        private readonly QueueBacking _backing;
        private readonly QueueFault _fault;
        public RuntimeQueueDrainClient Client { get; }

        public QueuePublicStore(QueueBacking backing, QueueFault fault)
        {
            _backing = backing;
            _fault = fault;
            Client = new RuntimeQueueDrainClient(this, this, this);
        }

        public bool SupportsClaimTransitions => _backing.Queue.SupportsClaimTransitions;

        public ValueTask<RuntimeSchedulerWorkItem> EnqueueAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default)
        {
            if (_fault == QueueFault.ResponseOnlySeed && workItem.WorkItemId == "scheduler-work-next-0127-31")
                return new(workItem);
            return _backing.Queue.EnqueueAsync(workItem, cancellationToken);
        }

        public async ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query, CancellationToken cancellationToken = default)
        {
            var page = await _backing.Queue.ListAsync(query, cancellationToken);
            if (_fault == QueueFault.WrongReopenedQueueHead && query.Limit == 1 && query.WorkflowExecutionId == "scheduler-workflow-0000" && page.Items.Count == 1)
            {
                var wrong = new RuntimeSchedulerWorkItem("wrong-queue-head", query.WorkflowExecutionId, "wrong-command", WorkflowExecutionCommandKind.RunSchedulerWork,
                    "wrong-envelope", "wrong-key", page.Items[0].EnqueuedAt, page.Items[0].RecordedAt, page.Items[0].Sequence);
                return new RuntimeStorePage<RuntimeSchedulerWorkItem>(query, [wrong], page.NextContinuationToken);
            }
            return page;
        }

        public ValueTask<RuntimeSchedulerWorkItem?> DequeueAsync(string workflowExecutionId, CancellationToken cancellationToken = default) =>
            _backing.Queue.DequeueAsync(workflowExecutionId, cancellationToken);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default) =>
            _backing.Queue.DeleteAsync(workflowExecutionId, workItemId, cancellationToken);

        public async ValueTask<IReadOnlyCollection<string>> ListPendingWorkflowExecutionIdsAsync(int limit, CancellationToken cancellationToken = default)
        {
            var ids = await _backing.Queue.ListPendingWorkflowExecutionIdsAsync(limit, cancellationToken);
            if (_fault == QueueFault.UnderfillWorkflowPage)
                return ids.Take(Math.Max(0, limit - 1)).ToArray();
            return _fault == QueueFault.ReverseWorkflowPage ? ids.Reverse().ToArray() : ids;
        }

        public async ValueTask<RuntimeSchedulerWorkClaim?> ClaimAsync(RuntimeSchedulerWorkClaimRequest request, CancellationToken cancellationToken = default)
        {
            if (request.OwnerId.StartsWith("queue-contender-", StringComparison.Ordinal))
                Interlocked.Increment(ref _backing.ContentionAttempts);
            var claim = await _backing.Queue.ClaimAsync(request, cancellationToken);
            if (claim is not null && _fault == QueueFault.ReturnShiftedClaimTime)
            {
                return new RuntimeSchedulerWorkClaim(claim.Item, claim.OwnerId, claim.FencingToken, claim.Revision,
                    claim.ClaimedAt.AddSeconds(1), claim.VisibleAfter.AddSeconds(1));
            }
            return claim;
        }

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> RenewClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken cancellationToken = default) =>
            _backing.Queue.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public async ValueTask<RuntimeSchedulerWorkClaimTransitionResult> CompleteClaimAsync(RuntimeSchedulerWorkClaim claim, CancellationToken cancellationToken = default)
        {
            var result = await _backing.Queue.CompleteClaimAsync(claim, cancellationToken);
            if (result.Status == RuntimeSchedulerWorkClaimTransitionStatus.Stale)
            {
                _backing.StaleAcknowledgements++;
                var active = await _backing.Queue.ListActiveClaimsAsync(claim.Item.WorkflowExecutionId, claim.ClaimedAt.AddMinutes(1).AddSeconds(1), cancellationToken);
                _backing.ActiveSuccessorsAtStaleAcknowledgement += active.Count;
                if (_fault == QueueFault.StaleAccepted)
                    return RuntimeSchedulerWorkClaimTransitionResult.Applied();
            }
            return result;
        }

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ReleaseClaimAsync(RuntimeSchedulerWorkClaim claim, DateTimeOffset visibleAt, CancellationToken cancellationToken = default) =>
            _backing.Queue.ReleaseClaimAsync(claim, visibleAt, cancellationToken);

        public ValueTask<RuntimeSchedulerWorkClaimTransitionResult> ConsumeClaimedAsync(ConsumedSchedulerWorkItem consumed, CancellationToken cancellationToken = default) =>
            _backing.Queue.ConsumeClaimedAsync(consumed, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerWorkClaim>> ListActiveClaimsAsync(string workflowExecutionId, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            _backing.Queue.ListActiveClaimsAsync(workflowExecutionId, now, cancellationToken);

        public ValueTask<RuntimeSchedulerPoisonRecord> RecordAsync(RuntimeSchedulerPoisonRecord record, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_fault != QueueFault.ResponseOnlyPoison)
                _backing.Poison[(record.WorkflowExecutionId, record.WorkItemId)] = record;
            return new(record);
        }

        public ValueTask<RuntimeSchedulerPoisonRecord?> FindAsync(string workflowExecutionId, string workItemId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(_backing.Poison.GetValueOrDefault((workflowExecutionId, workItemId)));
        }

        public ValueTask<IReadOnlyCollection<RuntimeSchedulerPoisonRecord>> ListAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new((IReadOnlyCollection<RuntimeSchedulerPoisonRecord>)_backing.Poison.Values
                .Where(record => record.WorkflowExecutionId == workflowExecutionId)
                .OrderBy(record => record.FirstFailedAt)
                .ThenBy(record => record.WorkItemId, StringComparer.Ordinal)
                .ToArray());
        }
    }
}

public enum QueueFault
{
    None,
    AliasInitialClients,
    SeparateInitialBacking,
    SameReopenedClient,
    FreshReopenedBacking,
    ResponseOnlySeed,
    UnderfillWorkflowPage,
    ReverseWorkflowPage,
    ReturnShiftedClaimTime,
    StaleAccepted,
    ResponseOnlyPoison,
    WrongReopenedQueueHead
}
