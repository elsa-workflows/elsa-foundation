using System.Text.Json;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingDeliveredContinuationProofTests
{
    private const string ExecutionId = "proof-run";
    private static readonly DateTimeOffset Now = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData("same")]
    [InlineData("renewed-lease")]
    [InlineData("legacy-unpartitioned")]
    public async Task A_committed_receipt_settles_the_real_physical_claim_without_dispatch(string variant)
    {
        await using var fixture = await Fixture.CreateAsync(variant);
        Assert.True(fixture.HasCommittedReceipt());
        var result = await fixture.ProcessAsync();
        Assert.Equal(0, fixture.Dispatcher.Dispatches);
        Assert.Equal(1, result.DeliveredCount);
        var settled = await fixture.Inner.FindAsync(fixture.PhysicalItem.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, settled?.Status);
        Assert.Equal(2, settled?.DeliveryFencingToken);
        Assert.Equal(1, settled?.DeliveryAttemptCount);
    }

    [Theory]
    [InlineData("unflushed")]
    [InlineData("failed-advance")]
    [InlineData("wrong-flush-fence")]
    [InlineData("wrong-owner")]
    [InlineData("wrong-lease")]
    [InlineData("wrong-token")]
    [InlineData("no-owner")]
    [InlineData("other-scope")]
    [InlineData("missing-scope")]
    [InlineData("global-scope")]
    [InlineData("other-wrapper")]
    [InlineData("other-physical-store")]
    [InlineData("other-workflow")]
    [InlineData("changed-payload")]
    [InlineData("changed-metadata")]
    [InlineData("other-kind")]
    [InlineData("not-persisted")]
    [InlineData("pending-overlay")]
    [InlineData("failed-overlay")]
    [InlineData("new-drain")]
    [InlineData("disposed-drain")]
    public async Task A_nonmatching_or_incomplete_receipt_preserves_normal_physical_delivery(string variant)
    {
        await using var fixture = await Fixture.CreateAsync(variant);
        var result = await fixture.ProcessAsync();
        Assert.Equal(1, fixture.Dispatcher.Dispatches);
        Assert.Equal(1, result.DeliveredCount);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered,
            (await fixture.Inner.FindAsync(fixture.PhysicalItem.OutboxItemId))?.Status);
    }

    [Theory]
    [InlineData("missing-source")]
    [InlineData("contradictory-source")]
    [InlineData("unavailable-source")]
    public async Task A_matching_receipt_never_bypasses_required_physical_source_validation(string variant)
    {
        await using var fixture = await Fixture.CreateAsync(variant);
        Assert.True(fixture.HasCommittedReceipt());
        var error = await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => fixture.ProcessAsync());
        Assert.Equal(variant == "unavailable-source", error.IsUnavailable);
        if (variant == "unavailable-source") Assert.Same(fixture.Physical.Failure, error.InnerException);
        Assert.Equal(0, fixture.Dispatcher.Dispatches);
        var retained = await fixture.Inner.FindAsync(fixture.PhysicalItem.OutboxItemId);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained?.Status);
        Assert.Equal(0, retained?.DeliveryAttemptCount);
        Assert.Equal(0, fixture.Physical.CompletionCalls);
    }

    [Theory]
    [InlineData("lost-renewal")]
    [InlineData("lost-completion")]
    public async Task A_matching_receipt_cannot_settle_a_successor_claim(string variant)
    {
        // Contrast the suppressed receipt path with ordinary delivery through the same real claim transitions.
        foreach (var useReceipt in new[] { true, false })
        {
            await using var fixture = await Fixture.CreateAsync(variant);
            Assert.True(fixture.HasCommittedReceipt());
            var result = await fixture.ProcessAsync(useReceipt);
            var dispatched = variant == "lost-completion" && !useReceipt;
            Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner, Assert.Single(result.Items).Outcome);
            Assert.Equal(0, result.DeliveredCount);
            Assert.Equal(dispatched ? 1 : 0, fixture.Dispatcher.Dispatches);
            Assert.Equal([dispatched], fixture.Logger.ClaimLostDispatchFlags);
            var retained = await fixture.Inner.FindAsync(fixture.PhysicalItem.OutboxItemId);
            Assert.Equal("successor", retained?.DeliveringOwnerId);
            Assert.Equal(3, retained?.DeliveryFencingToken);
            Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained?.Status);
            Assert.Equal(0, retained?.DeliveryAttemptCount);
            Assert.Equal(variant == "lost-renewal" ? 0 : 1, fixture.Physical.CompletionCalls);
        }
    }

    [Fact]
    public async Task A_source_handler_that_loses_its_claim_after_invocation_reports_the_dispatch_attempt()
    {
        await using var fixture = await Fixture.CreateAsync("handler-stale-source");
        Assert.True(fixture.HasCommittedReceipt());
        var result = await fixture.ProcessAsync(useReceipt: false);
        Assert.Equal(RuntimePostCommitOutboxClaimCompletionOutcome.SupersededByOtherOwner, Assert.Single(result.Items).Outcome);
        Assert.Equal(0, result.DeliveredCount);
        Assert.Equal(1, fixture.Dispatcher.Dispatches);
        Assert.Equal([true], fixture.Logger.ClaimLostDispatchFlags);
        Assert.Equal(0, fixture.Physical.CompletionCalls);
        var retained = await fixture.Inner.FindAsync(fixture.PhysicalItem.OutboxItemId);
        Assert.Equal("successor", retained?.DeliveringOwnerId);
        Assert.Equal(3, retained?.DeliveryFencingToken);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, retained?.Status);
        Assert.Equal(0, retained?.DeliveryAttemptCount);
    }

    private sealed class Fixture(ServiceProvider host, AsyncServiceScope scope, FakeTimeProvider clock,
        RuntimeCoalescingSession session, IRuntimeCoalescingSessionAccessor sessions,
        IRuntimeExecutionOwnershipContextAccessor owners, CoalescingRuntimePostCommitOutboxStore store,
        InMemoryRuntimeCheckpointCommitStore inner, RuntimePostCommitOutboxItem physicalItem,
        RuntimeExecutionLease? currentLease, Access? access, RecordingDispatcher dispatcher, PhysicalFaults physical,
        RecordingLogger logger) : IAsyncDisposable
    {
        public InMemoryRuntimeCheckpointCommitStore Inner => inner;
        public RuntimePostCommitOutboxItem PhysicalItem => physicalItem;
        public RecordingDispatcher Dispatcher => dispatcher;
        public PhysicalFaults Physical => physical;
        public RecordingLogger Logger => logger;
        public bool HasCommittedReceipt() => session.HasCommittedContinuation(physicalItem, physicalItem.Intent,
            store, access?.Current, currentLease);

        public async Task<RuntimePostCommitOutboxProcessResult> ProcessAsync(bool useReceipt = true)
        {
            using var sessionScope = sessions.Push(useReceipt ? session : null);
            using var ownerScope = owners.Push(currentLease);
            var processor = new RuntimePostCommitOutboxProcessor(store, dispatcher, clock,
                DefaultRuntimeFaultCapturePolicy.CreateDefault(), null, [], logger, null, sessions, access, [], null, owners);
            return await processor.ProcessAsync(new(1, physicalItem.Intent.WorkflowExecutionId));
        }

        public static async Task<Fixture> CreateAsync(string variant)
        {
            var services = new ServiceCollection();
            var clock = new FakeTimeProvider(Now);
            services.AddSingleton<TimeProvider>(clock);
            new WorkflowsRuntimeApiFeature().ConfigureServices(services);
            services.AddPersistenceModes();
            var host = services.BuildServiceProvider();
            var scope = host.CreateAsyncScope();
            var provider = scope.ServiceProvider;
            var inner = provider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>();
            var sessions = provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>();
            var owners = provider.GetRequiredService<IRuntimeExecutionOwnershipContextAccessor>();
            var physicalProvider = new PhysicalFaults(inner, clock, variant);
            var store = new CoalescingRuntimePostCommitOutboxStore(new CoalescingInner<IRuntimePostCommitOutboxStore>(physicalProvider),
                sessions, provider.GetRequiredService<IWorkflowExecutionStateStore>());
            IRuntimePostCommitOutboxStore sessionInner = variant == "other-physical-store"
                ? new InMemoryRuntimeCheckpointCommitStore(state: new InMemoryRuntimeCheckpointStoreState())
                : physicalProvider;
            var queue = new AdvanceFailureQueue(new InMemoryWorkflowSchedulerWorkQueue());
            var session = new RuntimeCoalescingSession(ExecutionId, queue,
                new PersistenceModeOptions(), sessionInner);
            IRuntimeCoalescingDrainScope? ownedDrain = null;
            if (variant == "disposed-drain")
            {
                ownedDrain = new RuntimeCoalescingDrainScopeFactory(sessions, provider.GetRequiredService<RuntimeCheckpointCommitter>(),
                    new(queue), new(sessionInner), new PersistenceModeOptions(), clock).Begin(ExecutionId, new(PersistenceMode.Balanced, true));
                session = ownedDrain.Session;
            }
            await session.EnsureQueueSeededAsync(default);
            var intent = Intent();
            if (variant == "other-kind") intent = Intent(kind: "Other.Intent");
            var original = Item(intent);
            var physical = variant switch
            {
                "other-workflow" => Item(Intent(workflow: "other-run")),
                "changed-payload" => Item(Intent(payload: "changed")),
                "changed-metadata" => Item(Intent(metadata: "changed")),
                _ => original
            };
            await inner.AddPendingForTestingAsync(physical);
            Assert.Single(await inner.ClaimAsync(new("peer", Now, TimeSpan.FromMinutes(1), 1)));
            if (sessionInner is InMemoryRuntimeCheckpointCommitStore otherInner)
            {
                await otherInner.AddPendingForTestingAsync(original);
                Assert.Single(await otherInner.ClaimAsync(new("other-provider-peer", Now, TimeSpan.FromMinutes(1), 1)));
            }
            var change = new RuntimeStateChange<RuntimePostCommitOutboxItem>(original.OutboxItemId,
                RuntimeStateChangeOperation.Upsert, original, new Dictionary<string, string>());
            session.RecordCapFlushState(new RuntimeCheckpointStateChangeSet(null, null, [], [], [], [], [], postCommitOutbox: [change]));
            if (variant != "not-persisted") session.MarkOutboxDurablyPersisted([change]);
            if (variant != "pending-overlay")
                session.RecordOutboxDelivery(new(original.OutboxItemId,
                    variant == "failed-overlay" ? RuntimePostCommitOutboxStatus.FailedFinal : RuntimePostCommitOutboxStatus.Delivered,
                    Now, variant == "failed-overlay" ? "constructed failure" : null));
            var lease = variant == "failed-advance"
                ? await provider.GetRequiredService<IRuntimeExecutionOwnershipService>().AcquireAsync(ExecutionId)
                : Lease();
            var selectedAccess = variant == "legacy-unpartitioned" ? null : new Access(PersistenceAccessContext.Scoped(new("scope-a")));
            session.RememberDeliveredContinuation(original, store, selectedAccess?.Current, lease);
            if (variant == "failed-advance")
            {
                queue.Armed = true;
                var flush = new RuntimeCheckpointCommit("proof-flush",
                    new("proof-flush-checkpoint", RuntimeCoalescingMetadataKeys.FlushCheckpointName, ExecutionId, Now, [],
                        new Dictionary<string, string> { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true" }),
                    new(null, null, [], [], [], [], []), [], new Dictionary<string, string>()) { ExpectedFence = lease.ToFence() };
                using var selected = sessions.Push(session);
                var commits = new CoalescingRuntimeCheckpointCommitStore(new CoalescingInner<IRuntimeCheckpointCommitStore>(inner), sessions);
                await Assert.ThrowsAsync<IOException>(() => commits.CommitAsync(flush, new(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());
                Assert.Contains(inner.ListCommits(), record => record.Commit.CommitId == flush.CommitId);
                Assert.False(session.IsActive);
                Assert.False(session.HasCommittedContinuation(original, intent, store, selectedAccess?.Current, lease));
            }
            else
            {
                await session.ReconcileDurablyPersistedOutboxAsync(Now, default);
                if (variant != "unflushed")
                {
                    await session.AdvanceInnerQueueAsync(true, default);
                    session.ConfirmReconciledContinuations(variant == "wrong-flush-fence" ? Lease(token: 2).ToFence() : lease.ToFence());
                }
            }
            session.ClearBuffer();
            session.Deactivate();
            if (ownedDrain is not null)
            {
                Assert.True(session.HasCommittedContinuation(original, intent, store, selectedAccess?.Current, lease));
                await ownedDrain.DisposeAsync();
                Assert.False(session.HasCommittedContinuation(original, intent, store, selectedAccess?.Current, lease));
            }
            if (variant == "new-drain")
                session = new RuntimeCoalescingSession(ExecutionId, new InMemoryWorkflowSchedulerWorkQueue(), new PersistenceModeOptions(), inner);
            if (variant == "new-drain") session.Deactivate();
            if (variant == "other-wrapper")
                store = new(new CoalescingInner<IRuntimePostCommitOutboxStore>(physicalProvider), sessions,
                    provider.GetRequiredService<IWorkflowExecutionStateStore>());
            selectedAccess = variant switch
            {
                "other-scope" => new(PersistenceAccessContext.Scoped(new("scope-b"))),
                "missing-scope" => null,
                "global-scope" => new(PersistenceAccessContext.Global),
                _ => selectedAccess
            };
            RuntimeExecutionLease? currentLease = variant switch
            {
                "wrong-owner" => Lease(owner: "other-owner"),
                "wrong-lease" => Lease(id: "other-lease"),
                "wrong-token" => Lease(token: 2),
                "no-owner" => null,
                "renewed-lease" => new(lease.LeaseId, ExecutionId, lease.OwnerId, Now, Now.AddHours(2), lease.FencingToken),
                _ => lease
            };
            clock.Advance(TimeSpan.FromMinutes(2));
            physicalProvider.Armed = true;
            var dispatcher = new RecordingDispatcher { RequiredSource = variant.EndsWith("-source", StringComparison.Ordinal) };
            if (variant == "handler-stale-source")
                dispatcher.BeforeSourceDispatch = async (source, token) =>
                {
                    await physicalProvider.StealAsync(source.SourceOutboxItemId, token);
                    throw new RuntimePostCommitOutboxStaleClaimException(source.SourceOutboxItemId, source.Claim.OwnerId,
                        source.Claim.FencingToken, "successor", 3, RuntimePostCommitOutboxStatus.Delivering);
                };
            return new(host, scope, clock, session, sessions, owners, store, inner, physical, currentLease, selectedAccess,
                dispatcher, physicalProvider, new());
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await host.DisposeAsync();
        }
    }

    private static RuntimeExecutionLease Lease(string id = "lease", string owner = "owner", long token = 1) =>
        new(id, ExecutionId, owner, Now, Now.AddHours(1), token);

    private static RuntimePostCommitIntent Intent(string workflow = ExecutionId, string payload = "original",
        string metadata = "original", string kind = RuntimePostCommitIntentKinds.EnqueueSchedulerWork) =>
        new("continue", workflow, kind, Now, null, "once", JsonSerializer.SerializeToElement(payload),
            new Dictionary<string, string> { ["tag"] = metadata });

    private static RuntimePostCommitOutboxItem Item(RuntimePostCommitIntent intent, string id = "commit:continue") =>
        new(id, intent, RuntimePostCommitOutboxStatus.Pending, Now, Now);

    private sealed class Access(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => current;
    }

    private sealed class AdvanceFailureQueue(InMemoryWorkflowSchedulerWorkQueue inner) : QueueWithoutVisibility(inner)
    {
        public bool Armed { get; set; }
        public override ValueTask<RuntimeStorePage<RuntimeSchedulerWorkItem>> ListAsync(RuntimeSchedulerWorkQuery query,
            CancellationToken token = default) => Armed
            ? ValueTask.FromException<RuntimeStorePage<RuntimeSchedulerWorkItem>>(new IOException("Constructed durable queue advance failure."))
            : base.ListAsync(query, token);
    }

    private sealed class RecordingDispatcher : IRuntimePostCommitIntentDispatcher
    {
        public int Dispatches { get; private set; }
        public bool RequiredSource { get; init; }
        public Func<PostCommitDeliveryContext, CancellationToken, ValueTask>? BeforeSourceDispatch { get; set; }
        public bool RequiresDurableSource(string kind) => RequiredSource;
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default)
        {
            Dispatches++;
            return ValueTask.CompletedTask;
        }
        public async ValueTask DispatchAsync(PostCommitDeliveryContext source, CancellationToken cancellationToken = default)
        {
            Dispatches++;
            if (BeforeSourceDispatch is { } beforeDispatch)
                await beforeDispatch(source, cancellationToken);
        }
    }

    private sealed class RecordingLogger : ILogger<RuntimePostCommitOutboxProcessor>
    {
        public List<bool> ClaimLostDispatchFlags { get; } = [];
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (eventId.Id == 68113 && state is IEnumerable<KeyValuePair<string, object?>> properties)
                ClaimLostDispatchFlags.Add(Assert.IsType<bool>(Assert.Single(properties, item => item.Key == "Dispatched").Value));
        }
    }

    private sealed class PhysicalFaults(InMemoryRuntimeCheckpointCommitStore inner, FakeTimeProvider clock, string variant)
        : ForwardingOutboxStore(inner), IPostCommitOutboxLookupStore, IRuntimePostCommitOutboxClaimStore,
            IRuntimePostCommitOutboxClaimCompletionStore
    {
        public bool Armed { get; set; }
        public IOException Failure { get; } = new("Constructed unavailable durable source.");
        public int CompletionCalls { get; private set; }

        public async ValueTask<RuntimePostCommitOutboxItem?> FindDurableAsync(string id, CancellationToken token = default)
        {
            if (Armed && variant == "missing-source") return null;
            if (Armed && variant == "unavailable-source") throw Failure;
            var item = await inner.FindAsync(id, token);
            if (Armed && variant == "contradictory-source" && item is not null)
                return new(item.OutboxItemId, Intent(payload: "contradictory"), item.Status, item.RecordedAt, item.AvailableAt,
                    item.RetryPolicy, item.DeliveryAttemptCount, item.DeliveringOwnerId, item.DeliveryStartedAt, item.DeliveredAt,
                    item.LastFailureMessage, item.Metadata, item.DeliveryFencingToken, item.DeliveryVisibleAfter);
            return item;
        }

        public new async ValueTask<RuntimePostCommitOutboxClaim?> RenewClaimAsync(RuntimePostCommitOutboxClaim claim,
            DateTimeOffset now, TimeSpan visibilityTimeout, CancellationToken token = default)
        {
            if (Armed && variant == "lost-renewal") await StealAsync(claim.OutboxItemId, token);
            return await inner.RenewClaimAsync(claim, now, visibilityTimeout, token);
        }

        public new async ValueTask<RuntimePostCommitOutboxClaimCompletionOutcome> CompleteClaimAsync(
            RuntimePostCommitOutboxClaimCompletion completion, CancellationToken token = default)
        {
            CompletionCalls++;
            if (Armed && variant == "lost-completion") await StealAsync(completion.Claim.OutboxItemId, token);
            return await inner.CompleteClaimAsync(completion, token);
        }

        public async Task StealAsync(string id, CancellationToken token)
        {
            var item = (await inner.FindAsync(id, token))!;
            clock.SetUtcNow(item.DeliveryVisibleAfter!.Value.AddSeconds(1));
            var successor = Assert.Single(await inner.ClaimAsync(new("successor", clock.GetUtcNow(), TimeSpan.FromMinutes(1), 1), token));
            Assert.Equal(id, successor.OutboxItemId);
        }
    }
}
