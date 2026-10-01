using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2225: a resumption sweep that runs while a live drain is between committing a continuation and delivering it.
/// </summary>
/// <remarks>
/// An Event activity suspends in one checkpoint (activity <c>Suspended/TriggerWaiting</c>, plus a durable
/// <c>CreateBookmark</c> continuation) and gets its bookmark from that continuation's own checkpoint. The live drain
/// delivers the continuation and drains it before the start command returns. The sweep used to claim it first, so the
/// drain saw nothing left to deliver, reported quiescence, and the caller got Accepted with no bookmark, which is the
/// failure the fixture-host evidence tests hit in CI. The sweep here is the real <see cref="IRuntimeResumptionService"/>,
/// run where the pump's timer would run it: in its own scope and execution context, at the drain's outbox read.
/// </remarks>
public abstract class LiveDrainSweepContentionContractTests : IAsyncDisposable
{
    private const string ActivityExecutionId = "actexec-event";
    private WorkflowExecutionHarness? _harness;

    public enum SweepTiming
    {
        /// <summary>The sweep's claim lands before the drain reads its deliverable continuations.</summary>
        BeforeTheDrainReads,

        /// <summary>The sweep's claim lands after that read and before the drain records the delivery.</summary>
        BetweenTheDrainsReadAndRecord
    }

    /// <summary>Replaces the default in-memory runtime stores with the store under test.</summary>
    protected abstract void ConfigureStore(IServiceCollection services);

    [Theory]
    [InlineData(SweepTiming.BeforeTheDrainReads)]
    [InlineData(SweepTiming.BetweenTheDrainsReadAndRecord)]
    public async Task A_sweep_during_the_drain_leaves_its_continuation_so_the_start_returns_with_the_bookmark(SweepTiming timing)
    {
        var sweep = new SweepAtDrainOutboxRead(WorkflowExecutionHarness.WorkflowExecutionId, timing);
        var harness = await StartHarnessAsync(sweep);

        // Throws "Scheduler did not drain to completion" when the drain stopped with the CreateBookmark work undrained.
        await harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention"));

        var result = Assert.Single(sweep.Results);
        Assert.Equal(0, result.OutboxFailedCount);
        Assert.DoesNotContain(sweep.Claimed, item =>
            item.Intent.WorkflowExecutionId == harness.ExecutionId &&
            item.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork);
        await using var scope = harness.Services.CreateAsyncScope();
        var bookmark = Assert.Single(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(harness.ExecutionId));
        var activity = Assert.Single(await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(harness.ExecutionId));
        Assert.Equal(ActivityExecutionStatus.Suspended, activity.Status);
        Assert.Equal(BookmarkSuspension.SuspendedSubStatus, activity.SubStatus);
        Assert.Contains(bookmark.BookmarkId, activity.BookmarkIds);
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_harness is not null)
            await _harness.DisposeAsync();
    }

    private async Task<WorkflowExecutionHarness> StartHarnessAsync(SweepAtDrainOutboxRead sweep)
    {
        _harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new WorkflowsRuntimeResumptionFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddLogging();
                ConfigureStore(services);
                OutboxStoreRegistration.Decorate(services, inner => new SweepTriggeringOutboxStore(inner, sweep));
            })
            .Build(ActivityExecutionId);
        foreach (var initializer in _harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        _harness.InitializeActivityTypes();
        sweep.Services = _harness.Services;
        return _harness;
    }

    /// <summary>Runs one real resumption sweep the first time the live drain reads its CreateBookmark continuation.</summary>
    private sealed class SweepAtDrainOutboxRead(string workflowExecutionId, SweepTiming timing)
    {
        private static readonly TimeSpan SweepTimeout = TimeSpan.FromSeconds(30);
        private int _fired;

        public IServiceProvider Services { get; set; } = null!;
        public List<RuntimeResumptionSweepResult> Results { get; } = [];
        public List<RuntimePostCommitOutboxItem> Claimed { get; } = [];

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ReadAsync(
            RuntimePostCommitOutboxQuery query,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read)
        {
            var items = await read();
            if (query.WorkflowExecutionId != workflowExecutionId || !items.Any(IsCreateBookmark) ||
                Interlocked.Exchange(ref _fired, 1) == 1)
                return items;

            Results.Add(await RunSweepAsync());
            return timing == SweepTiming.BeforeTheDrainReads ? await read() : items;
        }

        public void Observe(RuntimePostCommitOutboxClaimRequest request, IEnumerable<RuntimePostCommitOutboxClaim> claims)
        {
            if (request.WorkflowExecutionId is null)
                Claimed.AddRange(claims.Select(claim => claim.Item));
        }

        // The pump's timer thread carries none of the drain's ambient state: no live-drain delivery scope, no ownership
        // context. Suppressing the flow gives the sweep that same isolation. The drain's own execution is excluded from
        // re-drive only so this test does not wait on the mailbox the drain is holding; the outbox step is unaffected.
        private async Task<RuntimeResumptionSweepResult> RunSweepAsync()
        {
            Task<RuntimeResumptionSweepResult> sweep;
            using (ExecutionContext.SuppressFlow())
            {
                sweep = Task.Run(async () =>
                {
                    await using var scope = Services.CreateAsyncScope();
                    return await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>().SweepAsync(
                        new RuntimeResumptionSweepRequest(
                            excludedWorkflowExecutionIds: new HashSet<string>(StringComparer.Ordinal) { workflowExecutionId }));
                });
            }

            return await sweep.WaitAsync(SweepTimeout);
        }

        private static bool IsCreateBookmark(RuntimePostCommitOutboxItem item) =>
            item.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
            item.Intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>()?.CommandKind == WorkflowExecutionCommandKind.CreateBookmark;
    }

    private sealed class SweepTriggeringOutboxStore(IRuntimePostCommitOutboxStore inner, SweepAtDrainOutboxRead sweep) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IRuntimePostCommitOutboxClaimCompletionStore
    {
        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            sweep.ReadAsync(query, () => inner.GetDeliverableAsync(query, cancellationToken));

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default)
        {
            var claims = await ((IRuntimePostCommitOutboxClaimStore)inner).ClaimAsync(request, cancellationToken);
            sweep.Observe(request, claims);
            return claims;
        }

        public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
            RuntimePostCommitOutboxClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default) =>
            ((IRuntimePostCommitOutboxClaimStore)inner).RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            ((IRuntimePostCommitOutboxClaimStore)inner).RecordDeliveryResultAsync(claim, result, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default) =>
            ((IRuntimePostCommitOutboxClaimCompletionStore)inner).CompleteClaimAsync(completion, cancellationToken);
    }
}
