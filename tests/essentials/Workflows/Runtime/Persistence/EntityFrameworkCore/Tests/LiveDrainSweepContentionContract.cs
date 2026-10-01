using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>Where a resumption sweep lands relative to a live drain's read of its own continuation.</summary>
public enum LiveDrainSweepTiming
{
    /// <summary>The sweep claims and delivers the continuation before the drain reads its deliverable items.</summary>
    BeforeTheDrainReads,

    /// <summary>The sweep claims and delivers it after that read and before the drain records the delivery.</summary>
    BetweenTheDrainsReadAndRecord
}

/// <summary>
/// #2225: another deliverer takes a live drain's own continuation between the drain's commit and its delivery step.
/// </summary>
/// <remarks>
/// An Event activity suspends in one checkpoint (activity <c>Suspended/TriggerWaiting</c>, plus a durable
/// <c>CreateBookmark</c> continuation) and gets its bookmark from that continuation's own checkpoint. A start returns
/// once its drain has run that continuation. When another deliverer, the resumption sweep in production, took the
/// continuation first, the drain saw nothing to deliver, reported quiescence, and the caller got Accepted with no
/// bookmark: the failure the fixture-host evidence tests hit in CI. Each scenario lets another deliverer act at the
/// drain's read of that continuation, in its own scope and execution context as the sweep's timer would. Written once
/// so the in-memory stores, SQLite and PostgreSQL are held to the same outcome.
/// </remarks>
internal static class LiveDrainSweepContentionContract
{
    private const string ActivityExecutionId = "actexec-event";
    private const string RecoverySigningKey = "ef-runtime-contention-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-contention-hierarchy-signing-key-32";
    private const string WorkflowExecutionId = WorkflowExecutionHarness.WorkflowExecutionId;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    /// <summary>The runtime's default in-memory stores.</summary>
    public static void InMemory(IServiceCollection services)
    {
    }

    /// <summary>The EF Core runtime stores on one provider, migrated by the module migrator when the node starts.</summary>
    public static Action<IServiceCollection> EntityFramework(string provider, string connectionString) =>
        services => services
            .AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
            {
                Provider = provider,
                ConnectionString = connectionString,
                RecoveryContinuationSigningKey = RecoverySigningKey,
                HierarchyCursorSigningKey = HierarchySigningKey
            })
            .AddEfModuleMigrations<RuntimeDbContext>(provider);

    /// <summary>
    /// The real resumption sweep claims and delivers the drain's continuation at the drain's read. The drain drains the
    /// work the sweep queued instead of reporting quiescence, so the start returns with its bookmark.
    /// </summary>
    public static async Task ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(
        Action<IServiceCollection> configureStore,
        LiveDrainSweepTiming timing)
    {
        var sweep = new SweepAtDrainRead(timing);
        await using var harness = await StartAsync(configureStore, sweep);

        // Throws "Scheduler did not drain to completion" when the drain stopped with the CreateBookmark work undrained.
        await harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention"));

        var result = Assert.Single(sweep.Results);
        Assert.Equal(0, result.OutboxFailedCount);
        Assert.Contains(sweep.Claimed, IsCreateBookmark);
        await AssertSuspendedWithBookmarkAsync(harness);
    }

    /// <summary>
    /// Another deliverer claims the continuation before the drain reads it and finishes delivering it only once the drain
    /// is waiting for it. The drain waits, then drains the work that deliverer queued.
    /// </summary>
    public static async Task ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync(Action<IServiceCollection> configureStore)
    {
        var other = new ClaimAtDrainRead("other-deliverer", TimeSpan.FromMinutes(1), deliverOnceTheDrainWaits: true);
        await using var harness = await StartAsync(configureStore, other);

        await harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention"));

        await other.DeliveredAsync();
        var item = await FindContinuationAsync(harness, other.ClaimedOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, item.Status);
        Assert.Equal(1, item.DeliveryFencingToken);
        await AssertSuspendedWithBookmarkAsync(harness);
    }

    /// <summary>
    /// The deliverer that claimed the continuation dies without delivering it. Its claim lapses, the drain claims the item
    /// itself and delivers it, and the start still returns with its bookmark, well inside the drain's wait limit.
    /// </summary>
    public static async Task ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync(Action<IServiceCollection> configureStore)
    {
        var dead = new ClaimAtDrainRead("dead-deliverer", TimeSpan.FromMilliseconds(500), deliverOnceTheDrainWaits: false);
        await using var harness = await StartAsync(configureStore, dead, waitLimit: TimeSpan.FromSeconds(20));

        await harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention"));

        var item = await FindContinuationAsync(harness, dead.ClaimedOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, item.Status);
        Assert.Equal(2, item.DeliveryFencingToken);
        await AssertSuspendedWithBookmarkAsync(harness);
    }

    /// <summary>
    /// The deliverer holding the continuation neither delivers it nor lets its claim lapse within the drain's wait limit.
    /// The drain gives up without reporting quiescence: the start is answered AcceptedButFaulted, not Accepted, and the
    /// item stays with that deliverer and, after it, the sweep.
    /// </summary>
    public static async Task ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync(Action<IServiceCollection> configureStore)
    {
        var stuck = new ClaimAtDrainRead("stuck-deliverer", TimeSpan.FromMinutes(10), deliverOnceTheDrainWaits: false);
        await using var harness = await StartAsync(configureStore, stuck, waitLimit: TimeSpan.FromMilliseconds(300));

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention")));

        Assert.Contains(nameof(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted), error.Message);
        var item = await FindContinuationAsync(harness, stuck.ClaimedOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, item.Status);
        Assert.Equal("stuck-deliverer", item.DeliveringOwnerId);
        await using var scope = harness.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(harness.ExecutionId));
    }

    private static async Task<WorkflowExecutionHarness> StartAsync(
        Action<IServiceCollection> configureStore,
        DrainReadInterceptor interceptor,
        TimeSpan? waitLimit = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                configureStore(services);
                services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                if (waitLimit is { } limit)
                    services.Replace(ServiceDescriptor.Singleton(new WorkflowDrainOrchestratorOptions(continuationClaimWaitLimit: limit)));
                OutboxStoreRegistration.Decorate(services, inner => new InterceptingOutboxStore(inner, interceptor));
            })
            .Build(ActivityExecutionId);
        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        interceptor.Services = harness.Services;
        return harness;
    }

    private static async Task AssertSuspendedWithBookmarkAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var bookmark = Assert.Single(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(harness.ExecutionId));
        var activity = Assert.Single(await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(harness.ExecutionId));
        Assert.Equal(ActivityExecutionStatus.Suspended, activity.Status);
        Assert.Equal(BookmarkSuspension.SuspendedSubStatus, activity.SubStatus);
        Assert.Contains(bookmark.BookmarkId, activity.BookmarkIds);
    }

    private static async Task<RuntimePostCommitOutboxItem> FindContinuationAsync(WorkflowExecutionHarness harness, string outboxItemId)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return Assert.IsType<RuntimePostCommitOutboxItem>(await scope.ServiceProvider
            .GetRequiredService<IPostCommitOutboxLookupStore>()
            .FindAsync(outboxItemId));
    }

    private static bool IsCreateBookmark(RuntimePostCommitOutboxItem item) =>
        item.Intent.WorkflowExecutionId == WorkflowExecutionId &&
        item.Intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
        item.Intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>()?.CommandKind == WorkflowExecutionCommandKind.CreateBookmark;

    /// <summary>
    /// Runs another deliverer's step where the pump's timer would run it: in its own scope, and with none of the drain's
    /// ambient state (no live-drain delivery scope, no ownership context).
    /// </summary>
    private static async Task<T> RunAsAnotherDelivererAsync<T>(IServiceProvider services, Func<IServiceProvider, Task<T>> step)
    {
        Task<T> running;
        using (ExecutionContext.SuppressFlow())
        {
            running = Task.Run(async () =>
            {
                await using var scope = services.CreateAsyncScope();
                return await step(scope.ServiceProvider);
            });
        }

        return await running.WaitAsync(Patience);
    }

    /// <summary>Acts once, the first time the drain reads its own CreateBookmark continuation as deliverable.</summary>
    private abstract class DrainReadInterceptor
    {
        private int _fired;

        public IServiceProvider Services { get; set; } = null!;

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ReadAsync(
            RuntimePostCommitOutboxQuery query,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read)
        {
            var items = await read();
            if (query.WorkflowExecutionId != WorkflowExecutionId || !items.Any(IsCreateBookmark) ||
                Interlocked.Exchange(ref _fired, 1) == 1)
                return items;

            return await OnDrainReadAsync(items.First(IsCreateBookmark), items, read);
        }

        protected abstract ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> OnDrainReadAsync(
            RuntimePostCommitOutboxItem continuation,
            IReadOnlyCollection<RuntimePostCommitOutboxItem> items,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read);

        public virtual void ObserveClaims(RuntimePostCommitOutboxClaimRequest request, IEnumerable<RuntimePostCommitOutboxClaim> claims)
        {
        }

        public virtual void ObserveClaimed(IReadOnlyCollection<RuntimePostCommitOutboxItem> claimed)
        {
        }
    }

    /// <summary>Runs one real resumption sweep at the drain's read.</summary>
    private sealed class SweepAtDrainRead(LiveDrainSweepTiming timing) : DrainReadInterceptor
    {
        public List<RuntimeResumptionSweepResult> Results { get; } = [];
        public List<RuntimePostCommitOutboxItem> Claimed { get; } = [];

        protected override async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> OnDrainReadAsync(
            RuntimePostCommitOutboxItem continuation,
            IReadOnlyCollection<RuntimePostCommitOutboxItem> items,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read)
        {
            // The drain's own execution is excluded from re-drive only so this test does not wait on the mailbox the drain
            // holds; the sweep's outbox step, which is what takes the continuation, is unaffected.
            Results.Add(await RunAsAnotherDelivererAsync(Services, services =>
                services.GetRequiredService<IRuntimeResumptionService>().SweepAsync(new RuntimeResumptionSweepRequest(
                    excludedWorkflowExecutionIds: new HashSet<string>(StringComparer.Ordinal) { WorkflowExecutionId })).AsTask()));
            return timing == LiveDrainSweepTiming.BeforeTheDrainReads ? await read() : items;
        }

        public override void ObserveClaims(RuntimePostCommitOutboxClaimRequest request, IEnumerable<RuntimePostCommitOutboxClaim> claims)
        {
            if (request.WorkflowExecutionId is null)
                Claimed.AddRange(claims.Select(claim => claim.Item));
        }
    }

    /// <summary>
    /// Claims the drain's continuation as another deliverer before the drain reads it. That deliverer delivers it once
    /// the drain lists it as claimed, or never, as one that died or got stuck would.
    /// </summary>
    private sealed class ClaimAtDrainRead(string ownerId, TimeSpan visibilityTimeout, bool deliverOnceTheDrainWaits)
        : DrainReadInterceptor
    {
        private RuntimePostCommitOutboxClaim? _claim;
        private Task? _delivery;

        public string ClaimedOutboxItemId => _claim?.OutboxItemId ?? throw new InvalidOperationException("Nothing was claimed.");

        public Task DeliveredAsync() =>
            (_delivery ?? throw new InvalidOperationException("The drain never waited for the claim.")).WaitAsync(Patience);

        protected override async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> OnDrainReadAsync(
            RuntimePostCommitOutboxItem continuation,
            IReadOnlyCollection<RuntimePostCommitOutboxItem> items,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read)
        {
            _claim = Assert.Single(await RunAsAnotherDelivererAsync(Services, services =>
                ClaimStore(services).ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                    ownerId,
                    services.GetRequiredService<TimeProvider>().GetUtcNow(),
                    visibilityTimeout,
                    limit: 1,
                    workflowExecutionId: WorkflowExecutionId,
                    intentKind: RuntimePostCommitIntentKinds.EnqueueSchedulerWork)).AsTask()));
            Assert.Equal(continuation.OutboxItemId, _claim.OutboxItemId);
            return await read();
        }

        // Delivers concurrently with the drain's wait, the way a sweep working through its batch would.
        public override void ObserveClaimed(IReadOnlyCollection<RuntimePostCommitOutboxItem> claimed)
        {
            if (deliverOnceTheDrainWaits && _claim is { } claim && _delivery is null &&
                claimed.Any(item => item.OutboxItemId == claim.OutboxItemId))
                _delivery = RunAsAnotherDelivererAsync(Services, async services =>
                {
                    await services.GetRequiredService<IRuntimePostCommitIntentDispatcher>().DispatchAsync(claim.Item.Intent);
                    return await ((IRuntimePostCommitOutboxClaimCompletionStore)services.GetRequiredService<IRuntimePostCommitOutboxStore>())
                        .CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                            claim,
                            new RuntimePostCommitOutboxDeliveryResult(
                                claim.OutboxItemId,
                                RuntimePostCommitOutboxStatus.Delivered,
                                services.GetRequiredService<TimeProvider>().GetUtcNow())));
                });
        }

        private static IRuntimePostCommitOutboxClaimStore ClaimStore(IServiceProvider services) =>
            (IRuntimePostCommitOutboxClaimStore)services.GetRequiredService<IRuntimePostCommitOutboxStore>();
    }

    private sealed class InterceptingOutboxStore(IRuntimePostCommitOutboxStore inner, DrainReadInterceptor interceptor) :
        IRuntimePostCommitOutboxStore,
        IRuntimePostCommitOutboxClaimStore,
        IRuntimePostCommitOutboxClaimCompletionStore,
        IPostCommitOutboxLookupStore
    {
        private IRuntimePostCommitOutboxClaimStore Claims => (IRuntimePostCommitOutboxClaimStore)inner;

        public ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            interceptor.ReadAsync(query, () => inner.GetDeliverableAsync(query, cancellationToken));

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> RecordDeliveryResultAsync(
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            inner.RecordDeliveryResultAsync(result, cancellationToken);

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default)
        {
            var claims = await Claims.ClaimAsync(request, cancellationToken);
            interceptor.ObserveClaims(request, claims);
            return claims;
        }

        public ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(
            RuntimePostCommitOutboxClaim claim,
            DateTimeOffset now,
            TimeSpan visibilityTimeout,
            CancellationToken cancellationToken = default) =>
            Claims.RenewClaimAsync(claim, now, visibilityTimeout, cancellationToken);

        public ValueTask RecordDeliveryResultAsync(
            RuntimePostCommitOutboxClaim claim,
            RuntimePostCommitOutboxDeliveryResult result,
            CancellationToken cancellationToken = default) =>
            Claims.RecordDeliveryResultAsync(claim, result, cancellationToken);

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
            RuntimePostCommitOutboxClaimedQuery query,
            CancellationToken cancellationToken = default)
        {
            var claimed = await Claims.ListClaimedAsync(query, cancellationToken);
            interceptor.ObserveClaimed(claimed);
            return claimed;
        }

        public ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion,
            CancellationToken cancellationToken = default) =>
            ((IRuntimePostCommitOutboxClaimCompletionStore)inner).CompleteClaimAsync(completion, cancellationToken);

        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(
            string outboxItemId,
            CancellationToken cancellationToken = default) =>
            ((IPostCommitOutboxLookupStore)inner).FindAsync(outboxItemId, cancellationToken);
    }
}
