using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeCheckpointCommitWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_checkpoint_contract_and_literal_golden_digest()
    {
        var adapter = new CheckpointAdapter();
        var result = await new RuntimeCheckpointCommitWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeCheckpointCommitWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeCheckpointCommitWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal("20bf6656047558018d39aab671014991d49d250b493ee1e27eae3db4dd431168", result.ObservableResults["committedBundleIdentityDigest"]);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeCheckpointCommitWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount, adapter.Shared.Markers.Count);
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount * RuntimeCheckpointCommitWorkload.ActivityChangesPerCheckpoint, adapter.Shared.Activities.Count);
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount * RuntimeCheckpointCommitWorkload.DurableValueChangesPerCheckpoint, adapter.Shared.DurableValues.Count);
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount * RuntimeCheckpointCommitWorkload.OutboxEntriesPerCheckpoint, adapter.Shared.Outbox.Count);
        Assert.Equal(3, adapter.OpenedClients.Count);
        Assert.Equal(3, adapter.OpenedClients.Distinct(ReferenceEqualityComparer.Instance).Count());
        Assert.All(adapter.Shared.ActivityPageRequests, request => Assert.Equal(3, request.Limit));
        Assert.All(adapter.Shared.DurableValuePageRequests, request => Assert.Equal(2, request.Limit));
        Assert.All(adapter.Shared.OutboxRequests, request => Assert.Equal(16, request.Limit));
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount, adapter.Shared.HeartbeatCount);
        Assert.Equal(RuntimeCheckpointCommitWorkload.CheckpointCount, adapter.Shared.RootWriteLeaseCount);
    }

    [Theory]
    [InlineData(CheckpointFault.AliasInitialClients)]
    [InlineData(CheckpointFault.AliasedComponentInstances)]
    [InlineData(CheckpointFault.DifferentInitialBacking)]
    [InlineData(CheckpointFault.SameReopenedClient)]
    [InlineData(CheckpointFault.FreshReopenedBacking)]
    [InlineData(CheckpointFault.ExecutableResponseOnly)]
    [InlineData(CheckpointFault.RootWriteLeaseUnavailable)]
    [InlineData(CheckpointFault.AcknowledgementDropsWork)]
    [InlineData(CheckpointFault.AcknowledgementBindsWrongCommit)]
    [InlineData(CheckpointFault.DropWorkflow)]
    [InlineData(CheckpointFault.DropActivity)]
    [InlineData(CheckpointFault.DropDurableValue)]
    [InlineData(CheckpointFault.DropOutbox)]
    [InlineData(CheckpointFault.PagingDrop)]
    [InlineData(CheckpointFault.PagingDuplicate)]
    [InlineData(CheckpointFault.PagingReorder)]
    [InlineData(CheckpointFault.PagingIgnoreContinuation)]
    [InlineData(CheckpointFault.PagingIgnoresLimit)]
    [InlineData(CheckpointFault.PagingFabricatedMember)]
    [InlineData(CheckpointFault.ReplayDuplicatesOutbox)]
    [InlineData(CheckpointFault.AcceptConflictingReplay)]
    [InlineData(CheckpointFault.StaleAccepted)]
    [InlineData(CheckpointFault.StalePartialMutation)]
    [InlineData(CheckpointFault.StaleLeaksReplayMarker)]
    [InlineData(CheckpointFault.IgnoreExpectedFence)]
    [InlineData(CheckpointFault.ExpiredLease)]
    [InlineData(CheckpointFault.HeartbeatRejected)]
    [InlineData(CheckpointFault.AlterReturnedPayload)]
    [InlineData(CheckpointFault.AlterReturnedActivityIdentity)]
    [InlineData(CheckpointFault.AlterReturnedDurableIdentity)]
    [InlineData(CheckpointFault.AlterWorkflowAccounting)]
    [InlineData(CheckpointFault.AlterReturnedOutboxFacts)]
    [InlineData(CheckpointFault.OutboxReordered)]
    public async Task Fails_closed_when_a_public_checkpoint_contract_surface_drifts(CheckpointFault fault)
    {
        var attempt = () => new RuntimeCheckpointCommitWorkload().ExecuteAsync(new CheckpointAdapter(fault)).AsTask();
        if (fault == CheckpointFault.PagingIgnoresLimit)
            await Assert.ThrowsAsync<ArgumentException>(attempt);
        else if (fault == CheckpointFault.RootWriteLeaseUnavailable)
            await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseUnavailableException>(attempt);
        else
            await Assert.ThrowsAsync<InvalidOperationException>(attempt);
    }

    [Fact]
    public void Public_adapter_surface_contains_only_the_required_provider_neutral_contracts()
    {
        var types = new[] { typeof(IRuntimeCheckpointCommitWorkloadAdapter), typeof(RuntimeCheckpointCommitClients), typeof(RuntimeCheckpointCommitClient) };
        var surface = types.SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly)).Select(member => member.ToString()!).Append(typeof(IRuntimeCheckpointCommitWorkloadAdapter).ToString());

        Assert.DoesNotContain(surface, value =>
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Exposes_the_frozen_public_operation_phases_without_rebuilding_the_scenario_in_the_adapter()
    {
        var adapter = new CheckpointAdapter();
        var operations = await new RuntimeCheckpointCommitWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.Get(RuntimeCheckpointCommitWorkload.WorkloadId).OperationSequence,
            operations.Select(operation => operation.Id));
        Assert.All(operations, operation => Assert.NotNull(operation));

        foreach (var operation in operations)
        {
            await operation.PrepareInvocationAsync(0);
            await operation.InvokeAsync(0);
        }
    }

    private sealed class CheckpointAdapter(CheckpointFault fault = CheckpointFault.None) : IRuntimeCheckpointCommitWorkloadAdapter
    {
        private readonly CheckpointBacking _secondary = fault == CheckpointFault.DifferentInitialBacking ? new() : null!;
        public CheckpointBacking Shared { get; } = new();
        public List<CheckpointPublicStore> OpenedClients { get; } = [];

        public ValueTask<RuntimeCheckpointCommitClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open(Shared);
            var secondary = fault == CheckpointFault.AliasInitialClients ? primary : Open(fault == CheckpointFault.DifferentInitialBacking ? _secondary : Shared);
            if (fault == CheckpointFault.AliasedComponentInstances)
                return new(new RuntimeCheckpointCommitClients(primary.Client, new RuntimeCheckpointCommitClient(
                    primary.Client.Checkpoints, primary.Client.Ownership, primary.Client.Executables, primary.Client.WorkflowExecutions,
                    primary.Client.Activities, primary.Client.DurableValues, primary.Client.Outbox)));
            return new(new RuntimeCheckpointCommitClients(primary.Client, secondary.Client));
        }

        public ValueTask<RuntimeCheckpointCommitClient> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault == CheckpointFault.SameReopenedClient)
                return new(OpenedClients[0].Client);
            return new(Open(fault == CheckpointFault.FreshReopenedBacking ? new CheckpointBacking() : Shared).Client);
        }

        private CheckpointPublicStore Open(CheckpointBacking backing)
        {
            var client = new CheckpointPublicStore(backing, fault);
            OpenedClients.Add(client);
            return client;
        }
    }

    private sealed class CheckpointBacking
    {
        public object Gate { get; } = new();
        public Dictionary<string, WorkflowExecutable> Executables { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, WorkflowExecutionState> Workflows { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, ActivityExecutionState> Activities { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, DurableValueState> DurableValues { get; } = new(StringComparer.Ordinal);
        public List<RuntimePostCommitOutboxItem> Outbox { get; } = [];
        public Dictionary<string, RuntimeCheckpointCommitStoreResult> Markers { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> MarkerFingerprints { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, RuntimeExecutionLease> Leases { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> FenceCounters { get; } = new(StringComparer.Ordinal);
        public List<ActivityExecutionStatePageQuery> ActivityPageRequests { get; } = [];
        public List<DurableValueStatePageQuery> DurableValuePageRequests { get; } = [];
        public List<RuntimePostCommitOutboxQuery> OutboxRequests { get; } = [];
        public int HeartbeatCount { get; set; }
        public int RootWriteLeaseCount { get; set; }
    }

    private sealed class CheckpointPublicStore :
        IRuntimeCheckpointCommitStore, IRuntimeExecutionOwnershipService, IWorkflowExecutableStore,
        IWorkflowExecutionStateStore, IActivityExecutionStateStore, IDurableValueStateStore, IRuntimePostCommitOutboxStore
    {
        private readonly CheckpointBacking _backing;
        private readonly CheckpointFault _fault;
        public RuntimeCheckpointCommitClient Client { get; }

        public CheckpointPublicStore(CheckpointBacking backing, CheckpointFault fault)
        {
            _backing = backing;
            _fault = fault;
            Client = new RuntimeCheckpointCommitClient(this, this, this, this, this, this, this);
        }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(RuntimeCheckpointCommit commit, RuntimeCheckpointPersistenceDecision decision, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                if (_backing.Markers.TryGetValue(commit.CommitId, out var replay))
                {
                    var fingerprint = RuntimeCheckpointCommitFingerprint.Compute(commit);
                    if (_fault != CheckpointFault.AcceptConflictingReplay &&
                        (!_backing.MarkerFingerprints.TryGetValue(commit.CommitId, out var acceptedFingerprint) ||
                         !StringComparer.Ordinal.Equals(acceptedFingerprint, fingerprint)))
                        throw new RuntimeCheckpointReplayConflictException(commit.CommitId);
                    if (_fault == CheckpointFault.ReplayDuplicatesOutbox)
                        _backing.Outbox.AddRange(commit.StateChanges.PostCommitOutbox.Select(change => change.State));
                    return new(replay);
                }

                var current = _backing.Leases.GetValueOrDefault(commit.WorkflowExecutionId);
                var stale = commit.ExpectedFence is null || current is null || current.FencingToken != commit.ExpectedFence.FencingToken || current.LeaseId != commit.ExpectedFence.LeaseId;
                if (stale && _fault != CheckpointFault.StaleAccepted && _fault != CheckpointFault.IgnoreExpectedFence)
                {
                    if (_fault == CheckpointFault.StalePartialMutation)
                        _backing.Activities[commit.StateChanges.ActivityExecutions.First().StateId] = commit.StateChanges.ActivityExecutions.First().State;
                    if (_fault == CheckpointFault.StaleLeaksReplayMarker)
                    {
                        _backing.Markers[commit.CommitId] = Acknowledgement(commit);
                        _backing.MarkerFingerprints[commit.CommitId] = RuntimeCheckpointCommitFingerprint.Compute(commit);
                    }
                    throw new RuntimeStaleFencingTokenException(commit.WorkflowExecutionId, commit.ExpectedFence?.FencingToken ?? 0, current?.FencingToken ?? 0);
                }

                var artifactId = commit.StateChanges.WorkflowExecution?.State.PinnedExecutable.ArtifactId
                    ?? throw new InvalidOperationException("The checkpoint fake requires a workflow-execution root.");
                var rootLease = AcquireRootWriteLease(artifactId, $"checkpoint:{commit.CommitId}");
                if (rootLease is null)
                    throw new WorkflowExecutableRootWriteLeaseUnavailableException(artifactId, $"checkpoint:{commit.CommitId}");
                try
                {
                    Apply(commit);
                    var acknowledgement = Acknowledgement(commit);
                    _backing.Markers.Add(commit.CommitId, acknowledgement);
                    _backing.MarkerFingerprints.Add(commit.CommitId, RuntimeCheckpointCommitFingerprint.Compute(commit));
                    return new(acknowledgement);
                }
                finally
                {
                    ReleaseRootWriteLease(rootLease);
                }
            }
        }

        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                var token = _backing.FenceCounters.GetValueOrDefault(workflowExecutionId) + 1;
                _backing.FenceCounters[workflowExecutionId] = token;
                var now = DateTimeOffset.UtcNow;
                var acquiredAt = _fault == CheckpointFault.ExpiredLease ? now.AddHours(-2) : now;
                var expiresAt = _fault == CheckpointFault.ExpiredLease ? now.AddHours(-1) : now.AddHours(1);
                var lease = new RuntimeExecutionLease($"lease-{workflowExecutionId}-{token}", workflowExecutionId, "checkpoint-workload", acquiredAt, expiresAt, token);
                _backing.Leases[workflowExecutionId] = lease;
                return new(lease);
            }
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (_backing.Gate)
            {
                var current = _backing.Leases.GetValueOrDefault(lease.WorkflowExecutionId);
                if (_fault == CheckpointFault.HeartbeatRejected || current is null ||
                    current.LeaseId != lease.LeaseId || current.FencingToken != lease.FencingToken)
                    return new(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Stale, current?.FencingToken));
                if (current.IsExpired(DateTimeOffset.UtcNow))
                    return new(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Expired, current.FencingToken));

                _backing.HeartbeatCount++;
                _backing.Leases[lease.WorkflowExecutionId] = new RuntimeExecutionLease(
                    current.LeaseId,
                    current.WorkflowExecutionId,
                    current.OwnerId,
                    current.AcquiredAt,
                    DateTimeOffset.UtcNow.AddHours(1),
                    current.FencingToken);
                return new(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, current.FencingToken));
            }
        }
        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default)
        {
            var current = _backing.Leases.GetValueOrDefault(workflowExecutionId);
            if (current is not null && current.FencingToken != fencingToken)
                throw new RuntimeStaleFencingTokenException(workflowExecutionId, fencingToken, current.FencingToken);
            return ValueTask.CompletedTask;
        }

        public ValueTask SaveAsync(WorkflowExecutable executable, CancellationToken cancellationToken = default)
        {
            _backing.Executables.TryAdd(executable.Identity.ArtifactId, executable);
            return ValueTask.CompletedTask;
        }

        ValueTask<WorkflowExecutable?> IWorkflowExecutableStore.FindAsync(string artifactId, CancellationToken cancellationToken) =>
            new(_fault == CheckpointFault.ExecutableResponseOnly ? null : _backing.Executables.GetValueOrDefault(artifactId));
        ValueTask<bool> IWorkflowExecutableStore.DeleteAsync(string artifactId, CancellationToken cancellationToken) => new(_backing.Executables.Remove(artifactId));
        public ValueTask<WorkflowExecutableRootWriteLease?> TryAcquireRootWriteLeaseAsync(string artifactId, string leaseId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            new(AcquireRootWriteLease(artifactId, leaseId));
        public ValueTask<bool> RenewRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            new(_backing.Executables.ContainsKey(lease.ArtifactId));
        public ValueTask ReleaseRootWriteLeaseAsync(WorkflowExecutableRootWriteLease lease, CancellationToken cancellationToken = default)
        {
            ReleaseRootWriteLease(lease);
            return ValueTask.CompletedTask;
        }
        public ValueTask<WorkflowExecutableDeletionGuard?> TryBeginDeletionAsync(string artifactId, string operationId, DateTimeOffset expiresAt, DateTimeOffset now, CancellationToken cancellationToken = default) => new((WorkflowExecutableDeletionGuard?)null);
        public ValueTask<bool> CancelDeletionAsync(WorkflowExecutableDeletionGuard guard, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<bool> DeleteAsync(WorkflowExecutableDeletionGuard guard, DateTimeOffset now, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<RuntimeStorePage<WorkflowExecutable>> ListPageAsync(RuntimeStorePageRequest request, CancellationToken cancellationToken = default) => new(new RuntimeStorePage<WorkflowExecutable>(request, _backing.Executables.Values.OrderBy(value => value.Identity.ArtifactId, StringComparer.Ordinal).Take(request.Limit).ToArray()));

        public ValueTask<WorkflowExecutionState> SaveAsync(WorkflowExecutionState state, CancellationToken cancellationToken = default)
        {
            _backing.Workflows[state.WorkflowExecutionId] = state;
            return new(state);
        }

        ValueTask<WorkflowExecutionState?> IWorkflowExecutionStateStore.FindAsync(string workflowExecutionId, CancellationToken cancellationToken) => new(_backing.Workflows.GetValueOrDefault(workflowExecutionId));
        public ValueTask<IReadOnlyCollection<WorkflowExecutionState>> ListAsync(CancellationToken cancellationToken = default) => new((IReadOnlyCollection<WorkflowExecutionState>)_backing.Workflows.Values.ToArray());
        public ValueTask<WorkflowExecutionStatePage> QueryPageAsync(WorkflowExecutionStatePageQuery query, CancellationToken cancellationToken = default) => new(new WorkflowExecutionStatePage(_backing.Workflows.Values.Where(value => WorkflowExecutionStateHistory.Matches(value, query)).OrderBy(value => value, Comparer<WorkflowExecutionState>.Create(WorkflowExecutionStateHistory.Compare)).Take(query.PageSize).ToArray(), null, false, _backing.Workflows.Count));
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default) => new((IReadOnlyCollection<string>)_backing.Workflows.Values.Select(value => value.PinnedExecutable.ArtifactId).Distinct(StringComparer.Ordinal).ToArray());
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, CancellationToken cancellationToken = default) => new(_backing.Workflows.Remove(workflowExecutionId));

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) { _backing.Activities[state.Execution.ActivityExecutionId] = state; return new(state); }
        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) => new(_backing.Activities.GetValueOrDefault(activityExecutionId));
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(ActivityExecutionStatePageQuery query, CancellationToken cancellationToken = default)
        {
            _backing.ActivityPageRequests.Add(query);
            return new(Page(query, _backing.Activities.Values.Where(value => value.Execution.WorkflowExecutionId == query.WorkflowExecutionId).OrderBy(value => value.Execution.ActivityExecutionId, StringComparer.Ordinal).ToArray()));
        }
        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(ActivityExecutionStateParentPageQuery query, CancellationToken cancellationToken = default) => new(new RuntimeStorePage<ActivityExecutionState>(query, []));

        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default) { _backing.DurableValues[state.DurableValueId] = state; return new(state); }
        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default) => new(_backing.DurableValues.Remove(durableValueId));
        ValueTask<DurableValueState?> IDurableValueStateStore.FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken) => new(_backing.DurableValues.GetValueOrDefault(durableValueId));
        public ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(DurableValueStatePageQuery query, CancellationToken cancellationToken = default)
        {
            _backing.DurableValuePageRequests.Add(query);
            return new(Page(query, _backing.DurableValues.Values.Where(value => value.WorkflowExecutionId == query.WorkflowExecutionId).OrderBy(value => value.DurableValueId, StringComparer.Ordinal).ToArray()));
        }

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(RuntimePostCommitOutboxQuery query, CancellationToken cancellationToken = default)
        {
            _backing.OutboxRequests.Add(query);
            var items = _backing.Outbox.Where(item => item.Intent.WorkflowExecutionId == query.WorkflowExecutionId).OrderBy(item => item.OutboxItemId, StringComparer.Ordinal).Take(query.Limit).ToArray();
            return new((IReadOnlyCollection<RuntimePostCommitOutboxItem>)(_fault switch
            {
                CheckpointFault.DropOutbox => items.Skip(1).ToArray(),
                CheckpointFault.AlterReturnedOutboxFacts => items.Select(Tamper).ToArray(),
                CheckpointFault.OutboxReordered => items.Reverse().ToArray(),
                _ => items
            }));
        }
        public ValueTask RecordDeliveryResultAsync(RuntimePostCommitOutboxDeliveryResult result, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        private WorkflowExecutableRootWriteLease? AcquireRootWriteLease(string artifactId, string leaseId)
        {
            if (_fault == CheckpointFault.RootWriteLeaseUnavailable || !_backing.Executables.ContainsKey(artifactId))
                return null;

            _backing.RootWriteLeaseCount++;
            return new WorkflowExecutableRootWriteLease(artifactId, leaseId, $"root-write:{leaseId}");
        }

        private static void ReleaseRootWriteLease(WorkflowExecutableRootWriteLease lease)
        {
            ArgumentNullException.ThrowIfNull(lease);
        }

        private void Apply(RuntimeCheckpointCommit commit)
        {
            var workflow = commit.StateChanges.WorkflowExecution!.State;
            if (_fault != CheckpointFault.DropWorkflow)
                _backing.Workflows[workflow.WorkflowExecutionId] = _fault == CheckpointFault.AlterWorkflowAccounting
                    ? workflow with { SystemMetadata = new Dictionary<string, string> { ["checkpoint"] = "tampered" } }
                    : workflow;
            foreach (var activity in commit.StateChanges.ActivityExecutions)
                if (_fault != CheckpointFault.DropActivity)
                {
                    var state = _fault == CheckpointFault.AlterReturnedPayload
                        ? activity.State with { Metadata = new Dictionary<string, string> { ["payload"] = "tampered" } }
                        : activity.State;
                    if (_fault == CheckpointFault.AlterReturnedActivityIdentity)
                        state = state with { Execution = state.Execution with { ActivityExecutionId = $"tampered-{state.Execution.ActivityExecutionId}" } };
                    _backing.Activities[activity.StateId] = state;
                }
            foreach (var value in commit.StateChanges.DurableValues)
                if (_fault != CheckpointFault.DropDurableValue)
                    _backing.DurableValues[value.StateId] = _fault == CheckpointFault.AlterReturnedDurableIdentity ? Tamper(value.State) : value.State;
            if (_fault != CheckpointFault.DropOutbox)
                _backing.Outbox.AddRange(commit.StateChanges.PostCommitOutbox.Select(change => change.State));
        }

        private RuntimeCheckpointCommitStoreResult Acknowledgement(RuntimeCheckpointCommit commit) => new(
            _fault switch
            {
                CheckpointFault.AcknowledgementDropsWork => [],
                CheckpointFault.AcknowledgementBindsWrongCommit => commit.StateChanges.PostCommitOutbox.Select(change => $"synthetic-{change.StateId}").ToArray(),
                _ => commit.StateChanges.PostCommitOutbox.Select(change => change.StateId).ToArray()
            });

        private RuntimeStorePage<T> Page<T>(RuntimeStorePageRequest query, IReadOnlyList<T> source)
        {
            var offset = query.ContinuationToken is null ? 0 : int.Parse(query.ContinuationToken, System.Globalization.CultureInfo.InvariantCulture);
            if (_fault == CheckpointFault.PagingIgnoreContinuation)
                offset = 0;
            var items = source.Skip(offset).Take(query.Limit).ToArray();
            if (_fault == CheckpointFault.PagingIgnoresLimit && offset == 0)
                items = source.ToArray();
            if (_fault == CheckpointFault.PagingDrop && offset == 0)
                items = items.Skip(1).ToArray();
            if (_fault == CheckpointFault.PagingDuplicate && offset > 0 && source.Count > 0)
                items = [source[0], .. items.Take(Math.Max(0, query.Limit - 1))];
            if (_fault == CheckpointFault.PagingReorder)
                Array.Reverse(items);
            if (_fault == CheckpointFault.PagingFabricatedMember && offset > 0 && source.Count > 0)
                items = [Fabricate(source[0]), .. items.Take(Math.Max(0, query.Limit - 1))];
            var nextOffset = _fault == CheckpointFault.PagingIgnoreContinuation && query.ContinuationToken is not null
                ? int.Parse(query.ContinuationToken, System.Globalization.CultureInfo.InvariantCulture) + query.Limit
                : offset + query.Limit;
            var next = nextOffset < source.Count ? nextOffset.ToString(System.Globalization.CultureInfo.InvariantCulture) : null;
            return new RuntimeStorePage<T>(query, items, next);
        }

        private static DurableValueState Tamper(DurableValueState state) => new(
            $"tampered-{state.DurableValueId}", state.WorkflowExecutionId, state.ValueId, state.Type, state.Lifecycle, state.Storage,
            state.InlineValue, state.ExternalReference, state.SourceActivityExecutionId, state.CapturedAt, state.Metadata);

        private static T Fabricate<T>(T item) =>
            item switch
            {
                ActivityExecutionState activity => (T)(object)(activity with
                {
                    Execution = activity.Execution with { ActivityExecutionId = $"fabricated-{activity.Execution.ActivityExecutionId}" }
                }),
                DurableValueState value => (T)(object)Tamper(value),
                _ => throw new InvalidOperationException($"The checkpoint fake cannot fabricate a {typeof(T).Name} page member.")
            };

        private static RuntimePostCommitOutboxItem Tamper(RuntimePostCommitOutboxItem item)
        {
            using var document = JsonDocument.Parse("{\"tampered\":true}");
            var intent = new RuntimePostCommitIntent(item.Intent.IntentId, item.Intent.WorkflowExecutionId, item.Intent.Kind, item.Intent.RecordedAt,
                item.Intent.ActivityExecutionId, item.Intent.IdempotencyKey, document.RootElement.Clone(), item.Intent.Metadata);
            return new RuntimePostCommitOutboxItem(item.OutboxItemId, intent, item.Status, item.RecordedAt, item.AvailableAt!.Value.AddSeconds(1),
                item.RetryPolicy, item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt, item.LastFailureMessage,
                item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter);
        }
    }

    public enum CheckpointFault
    {
        None, AliasInitialClients, AliasedComponentInstances, DifferentInitialBacking, SameReopenedClient, FreshReopenedBacking, ExecutableResponseOnly,
        RootWriteLeaseUnavailable,
        AcknowledgementDropsWork, AcknowledgementBindsWrongCommit, DropWorkflow, DropActivity, DropDurableValue, DropOutbox, PagingDrop, PagingDuplicate, PagingReorder,
        PagingIgnoreContinuation, PagingIgnoresLimit, ReplayDuplicatesOutbox, AcceptConflictingReplay, StaleAccepted, StalePartialMutation,
        StaleLeaksReplayMarker, IgnoreExpectedFence, ExpiredLease, HeartbeatRejected, AlterReturnedPayload, AlterReturnedActivityIdentity,
        AlterReturnedDurableIdentity, AlterWorkflowAccounting, PagingFabricatedMember, AlterReturnedOutboxFacts, OutboxReordered
    }
}
