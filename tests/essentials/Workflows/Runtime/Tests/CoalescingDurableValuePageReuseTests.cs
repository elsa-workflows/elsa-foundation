using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Contracts;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Services.Incidents;
using Elsa.Workflows.Runtime.Services.Recovery;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class CoalescingDurableValuePageReuseTests
{
    private const string WorkflowExecutionId = "workflow";

    [Fact]
    public async Task Memo_hit_remerges_the_current_staged_overlay()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "from-provider")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var query = Query();

        using (fixture.SessionAccessor.Push(session))
        {
            var first = await store.ListPageAsync(query);
            Assert.Equal("a", Assert.Single(first.Items).DurableValueId);

            session.BufferDeferred(UpsertCommit(Row("b", "staged")));
            var second = await store.ListPageAsync(query);

            Assert.Equal(["a", "b"], second.Items.Select(row => row.DurableValueId));
            Assert.Equal(["from-provider", "staged"], second.Items.Select(row => row.ValueId));
            Assert.Equal(1, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Direct_writes_invalidate_pages_and_failed_write_disables_reuse_for_the_session()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var query = Query();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(query);
            await store.SaveAsync(Row("b", "two"));
            var afterSave = await store.ListPageAsync(query);
            Assert.Equal(2, inner.PageReadCount);
            Assert.Equal(["a", "b"], afterSave.Items.Select(row => row.DurableValueId));

            await store.ListPageAsync(query); // Warm the new generation.
            Assert.Equal(2, inner.PageReadCount);
            Assert.True(await store.DeleteAsync(WorkflowExecutionId, "a"));
            var afterDelete = await store.ListPageAsync(query);
            Assert.Equal(3, inner.PageReadCount);
            Assert.Equal("b", Assert.Single(afterDelete.Items).DurableValueId);

            await store.ListPageAsync(query); // Warm again before an unsuccessful provider write.
            inner.ThrowOnNextSave = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveAsync(Row("c", "three")).AsTask());

            await store.ListPageAsync(query);
            await store.ListPageAsync(query);
            Assert.Equal(5, inner.PageReadCount);

            await store.SaveAsync(Row("d", "four"));
            await store.ListPageAsync(query);
            await store.ListPageAsync(query);
            Assert.Equal(7, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Deferred_replacement_and_tombstone_remerge_without_invalidating_the_provider_page()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "provider-a"), Row("b", "provider-b")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor);

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await checkpointStore.CommitAsync(
                UpsertCommit(Row("a", "staged-replacement")),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            await checkpointStore.CommitAsync(
                DeleteCommit(Row("b", "provider-b")),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));

            var page = await store.ListPageAsync(Query());

            Assert.Equal("a", Assert.Single(page.Items).DurableValueId);
            Assert.Equal("staged-replacement", page.Items[0].ValueId);
            Assert.Equal(1, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Nested_child_owns_an_independent_memo_and_parent_cannot_reactivate()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var parent = NewSession();
        var child = NewSession();

        using (fixture.SessionAccessor.Push(parent))
        {
            await store.ListPageAsync(Query());
            using (fixture.SessionAccessor.Push(child))
            {
                await store.ListPageAsync(Query());
                await store.ListPageAsync(Query());
                Assert.Equal(2, inner.PageReadCount);
            }

            await store.ListPageAsync(Query());
            Assert.Equal(3, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(4, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Disposed_owner_bypasses_the_old_memo_and_the_next_drain_starts_fresh()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;

        using (fixture.SessionAccessor.Push(NewSession()))
        {
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
            Assert.Equal(1, inner.PageReadCount);
        }

        await store.ListPageAsync(Query());
        Assert.Equal(2, inner.PageReadCount);

        using (fixture.SessionAccessor.Push(NewSession()))
        {
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Failed_delete_permanently_disables_reuse_even_after_a_later_successful_write()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            inner.ThrowOnNextDelete = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.DeleteAsync(WorkflowExecutionId, "a").AsTask());

            await store.SaveAsync(Row("b", "two"));
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());

            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Cancelled_save_permanently_disables_reuse_even_after_a_later_successful_write()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.SaveAsync(Row("b", "two"), cancellation.Token).AsTask());

            await store.SaveAsync(Row("c", "three"));
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());

            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Cancelled_delete_permanently_disables_reuse_even_after_a_later_successful_write()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => store.DeleteAsync(WorkflowExecutionId, "a", cancellation.Token).AsTask());

            await store.SaveAsync(Row("b", "two"));
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());

            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Read_started_before_write_cannot_fill_the_new_memo_generation()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();

        using (fixture.SessionAccessor.Push(session))
        {
            inner.BlockNextPageRead();
            var inFlightRead = store.ListPageAsync(Query()).AsTask();
            await inner.PageReadStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await store.SaveAsync(Row("b", "two"));
            inner.ReleasePageRead.TrySetResult();

            var first = await inFlightRead;
            Assert.Equal("a", Assert.Single(first.Items).DurableValueId);
            var refreshed = await store.ListPageAsync(Query());
            Assert.Equal(["a", "b"], refreshed.Items.Select(row => row.DurableValueId));
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Null_owner_suspension_disables_parent_memo_and_preserves_staged_overlay()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "provider-a")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var parent = NewSession();

        using (fixture.SessionAccessor.Push(parent))
        {
            await store.ListPageAsync(Query());
            parent.BufferDeferred(UpsertCommit(Row("c", "staged-c")));

            using (fixture.SessionAccessor.Push(null))
            {
                Assert.Null(fixture.SessionAccessor.Current);
                await store.SaveAsync(Row("b", "provider-b"));
            }

            Assert.Same(parent, fixture.SessionAccessor.Current);
            var page = await store.ListPageAsync(Query());

            Assert.Equal(["a", "b", "c"], page.Items.Select(row => row.DurableValueId));
            Assert.Equal(["provider-a", "provider-b", "staged-c"], page.Items.Select(row => row.ValueId));
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Provider_failure_is_returned_and_the_next_successful_page_is_admitted()
    {
        await using var fixture = WrapperFixture.Create([]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        using (fixture.SessionAccessor.Push(session))
        {
            inner.ThrowOnNextPageRead = true;
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListPageAsync(Query()).AsTask());
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Changed_across_scope_and_global_contexts_bypass_the_prior_page()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            Assert.Equal(1, inner.PageReadCount);

            fixture.AccessContextAccessor.Current =
                PersistenceAccessContext.Scoped(new PersistenceScope("other-tenant"));
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);

            fixture.AccessContextAccessor.Current =
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("maintenance"));
            await store.ListPageAsync(Query());
            Assert.Equal(3, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(4, inner.PageReadCount);

            fixture.AccessContextAccessor.Current = PersistenceAccessContext.Global;
            await store.ListPageAsync(Query());
            Assert.Equal(5, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(6, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Ineligible_manual_wrapper_does_not_resolve_the_recovery_codec()
    {
        var codecResolutionCount = 0;
        var sessionAccessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeCoalescingSessionAccessor>(sessionAccessor);
        services.AddSingleton<IRuntimeRecoveryContinuationCodec>(_ =>
        {
            codecResolutionCount++;
            throw new InvalidOperationException("The ineligible path must not resolve the codec.");
        });
        services.AddScoped<FakeDurableValueStateStore>();
        services.AddScoped<IDurableValueStateStore>(provider => new CoalescingDurableValueStateStore(
            new CoalescingInner<IDurableValueStateStore>(provider.GetRequiredService<FakeDurableValueStateStore>()),
            sessionAccessor,
            pageReuseRegistration: null,
            serviceProvider: provider));

        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await using var scope = provider.CreateAsyncScope();
        var store = scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
        var inner = scope.ServiceProvider.GetRequiredService<FakeDurableValueStateStore>();
        inner.AddInitial([Row("a", "one")]);
        var session = NewSession();

        using (sessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
        }

        Assert.Equal(2, inner.PageReadCount);
        Assert.Equal(0, codecResolutionCount);
    }

    [Theory]
    [InlineData("accessor")]
    [InlineData("codec")]
    [InlineData("carrier")]
    [InlineData("inner")]
    public async Task Live_registration_changes_after_warm_bypass_the_wrapper_memo(string changedGroup)
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
            Assert.Equal(1, inner.PageReadCount);

            switch (changedGroup)
            {
                case "accessor":
                    fixture.Services.RemoveAll<IPersistenceAccessContextAccessor>();
                    fixture.Services.AddSingleton<IPersistenceAccessContextAccessor>(new MutableAccessContextAccessor());
                    break;
                case "codec":
                    fixture.Services.RemoveAll<IRuntimeRecoveryContinuationCodec>();
                    fixture.Services.AddSingleton<IRuntimeRecoveryContinuationCodec>(NewCodec());
                    break;
                case "carrier":
                    fixture.Services.RemoveAll<RuntimeCoalescingDurableValuePageReuseRegistration>();
                    fixture.Services.AddSingleton(RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(
                        new ServiceCollection()));
                    break;
                case "inner":
                    fixture.Services.RemoveAll<CoalescingInner<IDurableValueStateStore>>();
                    fixture.Services.AddScoped<CoalescingInner<IDurableValueStateStore>>(_ =>
                        new CoalescingInner<IDurableValueStateStore>(inner));
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(changedGroup));
            }

            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Actual_inner_checkpoint_boundary_invalidates_pages_and_keeps_the_fresh_segment_active()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var commitStore = new CoalescingRuntimeCheckpointCommitStore(
            new CoalescingInner<IRuntimeCheckpointCommitStore>(new InMemoryRuntimeCheckpointCommitStore()),
            fixture.SessionAccessor);

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            Assert.Equal(1, inner.PageReadCount);

            await commitStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            Assert.True(session.IsActive);
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Folded_checkpoint_commit_invalidates_the_page_memo()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor);

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await checkpointStore.CommitAsync(
                UpsertCommit(Row("b", "staged")),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Deferred));
            await store.ListPageAsync(Query());
            Assert.Equal(1, inner.PageReadCount);

            await checkpointStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            Assert.True(session.IsActive);
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Flush_marker_deactivates_session_and_disables_page_reuse()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor);

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await checkpointStore.CommitAsync(
                FlushMarkerCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            Assert.False(session.IsActive);
            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
            await store.ListPageAsync(Query());
            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Checkpoint_written_with_no_current_owner_cannot_reactivate_parent_memo()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var parent = NewSession();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor);

        using (fixture.SessionAccessor.Push(parent))
        {
            await store.ListPageAsync(Query());
            using (fixture.SessionAccessor.Push(null))
                await checkpointStore.CommitAsync(
                    AttemptClaimBoundaryCommit(),
                    new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));

            await store.ListPageAsync(Query());
            Assert.Equal(2, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Failed_checkpoint_write_permanently_disables_reuse_after_later_success()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var commitInner = new FailOnceCheckpointCommitStore();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor, commitInner);

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await Assert.ThrowsAsync<InvalidOperationException>(() => checkpointStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate)).AsTask());

            await checkpointStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());

            Assert.Equal(2, commitInner.CommitCount);
            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact]
    public async Task Cancelled_checkpoint_write_permanently_disables_reuse_after_later_success()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var session = NewSession();
        var checkpointStore = NewCheckpointStore(fixture.SessionAccessor);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        using (fixture.SessionAccessor.Push(session))
        {
            await store.ListPageAsync(Query());
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => checkpointStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate),
                cancellation.Token).AsTask());

            await checkpointStore.CommitAsync(
                AttemptClaimBoundaryCommit(),
                new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
            await store.ListPageAsync(Query());
            await store.ListPageAsync(Query());

            Assert.Equal(3, inner.PageReadCount);
        }
    }

    [Fact(Timeout = 5_000)]
    public async Task Caller_cancellation_disables_memo_during_the_live_coalesced_drain()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var drainScopeFactory = new CapturingDrainScopeFactory(fixture.SessionAccessor);
        var drainer = new CancellationHoldingDrainer(store, inner);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        var orchestrator = new WorkflowDrainOrchestrator(
            drainer,
            new EmptyPostCommitOutboxProcessor(),
            [],
            TestCheckpointRuleViolationFaulter.Create(),
            new TestOwnershipService(),
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            checkpointStore,
            checkpointStore,
            new InMemoryWorkflowSchedulerWorkQueue(),
            coalescingScopeFactory: drainScopeFactory);
        using var cancellation = new CancellationTokenSource();

        var drain = orchestrator.DrainAsync(NewEnvelope(), new RuntimeSchedulerDrainRequest(WorkflowExecutionId), cancellation.Token).AsTask();
        await drainer.PageWarmed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(drainScopeFactory.Session);
        drainScopeFactory.Session!.BufferDeferred(UpsertCommit(Row("b", "staged-during-drain")));
        await cancellation.CancelAsync();
        drainer.ContinueAfterCancellation.TrySetResult();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => drain);
        Assert.Equal(2, inner.PageReadCount);
        var rowsAfterCancellation = drainer.RowsAfterCancellation ?? throw new InvalidOperationException("The drain did not capture its post-cancellation page.");
        Assert.Equal(["a", "b"], rowsAfterCancellation.Select(row => row.DurableValueId));
        Assert.Equal(["one", "staged-during-drain"], rowsAfterCancellation.Select(row => row.ValueId));
        Assert.True(drainScopeFactory.SessionWasCreated);
    }

    [Fact(Timeout = 5_000)]
    public async Task Heartbeat_cancellation_fences_tokenless_cleanup_reads_and_preserves_the_overlay()
    {
        await using var fixture = WrapperFixture.Create([Row("a", "one")]);
        var store = fixture.Store;
        var inner = fixture.Inner;
        var drainScopeFactory = new CapturingDrainScopeFactory(fixture.SessionAccessor);
        using var cancellationCallbackGate = new CancellationCallbackGate();
        var drainer = new HeartbeatLossHoldingDrainer(store, cancellationCallbackGate);
        var checkpointStore = new InMemoryRuntimeCheckpointCommitStore();
        var orchestrator = new WorkflowDrainOrchestrator(
            drainer,
            new EmptyPostCommitOutboxProcessor(),
            [],
            TestCheckpointRuleViolationFaulter.Create(),
            new StaleHeartbeatOwnershipService(),
            new AsyncLocalRuntimeExecutionOwnershipContextAccessor(),
            checkpointStore,
            checkpointStore,
            new InMemoryWorkflowSchedulerWorkQueue(),
            coalescingScopeFactory: drainScopeFactory);

        var drain = orchestrator.DrainAsync(
            NewEnvelope(),
            new RuntimeSchedulerDrainRequest(WorkflowExecutionId)).AsTask();
        await drainer.PageWarmed.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.NotNull(drainScopeFactory.Session);
        drainScopeFactory.Session!.BufferDeferred(UpsertCommit(Row("b", "staged-during-drain")));
        int pageReadsDuringCancellationCallback;
        IReadOnlyList<DurableValueState> rowsDuringCancellationCallback;
        try
        {
            await drainer.CancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await cancellationCallbackGate.CallbackEntered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await drainer.CleanupReadCompleted.Task.WaitAsync(TimeSpan.FromSeconds(5));
            pageReadsDuringCancellationCallback = inner.PageReadCount;
            rowsDuringCancellationCallback = drainer.RowsAfterCancellation!;
        }
        finally
        {
            cancellationCallbackGate.Release();
        }

        var exception = await Assert.ThrowsAsync<RuntimeExecutionOwnershipLostException>(() => drain);

        Assert.Equal("heartbeat", exception.Operation);
        Assert.Equal(RuntimeExecutionOwnershipTransitionStatus.Stale, exception.TransitionStatus);
        Assert.Equal(2, pageReadsDuringCancellationCallback);
        Assert.Equal(["a", "b"], rowsDuringCancellationCallback.Select(row => row.DurableValueId));
        Assert.Equal(["one", "staged-during-drain"], rowsDuringCancellationCallback.Select(row => row.ValueId));
        Assert.True(drainScopeFactory.SessionWasCreated);
    }

    private static RuntimeCoalescingSession NewSession() => new(
        WorkflowExecutionId,
        new InMemoryWorkflowSchedulerWorkQueue(),
        new CoalescingRuntimeCheckpointPersistenceOptions());

    private static DurableValueStatePageQuery Query() => new(WorkflowExecutionId, limit: 10);

    private static DurableValueState Row(string id, string value) => new(
        id,
        WorkflowExecutionId,
        value,
        new RuntimeValueTypeDescriptor("string", null, null),
        DurableValueLifecycle.None,
        DurableValueStorage.None,
        null,
        null,
        null,
        DateTimeOffset.UnixEpoch,
        new Dictionary<string, string>(StringComparer.Ordinal));

    private static HmacRuntimeRecoveryContinuationCodec NewCodec() => new(
        Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));

    private static RuntimeCheckpointCommit UpsertCommit(DurableValueState row) => new(
        $"commit-{row.DurableValueId}",
        new RuntimeCheckpoint(
            $"checkpoint-{row.DurableValueId}",
            "ActivityStarted",
            WorkflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: [],
            bookmarks: [],
            durableValues: [new RuntimeStateChange<DurableValueState>(
                row.DurableValueId,
                RuntimeStateChangeOperation.Upsert,
                row,
                new Dictionary<string, string>())],
            incidents: [],
            operational: []),
        [],
        new Dictionary<string, string>());

    private static RuntimeCheckpointCommit DeleteCommit(DurableValueState row) => new(
        $"delete-commit-{row.DurableValueId}",
        new RuntimeCheckpoint(
            $"delete-checkpoint-{row.DurableValueId}",
            "ActivityStarted",
            WorkflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: [],
            bookmarks: [],
            durableValues: [new RuntimeStateChange<DurableValueState>(
                row.DurableValueId,
                RuntimeStateChangeOperation.Delete,
                row,
                new Dictionary<string, string>())],
            incidents: [],
            operational: []),
        [],
        new Dictionary<string, string>());

    private static CoalescingRuntimeCheckpointCommitStore NewCheckpointStore(
        IRuntimeCoalescingSessionAccessor sessionAccessor,
        IRuntimeCheckpointCommitStore? inner = null) => new(
        new CoalescingInner<IRuntimeCheckpointCommitStore>(inner ?? new InMemoryRuntimeCheckpointCommitStore()),
        sessionAccessor);

    private static RuntimeCheckpointCommit FlushMarkerCommit() => AttemptClaimBoundaryCommit() with
    {
        Checkpoint = new RuntimeCheckpoint(
            "flush-marker-checkpoint",
            RuntimeCoalescingMetadataKeys.FlushCheckpointName,
            WorkflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string> { [RuntimeCoalescingMetadataKeys.CoalescedFlush] = "true" })
    };

    private static WorkflowExecutionCommandEnvelope NewEnvelope()
    {
        var now = DateTimeOffset.UtcNow;
        var command = new WorkflowExecutionCommand(
            CommandId: "page-reuse-command",
            WorkflowExecutionId: WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.RunSchedulerWork,
            EnqueuedAt: now,
            Payload: null,
            Metadata: new Dictionary<string, string>());
        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "page-reuse-envelope",
            workflowExecutionId: WorkflowExecutionId,
            command,
            idempotencyKey: "page-reuse-command",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: now);
    }

    private static RuntimeCheckpointCommit AttemptClaimBoundaryCommit() => new(
        "attempt-claim-commit",
        new RuntimeCheckpoint(
            "attempt-claim-checkpoint",
            RuntimeCheckpointNames.ActivityAttemptClaimed,
            WorkflowExecutionId,
            DateTimeOffset.UnixEpoch,
            [],
            new Dictionary<string, string>()),
        new RuntimeCheckpointStateChangeSet(
            workflowExecution: null,
            scheduler: null,
            activityExecutions: [],
            bookmarks: [],
            durableValues: [],
            incidents: [],
            operational: []),
        [],
        new Dictionary<string, string>());

    private sealed class WrapperFixture : IAsyncDisposable
    {
        private readonly ServiceProvider _provider;
        private readonly AsyncServiceScope _scope;

        private WrapperFixture(
            ServiceProvider provider,
            AsyncLocalRuntimeCoalescingSessionAccessor sessionAccessor,
            MutableAccessContextAccessor accessContextAccessor,
            IServiceCollection services,
            IReadOnlyCollection<DurableValueState> initialRows)
        {
            _provider = provider;
            _scope = provider.CreateAsyncScope();
            SessionAccessor = sessionAccessor;
            AccessContextAccessor = accessContextAccessor;
            Services = services;
            Store = _scope.ServiceProvider.GetRequiredService<IDurableValueStateStore>();
            Inner = _scope.ServiceProvider.GetRequiredService<FakeDurableValueStateStore>();
            Inner.AddInitial(initialRows);
        }

        public IDurableValueStateStore Store { get; }
        public FakeDurableValueStateStore Inner { get; }
        public AsyncLocalRuntimeCoalescingSessionAccessor SessionAccessor { get; }
        public MutableAccessContextAccessor AccessContextAccessor { get; }
        public IServiceCollection Services { get; }

        public static WrapperFixture Create(IReadOnlyCollection<DurableValueState> initialRows)
        {
            IServiceCollection services = new ServiceCollection();
            var concreteStore = new ServiceDescriptor(
                typeof(FakeDurableValueStateStore), typeof(FakeDurableValueStateStore), ServiceLifetime.Scoped);
            var durableStore = ServiceDescriptor.Scoped<IDurableValueStateStore>(provider =>
                provider.GetRequiredService<FakeDurableValueStateStore>());
            services.Add(concreteStore);
            services.Add(durableStore);

            var backend = new RuntimeOperationalStateStoreBackend(
                RuntimeOperationalStateStoreBackend.EntityFramework,
                [concreteStore, durableStore]);
            services.Add(ServiceDescriptor.Singleton(backend));
            var accessAccessor = new MutableAccessContextAccessor();
            services.Add(ServiceDescriptor.Singleton<IPersistenceAccessContextAccessor>(accessAccessor));
            var codec = new HmacRuntimeRecoveryContinuationCodec(
                Options.Create(new RuntimeRecoveryContinuationOptions { SigningKey = new string('k', 32) }));
            services.Add(ServiceDescriptor.Singleton<IRuntimeRecoveryContinuationCodec>(codec));
            var sessionAccessor = new AsyncLocalRuntimeCoalescingSessionAccessor();
            services.AddSingleton<IRuntimeCoalescingSessionAccessor>(sessionAccessor);

            var registration = RuntimeCoalescingDurableValuePageReuseRegistration.CaptureBeforeDecoration(services);
            Assert.True(registration.EligibleBeforeDecoration);

            services.Remove(durableStore);
            var inner = ServiceDescriptor.Scoped<CoalescingInner<IDurableValueStateStore>>(provider =>
                new CoalescingInner<IDurableValueStateStore>(provider.GetRequiredService<FakeDurableValueStateStore>()));
            var wrapper = ServiceDescriptor.Scoped<IDurableValueStateStore>(provider =>
                new CoalescingDurableValueStateStore(
                    provider.GetRequiredService<CoalescingInner<IDurableValueStateStore>>(),
                    provider.GetRequiredService<IRuntimeCoalescingSessionAccessor>(),
                    provider.GetRequiredService<RuntimeCoalescingDurableValuePageReuseRegistration>(),
                    provider));
            services.Add(inner);
            services.Add(wrapper);
            services.AddSingleton<FakeMarker>();
            var marker = services.Last(descriptor => descriptor.ServiceType == typeof(FakeMarker));
            var carrier = ServiceDescriptor.Singleton(registration);
            services.Add(carrier);
            registration.CaptureAfterDecoration(
                [new(durableStore, inner, wrapper)],
                marker,
                carrier);
            Assert.True(registration.IsEligible);

            var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
            return new WrapperFixture(provider, sessionAccessor, accessAccessor, services, initialRows);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await _scope.DisposeAsync();
            }
            finally
            {
                await _provider.DisposeAsync();
            }
        }
    }

    private sealed class MutableAccessContextAccessor : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } =
            PersistenceAccessContext.Scoped(new PersistenceScope("tenant"));
    }

    private sealed class FakeMarker;

    private sealed class CancellationCallbackGate : IDisposable
    {
        private readonly ManualResetEventSlim _release = new();

        public TaskCompletionSource CallbackEntered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Block()
        {
            CallbackEntered.TrySetResult();
            _release.Wait();
        }

        public void Release()
        {
            CallbackEntered.TrySetResult();
            _release.Set();
        }

        public void Dispose() => _release.Dispose();
    }

    private sealed class CapturingDrainScopeFactory(IRuntimeCoalescingSessionAccessor sessionAccessor)
        : IRuntimeCoalescingDrainScopeFactory
    {
        public bool SessionWasCreated { get; private set; }
        public RuntimeCoalescingSession? Session { get; private set; }

        public IRuntimeCoalescingDrainScope Begin(string workflowExecutionId, int? maxSegmentCheckpoints = null)
        {
            var session = new RuntimeCoalescingSession(
                workflowExecutionId,
                new InMemoryWorkflowSchedulerWorkQueue(),
                new CoalescingRuntimeCheckpointPersistenceOptions());
            SessionWasCreated = true;
            Session = session;
            return new CapturedDrainScope(session, sessionAccessor.Push(session));
        }
    }

    private sealed class CapturedDrainScope(RuntimeCoalescingSession session, IDisposable handle) : IRuntimeCoalescingDrainScope
    {
        public RuntimeCoalescingSession Session => session;
        public ValueTask FlushAtQuiescenceAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        public ValueTask DisposeAsync()
        {
            handle.Dispose();
            return ValueTask.CompletedTask;
        }
    }

    private sealed class CancellationHoldingDrainer(
        IDurableValueStateStore store,
        FakeDurableValueStateStore inner) : IWorkflowSchedulerDrainer
    {
        public TaskCompletionSource PageWarmed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource ContinueAfterCancellation { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<DurableValueState>? RowsAfterCancellation { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            await store.ListPageAsync(new DurableValueStatePageQuery(request.WorkflowExecutionId, limit: 10), CancellationToken.None);
            PageWarmed.TrySetResult();
            await ContinueAfterCancellation.Task;
            var page = await store.ListPageAsync(new DurableValueStatePageQuery(request.WorkflowExecutionId, limit: 10), CancellationToken.None);
            RowsAfterCancellation = page.Items;
            Assert.Equal(2, inner.PageReadCount);
            var now = DateTimeOffset.UtcNow;
            return new RuntimeSchedulerDrainResult(request.WorkflowExecutionId, now, now, []);
        }
    }

    private sealed class HeartbeatLossHoldingDrainer(
        IDurableValueStateStore store,
        CancellationCallbackGate cancellationCallbackGate) : IWorkflowSchedulerDrainer
    {
        public TaskCompletionSource PageWarmed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CancellationObserved { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CleanupReadCompleted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public IReadOnlyList<DurableValueState>? RowsAfterCancellation { get; private set; }

        public async ValueTask<RuntimeSchedulerDrainResult> DrainAsync(
            RuntimeSchedulerDrainRequest request,
            CancellationToken cancellationToken = default)
        {
            await store.ListPageAsync(new DurableValueStatePageQuery(request.WorkflowExecutionId, limit: 10), CancellationToken.None);
            PageWarmed.TrySetResult();
            using var delayedDisable = cancellationToken.Register(
                static state => ((CancellationCallbackGate)state!).Block(),
                cancellationCallbackGate);
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                CancellationObserved.TrySetResult();
            }

            // The later-registered blocker holds the session's disable callback after cancellation is observable.
            await cancellationCallbackGate.CallbackEntered.Task;
            var page = await store.ListPageAsync(new DurableValueStatePageQuery(request.WorkflowExecutionId, limit: 10), CancellationToken.None);
            RowsAfterCancellation = page.Items;
            CleanupReadCompleted.TrySetResult();
            var now = DateTimeOffset.UtcNow;
            return new RuntimeSchedulerDrainResult(request.WorkflowExecutionId, now, now, []);
        }
    }

    private sealed class StaleHeartbeatOwnershipService : IRuntimeExecutionOwnershipService
    {
        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new RuntimeExecutionLease("lease", workflowExecutionId, "owner", now, now.AddMilliseconds(120), 1));
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Stale, lease.FencingToken));

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken));

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class EmptyPostCommitOutboxProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(
            RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimePostCommitOutboxProcessResult([]));
    }

    private sealed class TestOwnershipService : IRuntimeExecutionOwnershipService
    {
        public ValueTask<RuntimeExecutionLease> AcquireAsync(string workflowExecutionId, CancellationToken cancellationToken = default)
        {
            var now = DateTimeOffset.UtcNow;
            return ValueTask.FromResult(new RuntimeExecutionLease("lease", workflowExecutionId, "owner", now, now.AddHours(1), 1));
        }

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> HeartbeatAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken));

        public ValueTask<RuntimeExecutionOwnershipTransitionResult> ReleaseAsync(RuntimeExecutionLease lease, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new RuntimeExecutionOwnershipTransitionResult(RuntimeExecutionOwnershipTransitionStatus.Applied, lease.FencingToken));

        public ValueTask EnsureCurrentAsync(string workflowExecutionId, long fencingToken, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
    }

    private sealed class FailOnceCheckpointCommitStore : IRuntimeCheckpointCommitStore
    {
        private bool _failNextCommit = true;

        public int CommitCount { get; private set; }

        public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
            RuntimeCheckpointCommit commit,
            RuntimeCheckpointPersistenceDecision decision,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CommitCount++;
            if (_failNextCommit)
            {
                _failNextCommit = false;
                throw new InvalidOperationException("Injected checkpoint commit failure.");
            }

            return ValueTask.FromResult(new RuntimeCheckpointCommitStoreResult([]));
        }
    }

    private sealed class FakeDurableValueStateStore : IDurableValueStateStore
    {
        private readonly Dictionary<string, DurableValueState> _rows = new(StringComparer.Ordinal);

        public int PageReadCount { get; private set; }
        public bool ThrowOnNextPageRead { get; set; }
        public bool ThrowOnNextSave { get; set; }
        public bool ThrowOnNextDelete { get; set; }
        public TaskCompletionSource PageReadStarted { get; private set; } = NewCompletionSource();
        public TaskCompletionSource ReleasePageRead { get; private set; } = NewCompletionSource();
        private bool _blockNextPageRead;

        public void AddInitial(IEnumerable<DurableValueState> rows)
        {
            foreach (var row in rows)
                _rows.Add(row.DurableValueId, row);
        }

        public ValueTask<DurableValueState> SaveAsync(DurableValueState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnNextSave)
            {
                ThrowOnNextSave = false;
                throw new InvalidOperationException("Injected save failure.");
            }

            _rows[state.DurableValueId] = state;
            return ValueTask.FromResult(state);
        }

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnNextDelete)
            {
                ThrowOnNextDelete = false;
                throw new InvalidOperationException("Injected delete failure.");
            }

            return ValueTask.FromResult(_rows.Remove(durableValueId));
        }

        public ValueTask<DurableValueState?> FindAsync(string workflowExecutionId, string durableValueId, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(_rows.GetValueOrDefault(durableValueId));
        }

        public async ValueTask<RuntimeStorePage<DurableValueState>> ListPageAsync(DurableValueStatePageQuery query, CancellationToken cancellationToken = default)
        {
            PageReadCount++;
            cancellationToken.ThrowIfCancellationRequested();
            if (ThrowOnNextPageRead)
            {
                ThrowOnNextPageRead = false;
                throw new InvalidOperationException("Injected page-read failure.");
            }

            IReadOnlyList<DurableValueState> rows = _rows.Values
                .Where(row => row.WorkflowExecutionId == query.WorkflowExecutionId)
                .OrderBy(row => row.DurableValueId, StringComparer.Ordinal)
                .Take(query.Limit)
                .ToArray();
            if (_blockNextPageRead)
            {
                _blockNextPageRead = false;
                PageReadStarted.TrySetResult();
                await ReleasePageRead.Task.WaitAsync(cancellationToken);
            }

            return new RuntimeStorePage<DurableValueState>(query, rows);
        }

        public void BlockNextPageRead()
        {
            PageReadStarted = NewCompletionSource();
            ReleasePageRead = NewCompletionSource();
            _blockNextPageRead = true;
        }

        private static TaskCompletionSource NewCompletionSource() => new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
