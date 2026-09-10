using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeDueTimerSelectionWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_frozen_due_timer_contract_and_literal_golden_digest()
    {
        var adapter = new DueTimerAdapter();
        var result = await new RuntimeDueTimerSelectionWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeDueTimerSelectionWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeDueTimerSelectionWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(
            ReproducibleWorkloadScenarioCatalog.Get(RuntimeDueTimerSelectionWorkload.WorkloadId).OperationSequence,
            result.ObservableOperations);
        Assert.Equal(3, adapter.Opened.Count);
        Assert.Equal(3, adapter.Opened.Distinct(ReferenceEqualityComparer.Instance).Count());
    }

    [Fact]
    public async Task Prepares_only_the_four_timed_operations_after_setup()
    {
        var adapter = new DueTimerAdapter();
        var operations = await new RuntimeDueTimerSelectionWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(
            [
                "list-bounded-due-timers",
                "advance-due-timer",
                "attempt-stale-advance",
                "reopen-and-read-due-state"
            ],
            operations.Select(operation => operation.Id));

        foreach (var operation in operations)
        {
            await operation.PrepareInvocationAsync(-1);
            await operation.InvokeAsync(-1);
            await operation.PrepareInvocationAsync(0);
            await operation.InvokeAsync(0);
        }
    }

    [Fact]
    public async Task Rejects_an_adapter_that_does_not_provide_atomic_claim_transitions()
    {
        var adapter = new DueTimerAdapter(supportsClaims: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeDueTimerSelectionWorkload().ExecuteAsync(adapter).AsTask());
    }

    [Fact]
    public async Task Rejects_aliased_initial_clients_and_reopened_clients()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeDueTimerSelectionWorkload().ExecuteAsync(new DueTimerAdapter(aliasInitialClients: true)).AsTask());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeDueTimerSelectionWorkload().ExecuteAsync(new DueTimerAdapter(aliasReopenedClient: true)).AsTask());
    }

    private sealed class DueTimerAdapter(
        bool supportsClaims = true,
        bool aliasInitialClients = false,
        bool aliasReopenedClient = false) : IDueTimerSelectionWorkloadAdapter
    {
        private readonly InMemoryDurableTimerStore shared = new();
        private readonly bool supportsClaims = supportsClaims;
        private readonly bool aliasInitialClients = aliasInitialClients;
        private readonly bool aliasReopenedClient = aliasReopenedClient;

        public List<IDurableTimerStore> Opened { get; } = [];

        public ValueTask<DueTimerSelectionClients> OpenIndependentClientsAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var primary = Open();
            var secondary = aliasInitialClients ? primary : Open();
            return new(new DueTimerSelectionClients(primary, secondary));
        }

        public ValueTask<IDurableTimerStore> ReopenClientAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return new(aliasReopenedClient ? Opened[0] : Open());
        }

        private IDurableTimerStore Open()
        {
            var client = new DueTimerClient(shared, supportsClaims);
            Opened.Add(client);
            return client;
        }
    }

    private sealed class DueTimerClient(InMemoryDurableTimerStore inner, bool supportsClaims) : IDurableTimerStore
    {
        public bool SupportsClaimTransitions => supportsClaims;

        public ValueTask<DurableTimer> SaveAsync(DurableTimer timer, CancellationToken cancellationToken = default) =>
            inner.SaveAsync(timer, cancellationToken);

        public ValueTask<IReadOnlyCollection<DurableTimer>> ListDueAsync(
            DateTimeOffset asOf,
            int limit,
            CancellationToken cancellationToken = default) =>
            inner.ListDueAsync(asOf, limit, cancellationToken);

        public ValueTask<DurableTimer?> FindAsync(
            string workflowExecutionId,
            string timerId,
            CancellationToken cancellationToken = default) =>
            inner.FindAsync(workflowExecutionId, timerId, cancellationToken);

        public ValueTask DeleteAsync(
            string workflowExecutionId,
            string timerId,
            CancellationToken cancellationToken = default) =>
            inner.DeleteAsync(workflowExecutionId, timerId, cancellationToken);

        public ValueTask<IReadOnlyCollection<RuntimeDurableTimerClaim>> ClaimDueAsync(
            RuntimeDurableTimerClaimRequest request,
            CancellationToken cancellationToken = default) =>
            inner.ClaimDueAsync(request, cancellationToken);

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> RenewClaimAsync(
            RuntimeDurableTimerClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default) =>
            inner.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> CompleteClaimAsync(
            RuntimeDurableTimerClaim claim,
            CancellationToken cancellationToken = default) =>
            inner.CompleteClaimAsync(claim, cancellationToken);

        public ValueTask<RuntimeDurableTimerClaimTransitionResult> ReleaseClaimAsync(
            RuntimeDurableTimerClaim claim,
            DateTimeOffset visibleAt,
            CancellationToken cancellationToken = default) =>
            inner.ReleaseClaimAsync(claim, visibleAt, cancellationToken);
    }
}
