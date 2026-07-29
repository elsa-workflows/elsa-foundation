using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Distributed.Contracts;
using Elsa.Workflows.Runtime.Distributed.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class DistributedPlacementTakeoverWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_exact_catalog_golden_through_independent_shared_backing_clients()
    {
        var adapter = new AtomicPlacementAdapter();
        var result = await new DistributedPlacementTakeoverWorkload().ExecuteAsync(adapter);

        Assert.Equal(DistributedPlacementTakeoverWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(DistributedPlacementTakeoverWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(DistributedPlacementTakeoverWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal("worker-beta", result.ObservableResults["currentOwner"]);
        Assert.True(new long[] { 1, 2, 3 }.SequenceEqual(Assert.IsType<long[]>(result.ObservableResults["placementTokenSequence"])));
        Assert.Equal(true, result.ObservableResults["reopenedOwnerMatched"]);
        Assert.Equal(true, result.ObservableResults["staleReleaseRejected"]);
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.Get(DistributedPlacementTakeoverWorkload.WorkloadId)
                .CreateExpectedObservations()["takeoverCandidateIdentityDigest"],
            result.ObservableResults["takeoverCandidateIdentityDigest"]);
        Assert.Equal(DistributedPlacementTakeoverWorkload.ActivePlacements + 5, adapter.Shared.TryClaimCalls);
        Assert.Equal(4, adapter.Shared.ListOwnedCalls);
        Assert.Equal(
            DistributedPlacementTakeoverWorkload.ExecutionCount - DistributedPlacementTakeoverWorkload.ActivePlacements + 4,
            adapter.Shared.FindCalls);
        Assert.Equal(3, adapter.OpenedClients.Count);
        Assert.Equal(3, adapter.OpenedClients.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public async Task Performs_the_frozen_public_operation_order()
    {
        var result = await new DistributedPlacementTakeoverWorkload().ExecuteAsync(new AtomicPlacementAdapter());

        Assert.Equal(
            [
                "seed-placement-leases",
                "claim-current-placement",
                "renew-current-placement",
                "advance-past-expiry",
                "take-over-expired-placement",
                "attempt-stale-release",
                "reopen-and-read-current-placement"
            ],
            result.ObservableOperations);
    }

    [Theory]
    [InlineData(PlacementAdapterFault.AliasInitialClients)]
    [InlineData(PlacementAdapterFault.SeparateInitialBacking)]
    [InlineData(PlacementAdapterFault.ReopenSameClient)]
    [InlineData(PlacementAdapterFault.ReopenSeparateBacking)]
    public async Task Rejects_clients_that_are_not_independent_or_visible_through_a_reopened_client(PlacementAdapterFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DistributedPlacementTakeoverWorkload().ExecuteAsync(new AtomicPlacementAdapter(fault)).AsTask());
    }

    [Theory]
    [InlineData(PlacementAdapterFault.WrongClaimToken)]
    [InlineData(PlacementAdapterFault.WrongFindLeaseIdentity)]
    [InlineData(PlacementAdapterFault.WrongFindLeaseExpiry)]
    [InlineData(PlacementAdapterFault.ReverseOwnedList)]
    [InlineData(PlacementAdapterFault.IgnoreOwnedListTake)]
    [InlineData(PlacementAdapterFault.IgnoreOwnedListOwner)]
    [InlineData(PlacementAdapterFault.IgnoreOwnedListExpiry)]
    [InlineData(PlacementAdapterFault.DropNonCandidatePlacement)]
    [InlineData(PlacementAdapterFault.NonAtomicDualGrant)]
    [InlineData(PlacementAdapterFault.CrossClaimantGrant)]
    [InlineData(PlacementAdapterFault.StaleReleaseClearsWinner)]
    public async Task Fails_closed_when_public_store_outcomes_drift(PlacementAdapterFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new DistributedPlacementTakeoverWorkload().ExecuteAsync(new AtomicPlacementAdapter(fault)).AsTask());
    }

    [Fact]
    public void Public_adapter_surface_contains_no_provider_or_timing_inputs()
    {
        var types = new[]
        {
            typeof(IDistributedPlacementTakeoverWorkloadAdapter),
            typeof(DistributedPlacementTakeoverClients)
        };
        var publicSurface = types
            .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IDistributedPlacementTakeoverWorkloadAdapter).ToString());

        Assert.DoesNotContain(publicSurface, value =>
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Matrix", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Manifest", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Physical", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class AtomicPlacementAdapter(PlacementAdapterFault fault = PlacementAdapterFault.None) : IDistributedPlacementTakeoverWorkloadAdapter
    {
        private readonly PlacementBacking _secondaryBacking = fault == PlacementAdapterFault.SeparateInitialBacking ? new() : null!;
        public PlacementBacking Shared { get; } = new();
        public List<AtomicPlacementStore> OpenedClients { get; } = [];

        public ValueTask<DistributedPlacementTakeoverClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open(Shared);
            var secondary = fault == PlacementAdapterFault.AliasInitialClients ? primary : Open(fault == PlacementAdapterFault.SeparateInitialBacking ? _secondaryBacking : Shared);
            return new(new DistributedPlacementTakeoverClients(primary, secondary));
        }

        public ValueTask<IExecutionPlacementStore> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault == PlacementAdapterFault.ReopenSameClient)
                return new(OpenedClients[0]);
            return new(Open(fault == PlacementAdapterFault.ReopenSeparateBacking ? new PlacementBacking() : Shared));
        }

        private AtomicPlacementStore Open(PlacementBacking backing)
        {
            var client = new AtomicPlacementStore(backing, fault);
            OpenedClients.Add(client);
            return client;
        }
    }

    private sealed class PlacementBacking
    {
        public object Sync { get; } = new();
        public Dictionary<string, ExecutionPlacementLease> Leases { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, long> Tokens { get; } = new(StringComparer.Ordinal);
        public int TryClaimCalls { get; set; }
        public int FindCalls { get; set; }
        public int ListOwnedCalls { get; set; }
        public int NonAtomicReadyCount { get; set; }
        public TaskCompletionSource NonAtomicRelease { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class AtomicPlacementStore(PlacementBacking backing, PlacementAdapterFault fault) : IExecutionPlacementStore
    {
        public ValueTask<ExecutionPlacementLease?> FindAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                backing.FindCalls++;
                if (!backing.Leases.TryGetValue(workflowExecutionId, out var lease))
                    return new((ExecutionPlacementLease?)null);
                if (fault == PlacementAdapterFault.WrongFindLeaseIdentity)
                    return new(new ExecutionPlacementLease("unexpected-execution", lease.OwnerId, lease.PlacementToken, lease.AcquiredAt, lease.ExpiresAt));
                if (fault == PlacementAdapterFault.WrongFindLeaseExpiry)
                    return new(new ExecutionPlacementLease(lease.WorkflowExecutionId, lease.OwnerId, lease.PlacementToken, lease.AcquiredAt, lease.ExpiresAt.AddTicks(1)));
                return new(Copy(lease));
            }
        }

        public ValueTask<ExecutionPlacementClaimResult> TryClaimAsync(ExecutionPlacementClaim claim, DateTimeOffset now, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (fault == PlacementAdapterFault.NonAtomicDualGrant && IsExpiredContentionCandidate(claim.WorkflowExecutionId, now))
                return SimulateNonAtomicDualGrantAsync(claim, cancellationToken);

            lock (backing.Sync)
            {
                backing.TryClaimCalls++;
                if (!backing.Leases.TryGetValue(claim.WorkflowExecutionId, out var current))
                    return new(Result(ExecutionPlacementClaimOutcome.Granted, Write(claim)));
                if (current.IsExpired(now))
                    return new(Result(
                        ExecutionPlacementClaimOutcome.Granted,
                        fault == PlacementAdapterFault.CrossClaimantGrant && claim.WorkflowExecutionId == "placement-expired-0001"
                            ? WriteAsOtherContentionOwner(claim)
                            : Write(claim)));
                if (current.OwnerId == claim.OwnerId)
                {
                    if (fault == PlacementAdapterFault.CrossClaimantGrant &&
                        claim.WorkflowExecutionId == "placement-expired-0001" &&
                        current.PlacementToken == 2)
                        return new(Result(ExecutionPlacementClaimOutcome.Denied, Copy(current)));
                    return new(Result(ExecutionPlacementClaimOutcome.Renewed, Write(claim)));
                }
                return new(Result(ExecutionPlacementClaimOutcome.Denied, Copy(current)));
            }
        }

        public ValueTask ReleaseAsync(ExecutionPlacementLease lease, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                if (!backing.Leases.TryGetValue(lease.WorkflowExecutionId, out var current))
                    return ValueTask.CompletedTask;
                if (fault == PlacementAdapterFault.StaleReleaseClearsWinner ||
                    (current.OwnerId == lease.OwnerId && current.PlacementToken == lease.PlacementToken))
                    backing.Leases.Remove(lease.WorkflowExecutionId);
                return ValueTask.CompletedTask;
            }
        }

        public ValueTask<IReadOnlyList<ExecutionPlacementLease>> ListOwnedAsync(ExecutionPlacementLeaseListRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            lock (backing.Sync)
            {
                backing.ListOwnedCalls++;
                var leases = backing.Leases.Values
                    .Where(lease =>
                        (fault == PlacementAdapterFault.IgnoreOwnedListOwner || lease.OwnerId == request.OwnerId) &&
                        (fault == PlacementAdapterFault.IgnoreOwnedListExpiry || !lease.IsExpired(request.Now)))
                    .OrderBy(lease => lease.ExpiresAt)
                    .ThenBy(lease => lease.WorkflowExecutionId, StringComparer.Ordinal)
                    .Take(fault == PlacementAdapterFault.IgnoreOwnedListTake ? int.MaxValue : request.Take)
                    .Select(Copy)
                    .ToArray();
                if (fault == PlacementAdapterFault.ReverseOwnedList)
                    Array.Reverse(leases);
                return new(leases);
            }
        }

        private ExecutionPlacementLease Write(ExecutionPlacementClaim claim)
        {
            var token = backing.Tokens.GetValueOrDefault(claim.WorkflowExecutionId) + 1;
            backing.Tokens[claim.WorkflowExecutionId] = token;
            var lease = new ExecutionPlacementLease(claim.WorkflowExecutionId, claim.OwnerId, token, claim.RequestedAt, claim.ExpiresAt);
            if (fault != PlacementAdapterFault.DropNonCandidatePlacement || !claim.WorkflowExecutionId.StartsWith("placement-live-", StringComparison.Ordinal))
                backing.Leases[claim.WorkflowExecutionId] = lease;
            return lease;
        }

        private ExecutionPlacementLease WriteAsOtherContentionOwner(ExecutionPlacementClaim claim)
        {
            var wrongOwner = claim.OwnerId == "worker-beta" ? "worker-gamma" : "worker-beta";
            return Write(new ExecutionPlacementClaim(
                claim.WorkflowExecutionId,
                wrongOwner,
                claim.RequestedAt,
                claim.ExpiresAt));
        }

        private bool IsExpiredContentionCandidate(string workflowExecutionId, DateTimeOffset now)
        {
            lock (backing.Sync)
                return backing.Leases.TryGetValue(workflowExecutionId, out var current) && current.IsExpired(now);
        }

        private async ValueTask<ExecutionPlacementClaimResult> SimulateNonAtomicDualGrantAsync(
            ExecutionPlacementClaim claim,
            CancellationToken cancellationToken)
        {
            lock (backing.Sync)
            {
                backing.TryClaimCalls++;
                if (++backing.NonAtomicReadyCount == DistributedPlacementTakeoverWorkload.ConcurrentClaimants)
                    backing.NonAtomicRelease.TrySetResult();
            }

            await backing.NonAtomicRelease.Task.WaitAsync(cancellationToken);
            lock (backing.Sync)
                return Result(ExecutionPlacementClaimOutcome.Granted, Write(claim));
        }

        private ExecutionPlacementClaimResult Result(ExecutionPlacementClaimOutcome outcome, ExecutionPlacementLease lease)
        {
            if (fault != PlacementAdapterFault.WrongClaimToken)
                return new(outcome, lease);
            return new(outcome, new ExecutionPlacementLease(lease.WorkflowExecutionId, lease.OwnerId, lease.PlacementToken + 1, lease.AcquiredAt, lease.ExpiresAt));
        }

        private static ExecutionPlacementLease Copy(ExecutionPlacementLease lease) =>
            new(lease.WorkflowExecutionId, lease.OwnerId, lease.PlacementToken, lease.AcquiredAt, lease.ExpiresAt);
    }
}

public enum PlacementAdapterFault
{
    None,
    AliasInitialClients,
    SeparateInitialBacking,
    ReopenSameClient,
    ReopenSeparateBacking,
    WrongClaimToken,
    WrongFindLeaseIdentity,
    WrongFindLeaseExpiry,
    ReverseOwnedList,
    IgnoreOwnedListTake,
    IgnoreOwnedListOwner,
    IgnoreOwnedListExpiry,
    DropNonCandidatePlacement,
    NonAtomicDualGrant,
    CrossClaimantGrant,
    StaleReleaseClearsWinner
}
