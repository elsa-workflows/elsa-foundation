using System.Collections.Concurrent;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Tests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// #2225: another deliverer takes a live drain's own continuation between the drain's commit and its delivery step.
/// </summary>
/// <remarks>
/// An Event activity suspends in one checkpoint (activity <c>Suspended/TriggerWaiting</c>, plus a durable
/// <c>CreateBookmark</c> continuation) and gets its bookmark from that continuation's own checkpoint. A start returns
/// once its drain has run that continuation. When another deliverer, the resumption sweep in production, took the
/// continuation first, the drain saw nothing to deliver, reported quiescence, and the caller got Accepted with no
/// bookmark: the failure the fixture-host evidence tests hit in CI. Each scenario lets another deliverer act at the
/// drain's read of that continuation, in its own scope and execution context as the sweep's timer would. The retry
/// scenarios fail that continuation's delivery transiently instead, on the drain or on the sweep, under the retry policy
/// production registers for it. Written once, as a table of scenarios each store runs through <see cref="RunAsync"/>, so
/// the in-memory stores, SQLite and PostgreSQL are held to the same outcome and a new scenario is added in one place.
/// </remarks>
internal static class LiveDrainSweepContentionContract
{
    private const string ActivityExecutionId = "actexec-event";
    private const string RecoverySigningKey = "ef-runtime-contention-recovery-signing-key-32";
    private const string HierarchySigningKey = "ef-runtime-contention-hierarchy-signing-key-32";
    private const string WorkflowExecutionId = WorkflowExecutionHarness.WorkflowExecutionId;
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private static readonly Dictionary<string, Func<Action<IServiceCollection>, Task>> All = new()
    {
        ["real-sweep-before-the-drains-read"] = store =>
            ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(store, SweepTiming.BeforeTheDrainReads),
        ["real-sweep-between-the-drains-read-and-record"] = store =>
            ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(store, SweepTiming.BetweenTheDrainsReadAndRecord),
        ["drain-waits-for-another-deliverer-that-holds-its-continuation"] = ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync,
        ["drain-delivers-a-continuation-whose-other-claim-lapsed"] = ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync,
        ["drain-whose-continuation-stays-taken-does-not-report-quiescence"] = ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync,
        ["a-transient-continuation-failure-is-retried-and-delivered"] = ATransientContinuationFailureIsRetriedAndDeliveredAsync,
        ["drain-whose-continuation-another-deliverer-failed-transiently-reports-a-failed-delivery"] =
            ADrainWhoseContinuationAnotherDelivererFailedTransientlyReportsAFailedDeliveryAsync,
        ["a-continuation-whose-retries-are-exhausted-fails-for-good"] = AContinuationWhoseRetriesAreExhaustedFailsForGoodAsync
    };

    /// <summary>Where a resumption sweep lands relative to a live drain's read of its own continuation.</summary>
    private enum SweepTiming
    {
        /// <summary>The sweep claims and delivers the continuation before the drain reads its deliverable items.</summary>
        BeforeTheDrainReads,

        /// <summary>The sweep claims and delivers it after that read and before the drain records the delivery.</summary>
        BetweenTheDrainsReadAndRecord
    }

    public static TheoryData<string> Scenarios
    {
        get
        {
            var scenarios = new TheoryData<string>();
            foreach (var scenario in All.Keys)
                scenarios.Add(scenario);
            return scenarios;
        }
    }

    /// <summary>Runs one scenario on the store <paramref name="configureStore"/> selects.</summary>
    public static Task RunAsync(string scenario, Action<IServiceCollection> configureStore) => All[scenario](configureStore);

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
    private static async Task ARealSweepAtTheDrainsReadLeavesTheStartWithItsBookmarkAsync(
        Action<IServiceCollection> configureStore,
        SweepTiming timing)
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
    private static async Task ADrainWaitsForAnotherDelivererThatHoldsItsContinuationAsync(Action<IServiceCollection> configureStore)
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
    private static async Task ADrainDeliversAContinuationWhoseOtherClaimLapsedAsync(Action<IServiceCollection> configureStore)
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
    private static async Task ADrainWhoseContinuationStaysTakenDoesNotReportQuiescenceAsync(Action<IServiceCollection> configureStore)
    {
        var stuck = new ClaimAtDrainRead("stuck-deliverer", TimeSpan.FromMinutes(10), deliverOnceTheDrainWaits: false);
        await using var harness = await StartAsync(configureStore, stuck, waitLimit: TimeSpan.FromMilliseconds(300));

        await AssertStartAcceptedButFaultedAsync(harness);

        var item = await FindContinuationAsync(harness, stuck.ClaimedOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, item.Status);
        Assert.Equal("stuck-deliverer", item.DeliveringOwnerId);
        await AssertNoBookmarkAsync(harness);
    }

    /// <summary>
    /// The drain's own delivery of its continuation fails once, as an enqueue the queue's store refused would, so nothing
    /// was queued. The start answers AcceptedButFaulted, and the continuation stays FailedRetryable instead of being lost:
    /// once its retry delay has passed, the sweep delivers it and re-drives the execution, which reaches its bookmark.
    /// </summary>
    private static async Task ATransientContinuationFailureIsRetriedAndDeliveredAsync(Action<IServiceCollection> configureStore)
    {
        var flaky = new FlakyContinuation(failures: 1);
        var drain = new ObserveDrainRead();
        await using var harness = await StartAsync(configureStore, drain, flaky: flaky);
        var beforeTheFailure = flaky.Clock.GetUtcNow();

        await AssertStartAcceptedButFaultedAsync(harness);

        var failed = await FindContinuationAsync(harness, drain.ContinuationOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, failed.Status);
        Assert.Equal(1, failed.DeliveryAttemptCount);
        Assert.True(failed.AvailableAt >= beforeTheFailure + RetryPolicy.Delay, "The retry is not held back by its delay.");

        var retry = await flaky.RetryAsync(harness);

        Assert.Equal(1, retry.OutboxDeliveredCount);
        var delivered = await FindContinuationAsync(harness, drain.ContinuationOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, delivered.Status);
        Assert.Equal(2, delivered.DeliveryAttemptCount);
        await AssertSuspendedWithBookmarkAsync(harness);
    }

    /// <summary>
    /// The direction that looked like success: the sweep takes the drain's continuation at the drain's read and its attempt
    /// fails transiently, before the drain reads anything. Nothing is claimed, deliverable or queued, so a drain that missed
    /// the failure would report quiescence and answer Accepted with no bookmark. The failed item is listed, and the drain
    /// does not wait for its retry: it answers AcceptedButFaulted at once. The retry stays with the sweep, which delivers it
    /// after its delay, and the execution reaches its bookmark.
    /// </summary>
    private static async Task ADrainWhoseContinuationAnotherDelivererFailedTransientlyReportsAFailedDeliveryAsync(
        Action<IServiceCollection> configureStore)
    {
        var flaky = new FlakyContinuation(failures: 1);
        var sweep = new SweepAtDrainRead(SweepTiming.BeforeTheDrainReads);
        await using var harness = await StartAsync(configureStore, sweep, flaky: flaky);

        await AssertStartAcceptedButFaultedAsync(harness);

        Assert.Equal(1, Assert.Single(sweep.Results).OutboxFailedCount);
        Assert.Contains(sweep.Claimed, IsCreateBookmark);
        var failed = await FindContinuationAsync(harness, sweep.ContinuationOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedRetryable, failed.Status);
        await AssertNoBookmarkAsync(harness);

        Assert.Equal(1, (await flaky.RetryAsync(harness)).OutboxDeliveredCount);
        Assert.Equal(
            RuntimePostCommitOutboxStatus.Delivered,
            (await FindContinuationAsync(harness, sweep.ContinuationOutboxItemId)).Status);
        await AssertSuspendedWithBookmarkAsync(harness);
    }

    /// <summary>
    /// Every attempt fails: the drain's own, then one per sweep once each retry delay has passed. The last attempt the policy
    /// allows makes the continuation FailedFinal, logged as such, and nothing claims it again. Until then each attempt is
    /// logged as a scheduled retry, never as final.
    /// </summary>
    private static async Task AContinuationWhoseRetriesAreExhaustedFailsForGoodAsync(Action<IServiceCollection> configureStore)
    {
        var flaky = new FlakyContinuation(failures: RetryPolicy.MaxAttempts);
        var drain = new ObserveDrainRead();
        await using var harness = await StartAsync(configureStore, drain, flaky: flaky);

        await AssertStartAcceptedButFaultedAsync(harness);
        for (var attempt = 2; attempt <= RetryPolicy.MaxAttempts; attempt++)
            Assert.Equal(1, (await flaky.RetryAsync(harness)).OutboxFailedCount);

        var item = await FindContinuationAsync(harness, drain.ContinuationOutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, item.Status);
        Assert.Equal(RetryPolicy.MaxAttempts, item.DeliveryAttemptCount);
        Assert.False(string.IsNullOrWhiteSpace(item.LastFailureMessage));
        Assert.Equal(RetryPolicy.MaxAttempts, flaky.Log.Count(68101));
        Assert.Equal(RetryPolicy.MaxAttempts - 1, flaky.Log.Count(68102));
        Assert.Equal(1, flaky.Log.Count(68103));
        Assert.Equal(0, (await flaky.RetryAsync(harness)).OutboxAttemptedCount);
        await AssertNoBookmarkAsync(harness);
    }

    private static RuntimePostCommitRetryPolicy RetryPolicy => RuntimeSchedulerPostCommitIntentDispatcher.RetryPolicy;

    private static async Task<WorkflowExecutionHarness> StartAsync(
        Action<IServiceCollection> configureStore,
        DrainReadInterceptor interceptor,
        TimeSpan? waitLimit = null,
        FlakyContinuation? flaky = null)
    {
        var harness = WorkflowExecutionHarness.Create()
            .ConfigureServices(services =>
            {
                services.AddLogging();
                configureStore(services);
                services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
                if (waitLimit is { } limit)
                    services.Replace(ServiceDescriptor.Singleton(new WorkflowDrainOrchestratorOptions(continuationClaimWaitLimit: limit)));
                OutboxStoreRegistration.DecorateWithClaimsAndLookup(services, inner => new InterceptingOutboxStore(inner, interceptor));
                flaky?.Register(services);
            })
            .Build(ActivityExecutionId);
        foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
            await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        interceptor.Services = harness.Services;
        return harness;
    }

    // The harness throws when the start is not answered Accepted, and names the status it got.
    private static async Task AssertStartAcceptedButFaultedAsync(WorkflowExecutionHarness harness)
    {
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            harness.RunAsync(RuntimeEventExecutableTestFixture.Create("contention")));
        Assert.Contains(nameof(WorkflowExecutionCommandDispatchStatus.AcceptedButFaulted), error.Message);
    }

    private static async Task AssertNoBookmarkAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(harness.ExecutionId));
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

    private static bool IsCreateBookmark(RuntimePostCommitOutboxItem item) => IsCreateBookmark(item.Intent);

    private static bool IsCreateBookmark(RuntimePostCommitIntent intent) =>
        intent.WorkflowExecutionId == WorkflowExecutionId &&
        intent.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork &&
        intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>()?.CommandKind == WorkflowExecutionCommandKind.CreateBookmark;

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
        private string? _continuationOutboxItemId;

        public IServiceProvider Services { get; set; } = null!;

        /// <summary>The outbox item of the CreateBookmark continuation the drain read.</summary>
        public string ContinuationOutboxItemId =>
            _continuationOutboxItemId ?? throw new InvalidOperationException("The drain never read its continuation.");

        public async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ReadAsync(
            RuntimePostCommitOutboxQuery query,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read)
        {
            var items = await read();
            if (query.WorkflowExecutionId != WorkflowExecutionId || !items.Any(IsCreateBookmark) ||
                Interlocked.Exchange(ref _fired, 1) == 1)
                return items;

            var continuation = items.First(IsCreateBookmark);
            _continuationOutboxItemId = continuation.OutboxItemId;
            return await OnDrainReadAsync(continuation, items, read);
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

    /// <summary>Only notes the drain's continuation: no other deliverer acts at the drain's read.</summary>
    private sealed class ObserveDrainRead : DrainReadInterceptor
    {
        protected override ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> OnDrainReadAsync(
            RuntimePostCommitOutboxItem continuation,
            IReadOnlyCollection<RuntimePostCommitOutboxItem> items,
            Func<ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>>> read) => new(items);
    }

    /// <summary>
    /// Makes the next dispatches of the drain's CreateBookmark continuation, by any deliverer, fail the way a transient
    /// enqueue failure does: before anything is queued. Also the node's clock, so a case can move past a retry delay
    /// without waiting it out, and the outbox processor's log.
    /// </summary>
    private sealed class FlakyContinuation(int failures)
    {
        private int _failuresLeft = failures;

        public OffsetClock Clock { get; } = new();
        public EventLog Log { get; } = new();

        public void Register(IServiceCollection services)
        {
            services.AddSingleton<TimeProvider>(Clock);
            services.AddSingleton<ILogger<RuntimePostCommitOutboxProcessor>>(Log);
            services.Replace(ServiceDescriptor.Scoped<IRuntimePostCommitIntentDispatcher>(provider =>
                new FailingDispatcher(ActivatorUtilities.CreateInstance<RuntimePostCommitIntentDispatcher>(provider), this)));
        }

        /// <summary>Moves the clock past the continuation's retry delay and runs one sweep, as the next pump tick would.</summary>
        public Task<RuntimeResumptionSweepResult> RetryAsync(WorkflowExecutionHarness harness)
        {
            Clock.Advance(RetryPolicy.Delay!.Value);
            return harness.SweepAsync();
        }

        private bool TakeFailure() => Interlocked.Decrement(ref _failuresLeft) >= 0;

        private sealed class FailingDispatcher(IRuntimePostCommitIntentDispatcher inner, FlakyContinuation flaky)
            : IRuntimePostCommitIntentDispatcher
        {
            public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
                IsCreateBookmark(intent) && flaky.TakeFailure()
                    ? throw new TimeoutException("The scheduler work queue did not answer the continuation's enqueue.")
                    : inner.DispatchAsync(intent, cancellationToken);
        }
    }

    /// <summary>The event ids a logger was called with.</summary>
    private sealed class EventLog : ILogger<RuntimePostCommitOutboxProcessor>
    {
        private readonly ConcurrentQueue<int> _eventIds = new();

        public int Count(int eventId) => _eventIds.Count(logged => logged == eventId);

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter) => _eventIds.Enqueue(eventId.Id);
    }

    /// <summary>Runs one real resumption sweep at the drain's read.</summary>
    private sealed class SweepAtDrainRead(SweepTiming timing) : DrainReadInterceptor
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
            return timing == SweepTiming.BeforeTheDrainReads ? await read() : items;
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

    /// <summary>The effective outbox store, with the interceptor on the drain's reads and on every claim.</summary>
    private sealed class InterceptingOutboxStore(IRuntimePostCommitOutboxStore inner, DrainReadInterceptor interceptor)
        : ForwardingOutboxStore(inner)
    {
        public override ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> GetDeliverableAsync(
            RuntimePostCommitOutboxQuery query,
            CancellationToken cancellationToken = default) =>
            interceptor.ReadAsync(query, () => base.GetDeliverableAsync(query, cancellationToken));

        public override async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxClaim>> ClaimAsync(
            RuntimePostCommitOutboxClaimRequest request,
            CancellationToken cancellationToken = default)
        {
            var claims = await base.ClaimAsync(request, cancellationToken);
            interceptor.ObserveClaims(request, claims);
            return claims;
        }

        public override async ValueTask<IReadOnlyCollection<RuntimePostCommitOutboxItem>> ListClaimedAsync(
            RuntimePostCommitOutboxClaimedQuery query,
            CancellationToken cancellationToken = default)
        {
            var claimed = await base.ListClaimedAsync(query, cancellationToken);
            interceptor.ObserveClaimed(claimed);
            return claimed;
        }
    }
}
