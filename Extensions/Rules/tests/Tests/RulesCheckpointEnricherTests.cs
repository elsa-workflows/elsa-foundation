using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Rules.Core;
using Elsa.Rules.Workflows.Runtime.Activities;
using Elsa.Rules.Workflows.Runtime.Models;
using Elsa.Rules.Workflows.Runtime.Services;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;

namespace Elsa.Rules.Tests;

public sealed class RulesCheckpointEnricherTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-22T10:00:00Z");
    private static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string>
    {
        ["existing"] = "keep"
    };

    [Fact]
    public async Task Bookmark_creation_freezes_the_actual_bookmark_and_registration_in_the_same_change_set()
    {
        var activity = Activity();
        var bookmark = Bookmark(activity);
        var unrelated = OtherActivity();
        var unrelatedBookmark = new BookmarkState("other-bookmark", "workflow-1", "other-activity", "other-node",
            "other-resume", "other-stimulus", new string('f', 64), null, Metadata, Now, null);
        var changes = StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata),
                new(unrelated.InvocationId, RuntimeStateChangeOperation.Upsert, unrelated, Metadata)],
            [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata),
                new(unrelatedBookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, unrelatedBookmark, Metadata)],
            includeUnrelatedState: true);
        var commit = Commit(RuntimeCheckpointNames.BookmarkCreated, changes);

        var enriched = await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(commit);

        var intent = Assert.Single(enriched.PostCommitIntents);
        Assert.Equal(RulesNativeBoundary.StartKind, intent.Kind);
        Assert.Equal(Now, intent.RecordedAt);
        var native = RulesNativeBoundary.Read<RulesNativeIntent>(intent.Payload!.Value);
        var expected = RulesNativeBoundary.CaptureAddress(activity, bookmark);
        Assert.Equal("bookmark-actual-id", native.Destination.BookmarkId);
        Assert.Equal("registration-actual-id", native.Destination.RegistrationId);
        Assert.NotEqual(native.Destination.BookmarkId, native.Destination.RegistrationId);
        Assert.Equal(expected, native.Destination);

        var enrichedActivity = Assert.Single(enriched.StateChanges.ActivityExecutions, change => change.StateId == activity.InvocationId).State;
        Assert.Equal("keep", enrichedActivity.Metadata["existing"]);
        Assert.Equal(expected, RulesNativeBoundary.ReadAddress(enrichedActivity));
        Assert.Equal(RulesNativeBoundary.Write(expected).GetRawText(), enrichedActivity.Metadata[RulesNativeBoundary.AddressMetadataKey]);
        Assert.Same(unrelated, Assert.Single(enriched.StateChanges.ActivityExecutions, change => change.StateId == unrelated.InvocationId).State);
        Assert.Same(unrelatedBookmark, Assert.Single(enriched.StateChanges.Bookmarks, change => change.StateId == unrelatedBookmark.BookmarkId).State);
        AssertUnchangedStateChangeFields(changes, enriched.StateChanges);
    }

    [Fact]
    public async Task Bookmark_creation_is_idempotent_for_the_same_frozen_address_and_start_intent()
    {
        var activity = Activity();
        var bookmark = Bookmark(activity);
        var request = RulesNativeBoundary.ReadState(activity);
        var address = RulesNativeBoundary.CaptureAddress(activity, bookmark);
        var frozen = activity with { Metadata = WithAddress(activity.Metadata, address) };
        var existingIntent = RulesNativeBoundary.Intent(request, address, cancel: false, Now);
        var commit = Commit(RuntimeCheckpointNames.BookmarkCreated,
            StateChanges([new(frozen.InvocationId, RuntimeStateChangeOperation.Upsert, frozen, Metadata)],
                [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)]), [existingIntent]);

        var enriched = await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(commit);

        Assert.Same(existingIntent, Assert.Single(enriched.PostCommitIntents));
        Assert.Same(frozen, Assert.Single(enriched.StateChanges.ActivityExecutions).State);
    }

    [Fact]
    public async Task Bookmark_creation_rejects_a_changed_frozen_address_or_conflicting_start_intent()
    {
        var activity = Activity();
        var originalBookmark = Bookmark(activity);
        var originalAddress = RulesNativeBoundary.CaptureAddress(activity, originalBookmark);
        var frozen = activity with { Metadata = WithAddress(activity.Metadata, originalAddress) };
        const string replacementBookmarkId = "bookmark-replaced";
        var changedActivity = frozen with { BookmarkIds = [replacementBookmarkId] };
        var changedBookmark = Bookmark(changedActivity, replacementBookmarkId);
        var changedAddressCommit = Commit(RuntimeCheckpointNames.BookmarkCreated,
            StateChanges([new(changedActivity.InvocationId, RuntimeStateChangeOperation.Upsert, changedActivity, Metadata)],
                [new(changedBookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, changedBookmark, Metadata)]));

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(changedAddressCommit));

        var request = RulesNativeBoundary.ReadState(activity);
        var actualIntent = RulesNativeBoundary.Intent(request, originalAddress, cancel: false, Now);
        var conflictingIntent = new RuntimePostCommitIntent(actualIntent.IntentId, actualIntent.WorkflowExecutionId,
            actualIntent.Kind, Now.AddTicks(1), actualIntent.ActivityExecutionId, actualIntent.IdempotencyKey,
            actualIntent.Payload);
        var conflictingIntentCommit = Commit(RuntimeCheckpointNames.BookmarkCreated,
            StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)],
                [new(originalBookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, originalBookmark, Metadata)]),
            [conflictingIntent]);

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(conflictingIntentCommit));
    }

    [Fact]
    public async Task Invalid_round_budget_still_records_native_start_without_creating_a_rules_row()
    {
        var activity = Activity(maxRounds: 0);
        var bookmark = Bookmark(activity);

        var enriched = await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(
            Commit(RuntimeCheckpointNames.BookmarkCreated,
                StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)],
                    [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)])));

        var intent = Assert.Single(enriched.PostCommitIntents);
        var payload = RulesNativeBoundary.Read<RulesNativeIntent>(intent.Payload!.Value);
        Assert.Equal(RulesNativeBoundary.StartKind, intent.Kind);
        Assert.True(RulesNativeBoundary.HasInvalidRoundLimit(payload.Request));
        Assert.Equal("registration-actual-id", payload.Destination.RegistrationId);
        Assert.Equal("bookmark-actual-id", payload.Destination.BookmarkId);
    }

    [Fact]
    public async Task Cancellation_uses_frozen_address_after_consumed_trigger_payload_is_retired()
    {
        var activity = Activity();
        var start = await BookmarkCheckpointAsync(activity);
        var frozen = Assert.Single(start.StateChanges.ActivityExecutions).State;
        var expectedAddress = RulesNativeBoundary.ReadAddress(frozen);
        var retired = frozen with
        {
            Status = ActivityExecutionStatus.Cancelled,
            BookmarkIds = [],
            TriggerRegistrations = [],
            TriggerDeliveries = [new("delivery-1", expectedAddress.RegistrationId, RulesNativeBoundary.CompletionType,
                ValueEnvelope.Absent(RulesNativeBoundary.CompletionType, ValueProtectionPolicy.InstanceInline),
                RulesNativeBoundary.ProviderId, Now.AddMinutes(1), "stable-result-key", ActivityTriggerDeliveryStatus.Consumed)]
        };

        var cancelled = await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(
            Commit("ActivityCancelled", StateChanges([new(retired.InvocationId, RuntimeStateChangeOperation.Upsert, retired, Metadata)], [])));

        var intent = Assert.Single(cancelled.PostCommitIntents);
        var payload = RulesNativeBoundary.Read<RulesNativeIntent>(intent.Payload!.Value);
        Assert.Equal(RulesNativeBoundary.CancelKind, intent.Kind);
        Assert.Equal(expectedAddress, payload.Destination);
        Assert.Equal(RulesNativeBoundary.ReadState(activity).RunId, payload.Request.RunId);
    }

    [Fact]
    public async Task Parent_cancellation_reads_all_pages_and_emits_cancel_for_persisted_rules_state()
    {
        var activity = Activity();
        var start = await BookmarkCheckpointAsync(activity);
        var persisted = Assert.Single(start.StateChanges.ActivityExecutions).State;
        var unrelated = OtherActivity();
        var store = new ActivityStore([unrelated, persisted]);
        var parent = new WorkflowExecutionState("workflow-1", new("executable-1", "definition-1", "version-1", "1", new string('a', 64)),
            WorkflowExecutionStatus.Cancelled, null, Now, Now, Now, Now, null, null, "tenant-1", Metadata);

        var cancelled = await new RulesCheckpointEnricher(store).EnrichAsync(
            Commit("ParentCancelled", StateChanges([], [], workflowExecution:
                new("workflow-1", RuntimeStateChangeOperation.Upsert, parent, Metadata))));

        var intent = Assert.Single(cancelled.PostCommitIntents);
        var payload = RulesNativeBoundary.Read<RulesNativeIntent>(intent.Payload!.Value);
        Assert.Equal(RulesNativeBoundary.CancelKind, intent.Kind);
        Assert.Equal(RulesNativeBoundary.ReadAddress(persisted), payload.Destination);
        Assert.Equal(2, store.PageReads);
    }

    [Fact]
    public async Task Parent_cancellation_includes_changed_nonterminal_rules_activity()
    {
        var activity = Activity();
        var start = await BookmarkCheckpointAsync(activity);
        var persisted = Assert.Single(start.StateChanges.ActivityExecutions).State;
        var changed = persisted with { Status = ActivityExecutionStatus.Running };
        var parent = new WorkflowExecutionState("workflow-1", new("executable-1", "definition-1", "version-1", "1", new string('a', 64)),
            WorkflowExecutionStatus.Cancelled, null, Now, Now, Now, Now, null, null, "tenant-1", Metadata);

        var cancelled = await new RulesCheckpointEnricher(new ActivityStore([persisted])).EnrichAsync(
            Commit("ParentCancelled", StateChanges(
                [new(changed.InvocationId, RuntimeStateChangeOperation.Upsert, changed, Metadata)], [],
                workflowExecution: new("workflow-1", RuntimeStateChangeOperation.Upsert, parent, Metadata))));

        var intent = Assert.Single(cancelled.PostCommitIntents);
        var payload = RulesNativeBoundary.Read<RulesNativeIntent>(intent.Payload!.Value);
        Assert.Equal(RulesNativeBoundary.CancelKind, intent.Kind);
        Assert.Equal(RulesNativeBoundary.ReadAddress(persisted), payload.Destination);
    }

    [Fact]
    public async Task Cancellation_fails_closed_when_private_rules_state_has_lost_its_frozen_address()
    {
        var activity = Activity() with { Status = ActivityExecutionStatus.Cancelled };

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(
                Commit("ActivityCancelled", StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)], []))));
    }

    [Fact]
    public async Task Cancellation_before_private_state_exists_has_no_rules_intent()
    {
        var activity = Activity(includePrivateState: false) with { Status = ActivityExecutionStatus.Cancelled };

        var enriched = await new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(
            Commit("ActivityCancelled", StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)], [])));

        Assert.Empty(enriched.PostCommitIntents);
    }

    private static ValueTask<RuntimeCheckpointCommit> BookmarkCheckpointAsync(ActivityExecutionState activity)
    {
        var bookmark = Bookmark(activity);
        return new RulesCheckpointEnricher(new ActivityStore()).EnrichAsync(
            Commit(RuntimeCheckpointNames.BookmarkCreated,
                StateChanges([new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)],
                    [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)])));
    }

    private static ActivityExecutionState Activity(
        int maxRounds = 4,
        string bookmarkId = "bookmark-actual-id",
        string registrationId = "registration-actual-id",
        bool includePrivateState = true)
    {
        var sample = RuleTestData.Invocation();
        var program = new RuleWorkflowProgramPin(1, new string('c', 64), sample.Artifact,
            sample.ExpectedImplementation, vocabularyCount: 7);
        var request = new RunRulesRequest(new RuleScope("tenant-1", "partition-1"),
            RulesNativeBoundary.RunId(new("tenant-1", "partition-1"), "workflow-1", "activity-1"), program,
            [new RuleFact("seed")], maxRounds, "workflow-1", "activity-1", "executable-1", new string('a', 64),
            "node-1", "resume-actual-id");
        var state = new RunRulesState(RulesNativeBoundary.FormatVersion, RulesNativeBoundary.Write(request));
        var privateState = includePrivateState
            ? new ActivityPrivateState("activity-1", 1,
                ValueEnvelope.Inline(new(typeof(RunRulesState).FullName!, schemaVersion: 1),
                    JsonSerializer.SerializeToElement(state), ValueProtectionPolicy.InstanceInline), "attempt-1", Now)
            : null;
        return new(new("activity-1", "workflow-1", "node-1", "authored-1", RunRules.ActivityType, "1"),
            ActivityExecutionStatus.Suspended, "TriggerWaiting", 1, Now, Now, null, null, "parent-1", null, null,
            ActivitySchedulingProvenance.Empty, 1, [bookmarkId], [], 0, 0, Metadata,
            PrivateState: privateState,
            TriggerRegistrations: includePrivateState
                ? [new(registrationId, "activity-1", request.ResumeTargetId, RulesNativeBoundary.CompletionType,
                    RulesNativeBoundary.StimulusType, RulesNativeBoundary.StimulusHash(request), ActivityTriggerDeduplicationPolicy.Once)]
                : []);
    }

    private static ActivityExecutionState OtherActivity() =>
        new(new("other-activity", "workflow-1", "other-node", "authored-other", "test.activity", "1"),
            ActivityExecutionStatus.Completed, null, 1, Now, Now, Now, null, "parent-1", null, null,
            ActivitySchedulingProvenance.Empty, 1, [], [], 0, 0, Metadata);

    private static BookmarkState Bookmark(ActivityExecutionState activity, string? bookmarkId = null)
    {
        var request = RulesNativeBoundary.ReadState(activity);
        return new(bookmarkId ?? activity.BookmarkIds.Single(), request.WorkflowExecutionId, request.ActivityExecutionId,
            request.NodeId, request.ResumeTargetId, RulesNativeBoundary.StimulusType,
            RulesNativeBoundary.StimulusHash(request), null, Metadata, Now, null);
    }

    private static IReadOnlyDictionary<string, string> WithAddress(
        IReadOnlyDictionary<string, string> metadata,
        RuleNativeResultAddress address)
    {
        var result = metadata.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal);
        result[RulesNativeBoundary.AddressMetadataKey] = RulesNativeBoundary.Write(address).GetRawText();
        return result;
    }

    private static RuntimeCheckpointStateChangeSet StateChanges(
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionState>> activityChanges,
        IReadOnlyCollection<RuntimeStateChange<BookmarkState>> bookmarkChanges,
        RuntimeStateChange<WorkflowExecutionState>? workflowExecution = null,
        bool includeUnrelatedState = false)
    {
        IReadOnlyCollection<RuntimeStateChange<DurableValueState>> durableValues = includeUnrelatedState
            ? new[] { new RuntimeStateChange<DurableValueState>("durable-1", RuntimeStateChangeOperation.Upsert,
                new("durable-1", "workflow-1", "value-1", new("String", null, null), DurableValueLifecycle.None,
                    DurableValueStorage.None, null, null, null, Now, Metadata), Metadata) }
            : Array.Empty<RuntimeStateChange<DurableValueState>>();
        IReadOnlyCollection<RuntimeStateChange<IncidentState>> incidents = includeUnrelatedState
            ? new[] { new RuntimeStateChange<IncidentState>("incident-1", RuntimeStateChangeOperation.Upsert,
                new("incident-1", "workflow-1", null, null, IncidentSeverity.Warning, IncidentStatus.Open,
                    null, "test.failure", "unrelated", Now, null, Metadata), Metadata) }
            : Array.Empty<RuntimeStateChange<IncidentState>>();
        IReadOnlyCollection<RuntimeStateChange<ExecutionLivenessState>> operational = includeUnrelatedState
            ? new[] { new RuntimeStateChange<ExecutionLivenessState>("liveness-1", RuntimeStateChangeOperation.Upsert,
                new("liveness-1", "workflow-1", null, null, null, null, ["pending-intent"], Metadata), Metadata) }
            : Array.Empty<RuntimeStateChange<ExecutionLivenessState>>();
        IReadOnlyCollection<RuntimeStateChange<ActivityExecutionInspectionProjection>> inspection = includeUnrelatedState
            ? [new RuntimeStateChange<ActivityExecutionInspectionProjection>("other-activity", RuntimeStateChangeOperation.Upsert,
                ActivityExecutionInspectionProjection.FromState(OtherActivity(), "checkpoint-old", Now), Metadata)]
            : Array.Empty<RuntimeStateChange<ActivityExecutionInspectionProjection>>();
        IReadOnlyCollection<RuntimeStateChange<RuntimePostCommitOutboxItem>> outbox = includeUnrelatedState
            ? [new RuntimeStateChange<RuntimePostCommitOutboxItem>("other-outbox", RuntimeStateChangeOperation.Upsert,
                new("other-outbox", new("other-intent", "workflow-1", "test.other", Now, null, null, null),
                    RuntimePostCommitOutboxStatus.Pending, Now, Now), Metadata)]
            : Array.Empty<RuntimeStateChange<RuntimePostCommitOutboxItem>>();
        IReadOnlyCollection<ActivityScopeCleanupRequest> cleanups = includeUnrelatedState
            ? [new ActivityScopeCleanupRequest("workflow-1", "scope-1", ["other-activity"], [], [], [])]
            : Array.Empty<ActivityScopeCleanupRequest>();
        IReadOnlyCollection<WorkflowDispatchCancellationRequest> dispatchCancellations = includeUnrelatedState
            ? [CreateDispatchCancellation()]
            : Array.Empty<WorkflowDispatchCancellationRequest>();
        IReadOnlyCollection<ConsumedSchedulerWorkItem> consumedSchedulerWork = includeUnrelatedState
            ? [new ConsumedSchedulerWorkItem("workflow-1", "scheduler-item", "worker-1", 1)]
            : Array.Empty<ConsumedSchedulerWorkItem>();
        var alteration = includeUnrelatedState
            ? new WorkflowAlterationJobTerminalChange("job-1", "claim-1", WorkflowAlterationJobStatus.Succeeded, [],
                "checkpoint-old", Now)
            : null;
        var backgroundWork = includeUnrelatedState
            ? new BackgroundActivityWorkCheckpointChanges(cancellations: [new BackgroundActivityWorkCancellationRequest("workflow-1", ["other-activity"])])
            : new BackgroundActivityWorkCheckpointChanges();

        return new(workflowExecution,
            includeUnrelatedState
                ? new RuntimeStateChange<SchedulerState>("workflow-1", RuntimeStateChangeOperation.Upsert,
                    new SchedulerState("workflow-1", 42), Metadata)
                : null,
            activityChanges,
            bookmarkChanges,
            durableValues,
            incidents,
            operational,
            workflowDispatches: [],
            activityExecutionInspections: inspection,
            postCommitOutbox: outbox,
            activityScopeCleanups: cleanups,
            workflowDispatchCancellations: dispatchCancellations,
            consumedSchedulerWorkItems: consumedSchedulerWork,
            alterationJobTerminalChange: alteration,
            backgroundActivityWork: backgroundWork);
    }

    private static WorkflowDispatchCancellationRequest CreateDispatchCancellation()
    {
        var identity = new WorkflowDispatchIdentity("parent-workflow", "parent-activity");
        return new(identity.DispatchId, "parent-workflow", "parent-activity", identity.ChildWorkflowExecutionId, Now);
    }

    private static RuntimeCheckpointCommit Commit(
        string name,
        RuntimeCheckpointStateChangeSet stateChanges,
        IReadOnlyList<RuntimePostCommitIntent>? intents = null) =>
        new("commit-1", new("checkpoint-1", name, "workflow-1", Now,
            stateChanges.ActivityExecutions.Select(change => change.StateId).ToArray(), Metadata),
            stateChanges, intents ?? [], Metadata);

    private static void AssertUnchangedStateChangeFields(
        RuntimeCheckpointStateChangeSet before,
        RuntimeCheckpointStateChangeSet after)
    {
        Assert.Same(before.WorkflowExecution, after.WorkflowExecution);
        Assert.Same(before.Scheduler, after.Scheduler);
        Assert.Same(before.ActivityExecutionInspections, after.ActivityExecutionInspections);
        Assert.Same(before.Bookmarks, after.Bookmarks);
        Assert.Same(before.DurableValues, after.DurableValues);
        Assert.Same(before.Incidents, after.Incidents);
        Assert.Same(before.Operational, after.Operational);
        Assert.Same(before.ActivityScopeCleanups, after.ActivityScopeCleanups);
        Assert.Same(before.WorkflowDispatches, after.WorkflowDispatches);
        Assert.True(WorkflowDispatchCancellationRequest.Equivalent(
            Assert.Single(before.WorkflowDispatchCancellations), Assert.Single(after.WorkflowDispatchCancellations)));
        Assert.Equal(before.ConsumedSchedulerWorkItems, after.ConsumedSchedulerWorkItems);
        Assert.Equal(before.AlterationJobTerminalChange?.JobId, after.AlterationJobTerminalChange?.JobId);
        Assert.Equal(before.AlterationJobTerminalChange?.ClaimToken, after.AlterationJobTerminalChange?.ClaimToken);
        Assert.Equal(before.AlterationJobTerminalChange?.Status, after.AlterationJobTerminalChange?.Status);
        Assert.Equal(before.AlterationJobTerminalChange?.CheckpointCommitId, after.AlterationJobTerminalChange?.CheckpointCommitId);
        Assert.Equal(before.AlterationJobTerminalChange?.CompletedAt, after.AlterationJobTerminalChange?.CompletedAt);
        Assert.Same(before.BackgroundActivityWork, after.BackgroundActivityWork);
        Assert.Same(before.PostCommitOutbox, after.PostCommitOutbox);
    }

    private sealed class ActivityStore(IReadOnlyList<ActivityExecutionState>? activities = null) : IActivityExecutionStateStore
    {
        private readonly IReadOnlyList<ActivityExecutionState> _activities = activities ?? [];
        public int PageReads { get; private set; }

        public ValueTask<ActivityExecutionState> SaveAsync(ActivityExecutionState state, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(state);

        public ValueTask<ActivityExecutionState?> FindAsync(string workflowExecutionId, string activityExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<ActivityExecutionState?>(_activities.SingleOrDefault(state => state.InvocationId == activityExecutionId));

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListPageAsync(
            ActivityExecutionStatePageQuery query,
            CancellationToken cancellationToken = default)
        {
            PageReads++;
            var request = query.ContinuationToken;
            var page = request switch
            {
                null => new RuntimeStorePage<ActivityExecutionState>(query, _activities.Take(1).ToArray(), _activities.Count > 1 ? "next" : null),
                "next" => new RuntimeStorePage<ActivityExecutionState>(query, _activities.Skip(1).ToArray()),
                _ => throw new InvalidOperationException("Unexpected activity-state continuation token.")
            };
            return ValueTask.FromResult(page);
        }

        public ValueTask<RuntimeStorePage<ActivityExecutionState>> ListByParentPageAsync(
            ActivityExecutionStateParentPageQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
