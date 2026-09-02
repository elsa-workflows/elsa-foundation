using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeOutboxDrainWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_outbox_contract_and_literal_golden_digest()
    {
        var adapter = new OutboxAdapter();
        var result = await new RuntimeOutboxDrainWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeOutboxDrainWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeOutboxDrainWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeOutboxDrainWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal(RuntimeOutboxDrainWorkload.OutboxEntryCount, adapter.Shared.Items.Count);
        Assert.Equal(3, adapter.Opened.Count);
        Assert.Equal(3, adapter.Opened.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.Equal(2, adapter.Shared.ContentionAttempts);
        Assert.Equal(RuntimeOutboxDrainWorkload.BatchSize, adapter.Shared.PrimaryClaims.Count);
        Assert.Equal(RuntimeOutboxDrainWorkload.BatchSize, adapter.Shared.PrimaryCompletions);
        Assert.Equal(adapter.Shared.ClaimRequestTimes.Order(), adapter.Shared.ClaimRequestTimes);
        Assert.True(adapter.Shared.Events.LastIndexOf("complete-primary") < adapter.Shared.Events.IndexOf("reclaim-sentinel"));
        Assert.True(adapter.Shared.Events.IndexOf("reclaim-sentinel") < adapter.Shared.Events.IndexOf("attempt-stale-sentinel-completion"));
    }

    [Fact]
    public async Task Prepares_exactly_the_frozen_bounded_public_operations()
    {
        var adapter = new OutboxAdapter();

        var operations = await new RuntimeOutboxDrainWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.Get(RuntimeOutboxDrainWorkload.WorkloadId).OperationSequence,
            operations.Select(operation => operation.Id));
        Assert.Equal(5, operations.Count);
        foreach (var operation in operations)
        {
            await operation.PrepareInvocationAsync(-1);
            await operation.InvokeAsync(-1);
        }
        Assert.Equal(
            RuntimeOutboxDrainWorkload.BatchSize - RuntimeOutboxDrainWorkload.RetryableEntries,
            adapter.Shared.Items.Values.Count(item => item.Status == RuntimePostCommitOutboxStatus.Delivered));
        Assert.Equal(
            RuntimeOutboxDrainWorkload.RetryableEntries,
            adapter.Shared.Items.Values.Count(item => item.Status == RuntimePostCommitOutboxStatus.FailedRetryable));
        var measuredClaim = Assert.Single(
            adapter.Shared.ClaimRequests,
            request => request.OwnerId.StartsWith("measured-claim-", StringComparison.Ordinal));
        Assert.Null(measuredClaim.WorkflowExecutionId);
        Assert.Null(measuredClaim.IntentKind);
    }

    [Theory]
    [InlineData(OutboxFault.AliasInitialClients)]
    [InlineData(OutboxFault.SeparateInitialBacking)]
    [InlineData(OutboxFault.SameReopenedClient)]
    [InlineData(OutboxFault.FreshReopenedBacking)]
    [InlineData(OutboxFault.ResponseOnlySeed)]
    [InlineData(OutboxFault.IgnoreDue)]
    [InlineData(OutboxFault.IgnoreScope)]
    [InlineData(OutboxFault.MissingSentinel)]
    [InlineData(OutboxFault.WrongSentinelIdentity)]
    [InlineData(OutboxFault.IgnoreLimit)]
    [InlineData(OutboxFault.ReverseOrder)]
    [InlineData(OutboxFault.DuplicateClaim)]
    [InlineData(OutboxFault.WrongReclaimFence)]
    [InlineData(OutboxFault.ReturnWrongOwner)]
    [InlineData(OutboxFault.SentinelWrongOwner)]
    [InlineData(OutboxFault.SentinelWrongState)]
    [InlineData(OutboxFault.StaleAccepted)]
    [InlineData(OutboxFault.ResponseOnlyCompletion)]
    [InlineData(OutboxFault.EarlyRetry)]
    [InlineData(OutboxFault.LateRetry)]
    [InlineData(OutboxFault.WrongReread)]
    [InlineData(OutboxFault.WrongDeliveredIdentity)]
    public async Task Fails_closed_when_a_public_outbox_contract_surface_drifts(OutboxFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutboxDrainWorkload().ExecuteAsync(new OutboxAdapter(fault)).AsTask());
    }

    [Fact]
    public async Task Return_only_sentinel_state_fabrication_reaches_exact_current_claim_validation()
    {
        var adapter = new OutboxAdapter(OutboxFault.SentinelWrongState);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutboxDrainWorkload().ExecuteAsync(adapter).AsTask());

        Assert.Contains("exact current public claim", exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, adapter.Shared.ContentionClaims);
    }

    [Fact]
    public async Task Return_only_delivered_identity_fabrication_reaches_exact_reopened_state_validation()
    {
        var adapter = new OutboxAdapter(OutboxFault.WrongDeliveredIdentity);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutboxDrainWorkload().ExecuteAsync(adapter).AsTask());

        Assert.Contains("delivered outbox result", exception.Message, StringComparison.Ordinal);
        var persisted = adapter.Shared.Items["outbox-due-0007"];
        Assert.Equal("outbox-due-0007", persisted.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, persisted.Status);
        Assert.Null(persisted.DeliveringOwnerId);
        Assert.Null(persisted.DeliveryStartedAt);
        Assert.Null(persisted.DeliveryVisibleAfter);
    }

    [Theory]
    [InlineData(OutboxFault.ReturnShiftedClaimTimes, "exact bounded ordered current ownership")]
    [InlineData(OutboxFault.PersistShiftedClaimTimes, "expected current claim after reopened retry")]
    public async Task Claim_time_drift_reaches_its_public_state_boundary(OutboxFault fault, string expectedMessage)
    {
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutboxDrainWorkload().ExecuteAsync(new OutboxAdapter(fault)).AsTask());

        Assert.Contains(expectedMessage, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Unrelated_completion_failure_is_not_counted_as_stale_claim_rejection()
    {
        var adapter = new OutboxAdapter(OutboxFault.UnrelatedStaleFailure);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeOutboxDrainWorkload().ExecuteAsync(adapter).AsTask());

        Assert.Equal("Synthetic unrelated completion failure.", exception.Message);
        var persisted = adapter.Shared.Items["outbox-due-0032"];
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, persisted.Status);
        Assert.Equal("outbox-sentinel-successor", persisted.DeliveringOwnerId);
        Assert.Equal(2, persisted.DeliveryFencingToken);
    }

    [Fact]
    public void Public_adapter_surface_contains_only_provider_neutral_runtime_contracts()
    {
        var types = new[] { typeof(IRuntimeOutboxDrainWorkloadAdapter), typeof(RuntimeOutboxDrainClients), typeof(RuntimeOutboxDrainClient) };
        var surface = types.SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IRuntimeOutboxDrainWorkloadAdapter).ToString());

        Assert.DoesNotContain(surface, value =>
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class OutboxAdapter(OutboxFault fault = OutboxFault.None) : IRuntimeOutboxDrainWorkloadAdapter
    {
        private readonly OutboxBacking _secondary = fault == OutboxFault.SeparateInitialBacking ? new() : null!;
        public OutboxBacking Shared { get; } = new();
        public List<OutboxPublicStore> Opened { get; } = [];

        public ValueTask<RuntimeOutboxDrainClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open(Shared);
            var secondary = fault == OutboxFault.AliasInitialClients ? primary : Open(fault == OutboxFault.SeparateInitialBacking ? _secondary : Shared);
            return new(new RuntimeOutboxDrainClients(primary.Client, secondary.Client));
        }

        public ValueTask<RuntimeOutboxDrainClient> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(fault == OutboxFault.SameReopenedClient ? Opened[0].Client : Open(fault == OutboxFault.FreshReopenedBacking ? new OutboxBacking() : Shared).Client);
        }

        private OutboxPublicStore Open(OutboxBacking backing)
        {
            var client = new OutboxPublicStore(backing, fault);
            Opened.Add(client);
            return client;
        }
    }

    private sealed class OutboxBacking
    {
        public object Gate { get; } = new();
        public Dictionary<string, RuntimePostCommitOutboxItem> Items { get; } = new(StringComparer.Ordinal);
        public HashSet<string> Commits { get; } = new(StringComparer.Ordinal);
        public List<RuntimePostCommitOutboxClaim> PrimaryClaims { get; } = [];
        public int ContentionClaims { get; set; }
        public int ContentionAttempts { get; set; }
        public int PrimaryCompletions { get; set; }
        public List<DateTimeOffset> ClaimRequestTimes { get; } = [];
        public List<RuntimePostCommitOutboxClaimRequest> ClaimRequests { get; } = [];
        public List<string> Events { get; } = [];
    }

    private sealed class OutboxPublicStore :
        IRuntimeCheckpointCommitStore, IRuntimePostCommitOutboxClaimStore, IRuntimePostCommitOutboxClaimCompletionStore, IPostCommitOutboxLookupStore
    {
        private static readonly RuntimeCheckpointPersistenceDecision Immediate = new(RuntimeCheckpointPersistenceMode.Immediate);
        private readonly OutboxBacking _backing;
        private readonly OutboxFault _fault;
        public RuntimeOutboxDrainClient Client { get; }

        public OutboxPublicStore(OutboxBacking backing, OutboxFault fault)
        {
            _backing = backing;
            _fault = fault;
            Client = new RuntimeOutboxDrainClient(this, this, this, this);
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (decision.Mode != Immediate.Mode)
                throw new InvalidOperationException("The fake only admits immediate public checkpoint commits.");
            lock (_backing.Gate)
            {
                if (!_backing.Commits.Add(commit.CommitId))
                    throw new InvalidOperationException("The fake received a duplicate seed commit.");
                var ids = commit.StateChanges.PostCommitOutbox.Select(change => change.State.OutboxItemId).ToArray();
                if (_fault != OutboxFault.ResponseOnlySeed)
                {
                    foreach (var change in commit.StateChanges.PostCommitOutbox)
                        _backing.Items.Add(change.State.OutboxItemId, change.State);
                }
                return new(new RuntimeCheckpointCommitStoreResult(ids));
            }
        }

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(RuntimePostCommitOutboxClaimRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                _backing.ClaimRequestTimes.Add(request.Now);
                _backing.ClaimRequests.Add(request);
                if (request.OwnerId.StartsWith("outbox-contender-", StringComparison.Ordinal))
                    _backing.ContentionAttempts++;
                if (_fault == OutboxFault.MissingSentinel && request.WorkflowExecutionId == "outbox-drain-contention")
                    return new((IReadOnlyCollection<RuntimePostCommitOutboxClaim>)Array.Empty<RuntimePostCommitOutboxClaim>());
                var ignoresScope = _fault is OutboxFault.IgnoreScope or OutboxFault.WrongSentinelIdentity;
                var eligibilityRequest = ignoresScope && request.WorkflowExecutionId is not null
                    ? new RuntimePostCommitOutboxClaimRequest(request.OwnerId, request.Now, request.VisibilityTimeout, request.Limit, intentKind: request.IntentKind)
                    : request;
                IEnumerable<RuntimePostCommitOutboxItem> candidates = _backing.Items.Values.Where(item =>
                    (ignoresScope || request.WorkflowExecutionId is null || item.Intent.WorkflowExecutionId == request.WorkflowExecutionId) &&
                    (_fault == OutboxFault.IgnoreDue || RuntimePostCommitOutboxClaimTransitions.CanClaim(item, eligibilityRequest)));
                if (_fault == OutboxFault.WrongSentinelIdentity && request.WorkflowExecutionId == "outbox-drain-contention")
                    candidates = candidates.Where(item => item.OutboxItemId != "outbox-due-0032");
                candidates = candidates.OrderBy(RuntimePostCommitOutboxClaimTransitions.ClaimableAt)
                    .ThenBy(item => item.RecordedAt)
                    .ThenBy(item => item.OutboxItemId, StringComparer.Ordinal);
                if (_fault == OutboxFault.ReverseOrder)
                    candidates = candidates.Reverse();
                var selected = candidates.Take(_fault == OutboxFault.IgnoreLimit ? int.MaxValue : request.Limit).ToArray();
                var claims = new List<RuntimePostCommitOutboxClaim>();
                foreach (var item in selected)
                {
                    var claim = RuntimePostCommitOutboxClaimTransitions.Claim(item, request);
                    var persistedClaimItem = claim.Item;
                    if (_fault == OutboxFault.ReturnShiftedClaimTimes)
                    {
                        var shiftedRequest = new RuntimePostCommitOutboxClaimRequest(
                            request.OwnerId,
                            request.Now.AddSeconds(1),
                            request.VisibilityTimeout,
                            request.Limit,
                            request.WorkflowExecutionId,
                            request.IntentKind);
                        claim = RuntimePostCommitOutboxClaimTransitions.Claim(item, shiftedRequest);
                    }
                    if (_fault == OutboxFault.PersistShiftedClaimTimes && request.OwnerId == "outbox-retry")
                    {
                        var shiftedRequest = new RuntimePostCommitOutboxClaimRequest(
                            request.OwnerId,
                            request.Now.AddSeconds(1),
                            request.VisibilityTimeout,
                            request.Limit,
                            request.WorkflowExecutionId,
                            request.IntentKind);
                        persistedClaimItem = RuntimePostCommitOutboxClaimTransitions.Claim(item, shiftedRequest).Item;
                    }
                    if (_fault == OutboxFault.WrongReclaimFence && claim.FencingToken > 1)
                        claim = new RuntimePostCommitOutboxClaim(claim.Item, claim.OwnerId, claim.FencingToken - 1, claim.ClaimedAt, claim.VisibleAfter);
                    if (_fault == OutboxFault.ReturnWrongOwner)
                        claim = new RuntimePostCommitOutboxClaim(claim.Item, "wrong-owner", claim.FencingToken, claim.ClaimedAt, claim.VisibleAfter);
                    if (_fault == OutboxFault.SentinelWrongOwner &&
                        item.Intent.WorkflowExecutionId == "outbox-drain-contention" &&
                        request.OwnerId.StartsWith("outbox-contender-", StringComparison.Ordinal))
                    {
                        claim = new RuntimePostCommitOutboxClaim(claim.Item, "wrong-sentinel-owner", claim.FencingToken, claim.ClaimedAt, claim.VisibleAfter);
                    }
                    if (_fault == OutboxFault.SentinelWrongState &&
                        item.Intent.WorkflowExecutionId == "outbox-drain-contention" &&
                        request.OwnerId.StartsWith("outbox-contender-", StringComparison.Ordinal))
                    {
                        claim = new RuntimePostCommitOutboxClaim(item, claim.OwnerId, claim.FencingToken, claim.ClaimedAt, claim.VisibleAfter);
                    }
                    if (_fault != OutboxFault.DuplicateClaim)
                        _backing.Items[item.OutboxItemId] = persistedClaimItem;
                    if (item.Intent.WorkflowExecutionId == "outbox-drain-contention")
                        _backing.ContentionClaims++;
                    if (request.OwnerId == "outbox-primary")
                    {
                        _backing.PrimaryClaims.Add(claim);
                        _backing.Events.Add("claim-primary");
                    }
                    else if (request.OwnerId == "outbox-sentinel-successor")
                    {
                        _backing.Events.Add("reclaim-sentinel");
                    }
                    claims.Add(claim);
                }
                return new((IReadOnlyCollection<RuntimePostCommitOutboxClaim>)claims);
            }
        }

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(claim, result), cancellationToken);

        public ValueTask CompleteClaimAsync(RuntimePostCommitOutboxClaimCompletion completion, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                if (completion.Claim.OwnerId == "outbox-primary")
                {
                    _backing.PrimaryCompletions++;
                    _backing.Events.Add("complete-primary");
                }
                else if (completion.Claim.OwnerId.StartsWith("outbox-contender-", StringComparison.Ordinal))
                {
                    _backing.Events.Add("attempt-stale-sentinel-completion");
                }
                var current = _backing.Items.GetValueOrDefault(completion.Claim.OutboxItemId)
                    ?? throw new InvalidOperationException("Outbox item was not found.");
                var stale = current.Status != RuntimePostCommitOutboxStatus.Delivering || current.DeliveringOwnerId != completion.Claim.OwnerId || current.DeliveryFencingToken != completion.Claim.FencingToken;
                if (stale && _fault == OutboxFault.UnrelatedStaleFailure)
                    throw new InvalidOperationException("Synthetic unrelated completion failure.");
                if (stale && _fault == OutboxFault.StaleAccepted)
                    return ValueTask.CompletedTask;
                var updated = RuntimePostCommitOutboxClaimTransitions.Complete(current, completion.Claim, completion.DeliveryResult);
                if (_fault == OutboxFault.EarlyRetry && updated.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
                    updated = Copy(updated, availableAt: completion.DeliveryResult.RecordedAt);
                if (_fault == OutboxFault.LateRetry && updated.Status == RuntimePostCommitOutboxStatus.FailedRetryable)
                    updated = Copy(updated, availableAt: completion.DeliveryResult.RecordedAt.AddMinutes(2));
                if (_fault != OutboxFault.ResponseOnlyCompletion)
                    _backing.Items[updated.OutboxItemId] = updated;
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                var item = _backing.Items.GetValueOrDefault(outboxItemId);
                if (item is not null && _fault == OutboxFault.WrongReread)
                    item = Copy(item, deliveryFencingToken: item.DeliveryFencingToken + 1);
                if (item is not null && _fault == OutboxFault.WrongDeliveredIdentity && item.Status == RuntimePostCommitOutboxStatus.Delivered)
                {
                    item = new RuntimePostCommitOutboxItem(
                        "unrelated-delivered-item",
                        item.Intent,
                        item.Status,
                        item.RecordedAt,
                        item.AvailableAt,
                        item.RetryPolicy,
                        item.DeliveryAttemptCount,
                        item.DeliveringOwnerId,
                        item.DeliveryStartedAt,
                        item.DeliveredAt,
                        item.LastFailureMessage,
                        item.Metadata,
                        item.DeliveryFencingToken,
                        item.DeliveryVisibleAfter);
                }
                return new(item);
            }
        }

        private static RuntimePostCommitOutboxItem Copy(RuntimePostCommitOutboxItem item, DateTimeOffset? availableAt = null, long? deliveryFencingToken = null) =>
            new(item.OutboxItemId, item.Intent, item.Status, item.RecordedAt, availableAt ?? item.AvailableAt, item.RetryPolicy, item.DeliveryAttemptCount,
                item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt, item.LastFailureMessage, item.Metadata,
                deliveryFencingToken ?? item.DeliveryFencingToken, item.DeliveryVisibleAfter);
    }
}

public enum OutboxFault
{
    None,
    AliasInitialClients,
    SeparateInitialBacking,
    SameReopenedClient,
    FreshReopenedBacking,
    ResponseOnlySeed,
    IgnoreDue,
    IgnoreScope,
    MissingSentinel,
    WrongSentinelIdentity,
    IgnoreLimit,
    ReverseOrder,
    DuplicateClaim,
    WrongReclaimFence,
    ReturnWrongOwner,
    SentinelWrongOwner,
    SentinelWrongState,
    StaleAccepted,
    ResponseOnlyCompletion,
    EarlyRetry,
    LateRetry,
    WrongReread,
    WrongDeliveredIdentity,
    ReturnShiftedClaimTimes,
    PersistShiftedClaimTimes,
    UnrelatedStaleFailure
}
