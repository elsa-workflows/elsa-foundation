using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Runtime;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Kernel;
using Groundwork.Query.Model;
using Groundwork.Sqlite;
using Groundwork.Store;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Runtime.Tests;

public sealed class GroundworkV2RuntimePostCommitOutboxStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Sqlite_round_trips_long_identity_claim_reclaim_and_completion()
    {
        await using var fixture = SqliteFixture.Create();
        var store = fixture.Store("tenant-a");
        var id = new string('x', 451);
        var item = Pending(id, "workflow-a");

        await store.SavePendingAsync(item);
        Assert.Equal(id, (await ((IPostCommitOutboxLookupStore)store).FindAsync(id))!.OutboxItemId);
        Assert.Equal(id, Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10))).OutboxItemId);

        var first = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-a", Now, TimeSpan.FromMinutes(1), 1)));
        var second = Assert.Single(await store.ClaimAsync(
            new RuntimePostCommitOutboxClaimRequest("owner-b", Now.AddMinutes(2), TimeSpan.FromMinutes(1), 1)));
        Assert.Equal(first.FencingToken + 1, second.FencingToken);
        await Assert.ThrowsAsync<RuntimePostCommitOutboxStaleClaimException>(() => store.RecordDeliveryResultAsync(
            first,
            new RuntimePostCommitOutboxDeliveryResult(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))).AsTask());

        await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            second,
            new RuntimePostCommitOutboxDeliveryResult(id, RuntimePostCommitOutboxStatus.Delivered, Now.AddMinutes(2))));
        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now.AddHours(1), 10)));

        var row = fixture.Connection.OpenSession(
            ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind),
            StorageAccess.Scoped(new StorageScope("tenant-a")))
            .Read(GroundworkRuntimeRowStore.Key(GroundworkV2PostCommitOutboxPhysicalId(id)));
        Assert.NotNull(row);
        Assert.Equal(
            GroundworkV2PostCommitOutboxPhysicalId(id),
            row!.Values.Values[ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField]);
    }

    [Fact]
    public async Task Sqlite_scopes_and_restart_keep_rows_isolated_and_durable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"elsa-v2-outbox-{Guid.NewGuid():N}.db");
        try
        {
            await using (var fixture = SqliteFixture.Create(path))
            {
                await fixture.Store("tenant-a").SavePendingAsync(Pending("same-id", "workflow-a"));
                await fixture.Store("tenant-b").SavePendingAsync(Pending("same-id", "workflow-b"));
            }

            await using (var fixture = SqliteFixture.Create(path))
            {
                var a = fixture.Store("tenant-a");
                var b = fixture.Store("tenant-b");
                Assert.Equal("workflow-a", (await a.FindAsync("same-id"))!.Intent.WorkflowExecutionId);
                Assert.Equal("workflow-b", (await b.FindAsync("same-id"))!.Intent.WorkflowExecutionId);
            }
        }
        finally
        {
            foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}-journal", $"{path}.schema.lock" })
                if (File.Exists(candidate))
                    File.Delete(candidate);
        }
    }

    [Fact]
    public async Task Candidate_routes_are_bounded_and_ordered_by_the_declared_manifest_prefix()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        var requests = new List<QueryRequest>();
        var source = new RecordingSessionSource(new RecordingSession(unit, requests), unit);
        var store = new GroundworkV2RuntimePostCommitOutboxStore(source, Access("tenant-a"));

        Assert.Empty(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, int.MaxValue)));
        Assert.Empty(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), int.MaxValue, "workflow-a", "test.intent")));

        Assert.Equal(2, requests.Count);
        Assert.All(requests, request => Assert.Equal(RuntimeStorePageRequest.MaximumLimit, request.Paging.Limit));
        Assert.Equal(
            [
                ElsaRuntimeV2StorageManifest.WorkflowExecutionIdField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxIntentKindField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxRecordedAtField,
                ElsaRuntimeV2StorageManifest.PostCommitOutboxItemIdField
            ],
            requests[1].Order.Select(term => term.Column.Name));
    }

    [Fact]
    public async Task Sqlite_pending_replay_is_exactly_idempotent_and_conflicting_intent_is_refused()
    {
        await using var fixture = SqliteFixture.Create();
        var store = fixture.Store("tenant-a");
        var pending = Pending("replay", "workflow-a", kind: "publish");

        await store.SavePendingAsync(pending);
        await store.SavePendingAsync(pending);

        var conflicting = Pending("replay", "workflow-a", kind: "other");
        Assert.NotEqual(Serialize(pending.Intent), Serialize(conflicting.Intent));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SavePendingAsync(conflicting).AsTask());

        var persisted = await store.FindAsync("replay");
        Assert.NotNull(persisted);
        Assert.Equal("publish", persisted.Intent.Kind);
        Assert.Single(await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10)));
    }

    [Fact]
    public async Task Sqlite_null_availability_is_immediately_eligible_and_exhausted_retry_is_not()
    {
        await using var fixture = SqliteFixture.Create();
        var store = fixture.Store("tenant-a");
        await store.SavePendingAsync(PendingWithoutAvailability("null-available", "workflow-a"));
        await store.SavePendingAsync(Pending(
            "exhausted",
            "workflow-a",
            retryPolicy: new RuntimePostCommitRetryPolicy(maxAttempts: 1, delay: TimeSpan.FromSeconds(1))));

        await store.RecordDeliveryResultAsync(new RuntimePostCommitOutboxDeliveryResult(
            "exhausted",
            RuntimePostCommitOutboxStatus.FailedRetryable,
            Now,
            "final failure"));

        var deliverable = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 10));
        Assert.Equal("null-available", Assert.Single(deliverable).OutboxItemId);
        Assert.Null(deliverable.Single().AvailableAt);
        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await store.FindAsync("exhausted"))!.Status);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 10)));
        Assert.Equal("null-available", claim.OutboxItemId);
    }

    [Fact]
    public async Task Current_rows_refuse_missing_outbox_eligibility_projections()
    {
        await using var fixture = SqliteFixture.Create();
        var item = PendingWithoutAvailability("missing-eligibility", "workflow-a");
        var values = GroundworkV2PostCommitOutboxStorageConventions.Values(item).Values
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        values[ElsaRuntimeV2StorageManifest.PostCommitOutboxDeliverableAtField] = null;
        values[ElsaRuntimeV2StorageManifest.PostCommitOutboxClaimableAtField] = null;
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        var session = fixture.Connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")));
        Assert.Equal(
            WriteOutcomeStatus.Inserted,
            session.Insert(new StorageValues(values), WriteOptions.CreateOnly).Status);

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            fixture.Store("tenant-a").FindAsync(item.OutboxItemId).AsTask());
    }

    [Fact]
    public async Task Sqlite_pending_order_is_deterministic_and_filters_match_workflow_and_intent()
    {
        await using var fixture = SqliteFixture.Create();
        var store = fixture.Store("tenant-a");
        await store.SavePendingAsync(PendingAt("z", Now, "workflow-a", "publish"));
        await store.SavePendingAsync(PendingAt("a", Now, "workflow-a", "publish"));
        await store.SavePendingAsync(PendingAt("b", Now, "workflow-a", "signal"));
        await store.SavePendingAsync(PendingAt("foreign", Now, "workflow-b", "publish"));

        var ordered = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(Now, 2));
        Assert.Equal(["a", "b"], ordered.Select(item => item.OutboxItemId));
        var filtered = await store.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(
            Now,
            10,
            workflowExecutionId: "workflow-a",
            intentKind: "publish"));
        Assert.Equal(["a", "z"], filtered.Select(item => item.OutboxItemId));
    }

    [Fact]
    public async Task Sqlite_long_alias_collision_fails_closed_even_when_the_foreign_row_is_consistent()
    {
        await using var fixture = SqliteFixture.Create();
        var store = fixture.Store("tenant-a");
        var longId = new string('x', 451);
        var collidingId = GroundworkV2PostCommitOutboxPhysicalId(longId);
        await store.SavePendingAsync(Pending(longId, "workflow-long"));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.SavePendingAsync(Pending(collidingId, "workflow-short")).AsTask());
        Assert.Contains("identity collision", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(longId, (await store.FindAsync(longId))!.OutboxItemId);
    }

    [Fact]
    public async Task Global_and_across_scope_access_refuse_before_opening_a_session()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        var session = new NoIoSession(unit);
        var source = new NoIoSessionSource(session, unit);
        var global = new GroundworkV2RuntimePostCommitOutboxStore(
            source,
            new TestAccessContextAccessor(PersistenceAccessContext.Global));
        var across = new GroundworkV2RuntimePostCommitOutboxStore(
            source,
            new TestAccessContextAccessor(
                PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync("item").AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => across.FindAsync("item").AsTask());
        Assert.False(session.ReadWasCalled);
        Assert.Equal(0, source.OpenCount);
    }

    [Fact]
    public async Task Cancellation_is_observed_before_any_v2_session_io()
    {
        var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind);
        var session = new NoIoSession(unit);
        var source = new NoIoSessionSource(session, unit);
        var store = new GroundworkV2RuntimePostCommitOutboxStore(source, Access("tenant-a"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() => store.FindAsync("item", cancellation.Token).AsTask());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.SavePendingAsync(
            Pending("item", "workflow-a"), cancellation.Token).AsTask());
        Assert.Equal(0, source.OpenCount);
        Assert.False(session.ReadWasCalled);
    }

    [Fact]
    public async Task Final_child_failure_projects_dispatch_failed_atomically()
    {
        await using var fixture = SqliteFixture.Create();
        var dispatch = PendingDispatchRecord("parent-final", "activity-final", WorkflowDispatchMode.FireAndForget);
        fixture.SeedDispatch(dispatch);
        var store = fixture.Store("tenant-a");
        var item = PendingDispatch("start-final", dispatch);
        await store.SavePendingAsync(item);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 1)));

        await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(1), "child-start-failure"),
            dispatch.TransitionToDispatchFailed(Now.AddSeconds(1))));

        Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await store.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.DispatchFailed, fixture.ReadDispatch(dispatch.DispatchId).Status);
    }

    [Fact]
    public async Task Visible_matching_child_wins_over_a_claimed_failure_atomically()
    {
        await using var fixture = SqliteFixture.Create();
        var dispatch = PendingDispatchRecord("parent-visible", "activity-visible", WorkflowDispatchMode.WaitForCompletion);
        fixture.SeedDispatch(dispatch);
        fixture.SeedExecution(ChildState(dispatch));
        var store = fixture.Store("tenant-a");
        var item = PendingDispatch("start-visible", dispatch);
        await store.SavePendingAsync(item);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 1)));
        var failedAt = Now.AddSeconds(1);
        var failed = dispatch.TransitionToDispatchFailed(failedAt);

        await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
            claim,
            new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "child-start-failure"),
            failed,
            ParentResume(dispatch, failedAt)));

        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, (await store.FindAsync(item.OutboxItemId))!.Status);
        Assert.Equal(WorkflowDispatchStatus.Started, fixture.ReadDispatch(dispatch.DispatchId).Status);
        Assert.Null(await store.FindAsync(ParentResume(dispatch, failedAt).OutboxItemId));
    }

    [Fact]
    public async Task Invalid_dispatch_projection_rolls_back_outbox_completion()
    {
        await using var fixture = SqliteFixture.Create();
        var dispatch = PendingDispatchRecord("parent-invalid", "activity-invalid", WorkflowDispatchMode.FireAndForget);
        fixture.SeedDispatch(dispatch);
        var store = fixture.Store("tenant-a");
        var item = PendingDispatch("start-invalid", dispatch);
        await store.SavePendingAsync(item);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 1)));
        var safeFailure = dispatch.TransitionToDispatchFailed(Now.AddSeconds(1));
        var invalid = new WorkflowDispatchRecord(
            safeFailure.DispatchId,
            safeFailure.ParentWorkflowExecutionId,
            safeFailure.ParentActivityExecutionId,
            safeFailure.ChildWorkflowExecutionId,
            safeFailure.ChildExecutable,
            safeFailure.ChildSource,
            safeFailure.Mode,
            safeFailure.Status,
            "conflicting-correlation",
            safeFailure.TenantId,
            safeFailure.Partition,
            safeFailure.RunKind,
            safeFailure.Authority,
            safeFailure.InputDescriptors,
            safeFailure.CreatedAt,
            safeFailure.UpdatedAt,
            safeFailure.Metadata,
            safeFailure.DispatchNestingDepth,
            safeFailure.TestScope);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.CompleteClaimAsync(
            new RuntimePostCommitOutboxClaimCompletion(
                claim,
                new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, Now.AddSeconds(1), "child-start-failure"),
                invalid)).AsTask());

        Assert.Equal(WorkflowDispatchStatus.Pending, fixture.ReadDispatch(dispatch.DispatchId).Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await store.FindAsync(item.OutboxItemId))!.Status);
    }

    [Fact]
    public async Task Atomic_completion_refuses_dispatch_projection_drift()
    {
        await using var fixture = SqliteFixture.Create();
        var dispatch = PendingDispatchRecord("parent-projection", "activity-projection", WorkflowDispatchMode.FireAndForget);
        fixture.SeedDispatchWithTestScopeProjection(dispatch, "forged-scope");
        var store = fixture.Store("tenant-a");
        var item = PendingDispatch("start-projection", dispatch);
        await store.SavePendingAsync(item);
        var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
            "owner-a", Now, TimeSpan.FromMinutes(1), 1)));

        await Assert.ThrowsAsync<InvalidDataException>(() => store.CompleteClaimAsync(
            new RuntimePostCommitOutboxClaimCompletion(
                claim,
                new RuntimePostCommitOutboxDeliveryResult(
                    item.OutboxItemId,
                    RuntimePostCommitOutboxStatus.FailedRetryable,
                    Now.AddSeconds(1),
                    "child-start-failure"),
                dispatch.TransitionToDispatchFailed(Now.AddSeconds(1)))).AsTask());

        Assert.Equal(WorkflowDispatchStatus.Pending, fixture.ReadDispatchContent(dispatch.DispatchId).Status);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, (await store.FindAsync(item.OutboxItemId))!.Status);
    }

    [Fact]
    public async Task Wait_exhaustion_persists_dead_letter_dispatch_and_resume_across_recreation()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-v2-outbox-wait-{Guid.NewGuid():N}.db");
        var dispatch = PendingDispatchRecord("parent-wait", "activity-wait", WorkflowDispatchMode.WaitForCompletion);
        var item = PendingDispatch("start-wait", dispatch);
        try
        {
            await using (var fixture = SqliteFixture.Create(path))
            {
                var store = fixture.Store("tenant-a");
                fixture.SeedDispatch(dispatch);
                await store.SavePendingAsync(item);
                var claim = Assert.Single(await store.ClaimAsync(new RuntimePostCommitOutboxClaimRequest(
                    "owner-a", Now, TimeSpan.FromMinutes(1), 1)));
                var failedAt = Now.AddSeconds(1);
                var failed = WorkflowDispatchLifecycle.TransitionToDispatchFailed(
                    dispatch,
                    item.OutboxItemId,
                    generation: 0,
                    attemptCount: 1,
                    firstAttemptAt: Now,
                    failedAt: failedAt);
                await store.CompleteClaimAsync(new RuntimePostCommitOutboxClaimCompletion(
                    claim,
                    new RuntimePostCommitOutboxDeliveryResult(item.OutboxItemId, RuntimePostCommitOutboxStatus.FailedRetryable, failedAt, "child-start-failure"),
                    failed,
                    ParentResume(failed, failedAt)));
            }

            await using (var fixture = SqliteFixture.Create(path))
            {
                var store = fixture.Store("tenant-a");
                Assert.Equal(RuntimePostCommitOutboxStatus.FailedFinal, (await store.FindAsync(item.OutboxItemId))!.Status);
                Assert.Equal(WorkflowDispatchStatus.DispatchFailed, fixture.ReadDispatch(dispatch.DispatchId).Status);
                var followUpId = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId)
                    .WaitFailureResumeOutboxItemId(0);
                Assert.Equal(RuntimePostCommitOutboxStatus.Pending, (await store.FindAsync(followUpId))!.Status);
            }
        }
        finally
        {
            CleanupDatabase(path);
        }
    }

    private static RuntimePostCommitOutboxItem Pending(string id, string workflowExecutionId, string kind = "test.intent", RuntimePostCommitRetryPolicy? retryPolicy = null) => new(
        id,
        new RuntimePostCommitIntent(
            $"intent-{id}",
            workflowExecutionId,
            kind,
            Now,
            null,
            null,
            null),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        null,
        retryPolicy);

    private static RuntimePostCommitOutboxItem PendingAt(
        string id,
        DateTimeOffset recordedAt,
        string workflowExecutionId,
        string kind) => new(
        id,
        new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, kind, recordedAt, null, null, null),
        RuntimePostCommitOutboxStatus.Pending,
        recordedAt,
        recordedAt);

    private static RuntimePostCommitOutboxItem PendingWithoutAvailability(string id, string workflowExecutionId) => new(
        id,
        new RuntimePostCommitIntent($"intent-{id}", workflowExecutionId, "test.intent", Now, null, null, null),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        null);

    private static WorkflowDispatchRecord PendingDispatchRecord(
        string parentWorkflowExecutionId,
        string parentActivityExecutionId,
        WorkflowDispatchMode mode)
    {
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, parentActivityExecutionId);
        return new WorkflowDispatchRecord(
            identity.DispatchId,
            parentWorkflowExecutionId,
            parentActivityExecutionId,
            identity.ChildWorkflowExecutionId,
            new WorkflowExecutableIdentity(
                $"artifact-{parentActivityExecutionId}",
                "definition-child",
                "version-child",
                "1",
                $"hash-{parentActivityExecutionId}"),
            new WorkflowExecutableSourceProvenance(
                $"source-{parentActivityExecutionId}",
                "WorkflowDefinitionVersion",
                "version-child",
                "1",
                "definition-child",
                "version-child",
                "1",
                "publication-child",
                "slot-child"),
            mode,
            WorkflowDispatchStatus.Pending,
            null,
            null,
            new WorkflowExecutionPartition(WorkflowExecutionPartition.DefaultValue),
            WorkflowRunKind.PublishedRun,
            new WorkflowExecutionAuthoritySnapshot(parentWorkflowExecutionId, "initiator-1"),
            [new WorkflowDispatchInputDescriptor("orderId", "string")],
            Now,
            Now,
            new Dictionary<string, string> { ["safe-code"] = "dispatch" });
    }

    private static RuntimePostCommitOutboxItem PendingDispatch(
        string outboxItemId,
        WorkflowDispatchRecord dispatch) => new(
        outboxItemId,
        new RuntimePostCommitIntent(
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIntentId,
            dispatch.ParentWorkflowExecutionId,
            WorkflowDispatchLifecycle.StartChildIntentKind,
            Now,
            dispatch.ParentActivityExecutionId,
            new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId).StartIdempotencyKey,
            null,
            new Dictionary<string, string> { [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId }),
        RuntimePostCommitOutboxStatus.Pending,
        Now,
        Now,
        RuntimePostCommitRetryPolicy.None);

    private static RuntimePostCommitOutboxItem ParentResume(
        WorkflowDispatchRecord dispatch,
        DateTimeOffset recordedAt)
    {
        var identity = new WorkflowDispatchIdentity(dispatch.ParentWorkflowExecutionId, dispatch.ParentActivityExecutionId);
        var generation = WorkflowDispatchLifecycle.ReadDeliveryGeneration(dispatch);
        return new RuntimePostCommitOutboxItem(
            identity.WaitFailureResumeOutboxItemId(generation),
            new RuntimePostCommitIntent(
                identity.ParentResumeIntentId,
                dispatch.ParentWorkflowExecutionId,
                WorkflowDispatchLifecycle.ResumeParentIntentKind,
                recordedAt,
                dispatch.ParentActivityExecutionId,
                identity.ParentResumeIdempotencyKey,
                null,
                new Dictionary<string, string>
                {
                    [RuntimeMetadataKeys.DispatchId] = dispatch.DispatchId,
                    [RuntimeMetadataKeys.ChildWorkflowExecutionId] = dispatch.ChildWorkflowExecutionId
                }),
            RuntimePostCommitOutboxStatus.Pending,
            recordedAt,
            recordedAt,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)));
    }

    private static WorkflowExecutionState ChildState(WorkflowDispatchRecord dispatch) => new(
        dispatch.ChildWorkflowExecutionId,
        dispatch.ChildExecutable,
        WorkflowExecutionStatus.Running,
        null,
        Now,
        Now,
        Now,
        null,
        dispatch.CorrelationId,
        dispatch.ParentWorkflowExecutionId,
        dispatch.TenantId,
        new Dictionary<string, string>())
    {
        RunKind = dispatch.RunKind,
        PinnedSource = dispatch.ChildSource,
        Partition = dispatch.Partition,
        Authority = dispatch.Authority
    };

    private static IPersistenceAccessContextAccessor Access(string scope) =>
        new TestAccessContextAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(scope)));

    private static void ApplyRuntimeUnits(IStorageProviderConnection connection)
    {
        foreach (var unitId in new[]
                 {
                     ElsaRuntimeV2StorageManifest.PostCommitOutboxDocumentKind,
                     ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind,
                     ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind
                 })
        {
            connection.Schema.Apply(ElsaRuntimeV2StorageManifest.Require(unitId));
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(IReadOnlyDictionary<string, object?> values)
    {
        var content = values[ElsaRuntimeV2StorageManifest.ContentField] switch
        {
            string text => text,
            JsonElement element => element.GetRawText(),
            _ => throw new InvalidDataException("The seeded runtime row did not contain JSON content.")
        };
        return JsonSerializer.Deserialize<T>(content, JsonOptions)
               ?? throw new InvalidDataException($"The seeded runtime row could not deserialize as {typeof(T).Name}.");
    }

    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }

    private static void CleanupDatabase(string path)
    {
        foreach (var candidate in new[] { path, $"{path}-shm", $"{path}-wal", $"{path}-journal", $"{path}.schema.lock" })
            if (File.Exists(candidate))
                File.Delete(candidate);
    }

    private static string GroundworkV2PostCommitOutboxPhysicalId(string id) =>
        RuntimePostCommitOutboxIdentity.CreateProjectionValue(id);

    private sealed class TestAccessContextAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current => current;
    }

    private sealed class NoIoSessionSource(NoIoSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public int OpenCount { get; private set; }

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            OpenCount++;
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            unitId == unit.Id.Value ? unit : ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class NoIoSession(StorageUnit unit) : IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access => StorageAccess.Scoped(new StorageScope("tenant-a"));
        public bool ReadWasCalled { get; private set; }
        public StoredEntry? Read(StorageKey key)
        {
            ReadWasCalled = true;
            return null;
        }

        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null) => throw new InvalidOperationException("I/O was not expected.");
        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }

    private sealed class DirectSessionSource(IStorageProviderConnection connection) : IGroundworkStorageSessionSource, IGroundworkStorageCapabilitySource
    {
        public IReadOnlyList<CapabilityDescriptor> Capabilities(string? targetName = null) => connection.Capabilities;

        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null) =>
            connection.OpenSession(ElsaRuntimeV2StorageManifest.Require(unitId), access);

        public IUnitOfWork BeginUnitOfWork(
            StorageAccess access,
            BatchWriteOptions options,
            IReadOnlyList<string> unitIds,
            string? targetName = null) =>
            connection.BeginUnitOfWork(
                access,
                options,
                unitIds.Select(ElsaRuntimeV2StorageManifest.Require).ToArray());

        public StorageUnit Unit(string unitId, string? targetName = null) => ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class SqliteFixture : IAsyncDisposable
    {
        private readonly IStorageProviderConnection connection;
        private bool disposed;

        private SqliteFixture(string path, IStorageProviderConnection connection)
        {
            Path = path;
            this.connection = connection;
            Source = new DirectSessionSource(connection);
            ApplyRuntimeUnits(connection);
        }

        public string Path { get; }
        public IStorageProviderConnection Connection => connection;
        private DirectSessionSource Source { get; }

        public static SqliteFixture Create() =>
            Create(System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"elsa-v2-outbox-{Guid.NewGuid():N}.db"));

        public static SqliteFixture Create(string path)
        {
            var connection = new SqliteProviderFactory().Create($"Data Source={path}");
            return new SqliteFixture(path, connection);
        }

        public GroundworkV2RuntimePostCommitOutboxStore Store(string scope) =>
            new(Source, Access(scope));

        public void SeedDispatch(WorkflowDispatchRecord record)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
            Assert.Equal(WriteOutcomeStatus.Inserted, connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Insert(GroundworkV2WorkflowDispatchStorageConventions.Values(record), WriteOptions.CreateOnly).Status);
        }

        public void SeedDispatchWithTestScopeProjection(WorkflowDispatchRecord record, string testScopeId)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
            var values = GroundworkV2WorkflowDispatchStorageConventions.Values(record).Values
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            values[ElsaRuntimeV2StorageManifest.TestScopeIdField] = testScopeId;
            Assert.Equal(
                WriteOutcomeStatus.Inserted,
                connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")))
                    .Insert(new StorageValues(values), WriteOptions.CreateOnly).Status);
        }

        public void SeedExecution(WorkflowExecutionState state)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowExecutionStateDocumentKind);
            var values = GroundworkRuntimeRowStore.Values(
                state.WorkflowExecutionId,
                ElsaRuntimeV2StorageManifest.SchemaVersion,
                Serialize(state));
            Assert.Equal(WriteOutcomeStatus.Inserted, connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Insert(values, WriteOptions.CreateOnly).Status);
        }

        public WorkflowDispatchRecord ReadDispatch(string dispatchId)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
            var entry = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId)));
            Assert.NotNull(entry);
            return GroundworkV2WorkflowDispatchStorageConventions.Deserialize(entry!.Values.Values);
        }

        public WorkflowDispatchRecord ReadDispatchContent(string dispatchId)
        {
            var unit = ElsaRuntimeV2StorageManifest.Require(ElsaRuntimeV2StorageManifest.WorkflowDispatchDocumentKind);
            var entry = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")))
                .Read(GroundworkRuntimeRowStore.Key(
                    GroundworkV2WorkflowDispatchStorageConventions.PhysicalId(dispatchId)));
            Assert.NotNull(entry);
            return Deserialize<WorkflowDispatchRecord>(entry!.Values.Values);
        }

        public ValueTask DisposeAsync()
        {
            if (!disposed)
            {
                disposed = true;
                connection.Dispose();
            }
            CleanupDatabase(Path);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class RecordingSessionSource(RecordingSession session, StorageUnit unit) : IGroundworkStorageSessionSource
    {
        public IStorageSession Open(string unitId, StorageAccess access, string? targetName = null)
        {
            Assert.Equal(unit.Id.Value, unitId);
            return session;
        }

        public IUnitOfWork BeginUnitOfWork(StorageAccess access, BatchWriteOptions options, IReadOnlyList<string> unitIds, string? targetName = null) =>
            throw new NotSupportedException();

        public StorageUnit Unit(string unitId, string? targetName = null) =>
            unitId == unit.Id.Value
                ? unit
                : ElsaRuntimeV2StorageManifest.Require(unitId);
    }

    private sealed class RecordingSession(StorageUnit unit, ICollection<QueryRequest> requests) : IStorageSession
    {
        public StorageUnit Unit { get; } = unit;
        public StorageAccess Access => StorageAccess.Scoped(new StorageScope("tenant-a"));
        public StoredEntry? Read(StorageKey key) => null;
        public QueryMaterializedResult Query(QueryRequest request, QueryRenderOptions? options = null)
        {
            requests.Add(request);
            return new QueryMaterializedResult([], null, null);
        }

        public AggregationResult Aggregate(AggregationQuery query) => throw new NotSupportedException();
        public WriteOutcome Insert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Update(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Upsert(StorageValues values, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Delete(StorageKey key, WriteOptions? options = null) => throw new NotSupportedException();
        public WriteOutcome Append(OperationId operationId, IReadOnlyList<StorageValues> values) => throw new NotSupportedException();
    }
}
