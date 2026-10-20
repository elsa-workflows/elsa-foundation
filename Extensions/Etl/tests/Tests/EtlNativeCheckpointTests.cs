using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Workflows.Runtime.Activities;
using Elsa.Etl.Workflows.Runtime.Models;
using Elsa.Etl.Workflows.Runtime.Services;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;

namespace Elsa.Etl.Tests;

public sealed class EtlNativeCheckpointTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-10-20T18:00:00Z");
    private static readonly IReadOnlyDictionary<string, string> Metadata = new Dictionary<string, string>();
    private readonly InMemoryActivityExecutionStateStore _activities = new();

    [Fact]
    public async Task Suspension_alone_does_not_admit_until_the_real_bookmark_is_committed()
    {
        var activity = Activity();
        var enricher = new EtlCheckpointEnricher(_activities);
        var suspended = await enricher.EnrichAsync(Commit(activity, RuntimeCheckpointNames.ActivitySuspended));
        Assert.Empty(suspended.PostCommitIntents);

        var created = await enricher.EnrichAsync(Commit(activity, RuntimeCheckpointNames.BookmarkCreated, Bookmark(activity)));
        var intent = Assert.Single(created.PostCommitIntents);
        var invocation = EtlRuntimeJson.Read<EtlInvocation>(intent.Payload!.Value, EtlNativeBoundary.MaximumRequestBytes);
        Assert.Equal(EtlNativeBoundary.StartKind, intent.Kind);
        Assert.Equal("actual-durable-registration-not-an-attempt-formula", invocation.ResultAddress.BookmarkId);
        Assert.Equal(invocation.ResultAddress.BookmarkId, invocation.ResultAddress.RegistrationId);
        Assert.Equal("node-1:etl-completed", invocation.ResultAddress.ResumeTargetId);
        Assert.Equal(typeof(RunEtlCompletion).FullName, invocation.ResultAddress.PayloadTypeAlias);
        Assert.Equal(EtlNativeResultAddress.CompletionProviderId, invocation.ResultAddress.ProviderId);
        Assert.Equal("once", invocation.ResultAddress.DeduplicationPolicy);
        Assert.Equal(Now, intent.RecordedAt);
        Assert.Single((await enricher.EnrichAsync(created)).PostCommitIntents);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("activity")]
    [InlineData("node")]
    [InlineData("bookmark")]
    [InlineData("resume")]
    [InlineData("stimulus-type")]
    [InlineData("stimulus-hash")]
    public async Task A_changed_bookmark_address_cannot_create_an_admission_intent(string field)
    {
        var activity = Activity();
        var original = Bookmark(activity);
        var changed = field switch
        {
            "workflow" => original with { WorkflowExecutionId = "other-workflow" },
            "activity" => original with { ActivityExecutionId = "other-activity" },
            "node" => original with { ExecutableNodeId = "other-node" },
            "bookmark" => original with { BookmarkId = "other-bookmark" },
            "resume" => original with { ResumeTargetId = "other-target" },
            "stimulus-type" => original with { StimulusType = "other-type" },
            _ => original with { StimulusHash = "other-hash" }
        };
        var commit = Commit(activity, RuntimeCheckpointNames.BookmarkCreated, changed);
        if (field == "activity")
        {
            // An unrelated bookmark is ignored, rather than attributed to an ETL activity.
            Assert.Empty((await new EtlCheckpointEnricher(_activities).EnrichAsync(commit)).PostCommitIntents);
            return;
        }
        await Assert.ThrowsAsync<InvalidDataException>(async () => await new EtlCheckpointEnricher(_activities).EnrichAsync(commit));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_before_bookmark_creation_freezes_the_actual_registration_address(bool wholeParent)
    {
        var activity = Activity();
        await _activities.SaveAsync(activity);
        var commit = wholeParent
            ? Commit(null, "ParentCancelled", wholeParentCancelled: true)
            : Commit(activity with { Status = ActivityExecutionStatus.Cancelled }, "SubtreeCancelled");

        var enriched = await new EtlCheckpointEnricher(_activities).EnrichAsync(commit);

        var intent = Assert.Single(enriched.PostCommitIntents);
        Assert.Equal(EtlNativeBoundary.CancelKind, intent.Kind);
        var invocation = EtlRuntimeJson.Read<EtlInvocation>(intent.Payload!.Value, EtlNativeBoundary.MaximumRequestBytes);
        Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(EtlNativeBoundary.Invocation(activity)), EtlRuntimeIdentity.InvocationFingerprint(invocation));
        Assert.Empty(commit.StateChanges.Bookmarks);
    }

    [Fact]
    public async Task Cancellation_uses_the_checkpoint_snapshot_even_if_the_store_still_has_an_older_attempt()
    {
        var older = Activity();
        await _activities.SaveAsync(older);
        var request = EtlNativeBoundary.ReadState(older);
        var changedRequest = request with { RunId = "not-the-stable-run-id" };
        var changedState = new RunEtlState(1, EtlRuntimeJson.WriteElement(changedRequest));
        var invalid = older with
        {
            Status = ActivityExecutionStatus.Cancelled,
            PrivateState = new(older.InvocationId, 1, ValueEnvelope.Inline(new(typeof(RunEtlState).FullName!, schemaVersion: 1),
                JsonSerializer.SerializeToElement(changedState), ValueProtectionPolicy.InstanceInline), "attempt-2", Now)
        };
        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new EtlCheckpointEnricher(_activities).EnrichAsync(Commit(invalid, "SubtreeCancelled")));
    }

    [Fact]
    public void Native_private_state_preserves_the_explicit_null_tenant_and_opaque_partition()
    {
        var activity = Activity(new(null, "partition-\uD800"));
        var json = JsonSerializer.Serialize(activity.PrivateState);
        var restored = activity with { PrivateState = JsonSerializer.Deserialize<ActivityPrivateState>(json) };
        var invocation = EtlNativeBoundary.Invocation(restored);

        Assert.Null(invocation.Scope.TenantId);
        Assert.Equal("partition-\uD800", invocation.Scope.PersistencePartition);
        Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(EtlNativeBoundary.Invocation(activity)), EtlRuntimeIdentity.InvocationFingerprint(invocation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Cancellation_after_typed_claim_uses_the_consumed_receipt_even_after_payload_retirement(bool retired)
    {
        var original = Activity();
        var invocation = EtlNativeBoundary.Invocation(original);
        var type = EtlNativeBoundary.CompletionType;
        var terminal = new EtlTerminalResult("result-1", EtlTerminalOutcome.Cancelled, 0, 0, 0, null, Now);
        var payload = retired
            ? ValueEnvelope.Absent(type, ValueProtectionPolicy.InstanceInline)
            : ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(new RunEtlCompletion(1,
                EtlRuntimeJson.WriteElement(invocation), EtlRuntimeJson.WriteElement(terminal))), ValueProtectionPolicy.InstanceInline);
        var claimed = original with
        {
            Status = ActivityExecutionStatus.Cancelled,
            TriggerRegistrations = [],
            TriggerDeliveries = [new("delivery-1", invocation.ResultAddress.RegistrationId, type, payload,
                EtlNativeResultAddress.CompletionProviderId, Now, "stable-result-key", ActivityTriggerDeliveryStatus.Consumed)]
        };

        var commit = await new EtlCheckpointEnricher(_activities).EnrichAsync(Commit(claimed, "ActivityCancelled"));

        var intent = Assert.Single(commit.PostCommitIntents);
        Assert.Equal(EtlNativeBoundary.CancelKind, intent.Kind);
        var recovered = EtlRuntimeJson.Read<EtlInvocation>(intent.Payload!.Value, EtlNativeBoundary.MaximumRequestBytes);
        Assert.Equal(EtlRuntimeIdentity.InvocationFingerprint(invocation), EtlRuntimeIdentity.InvocationFingerprint(recovered));
    }

    [Fact]
    public async Task A_missing_registration_without_a_consumed_receipt_cannot_be_reconstructed()
    {
        var retired = Activity() with { Status = ActivityExecutionStatus.Cancelled, TriggerRegistrations = [] };

        await Assert.ThrowsAsync<InvalidDataException>(async () =>
            await new EtlCheckpointEnricher(_activities).EnrichAsync(Commit(retired, "ActivityCancelled")));
    }

    internal static ActivityExecutionState Activity(EtlScope? scope = null)
    {
        scope ??= new("tenant-a", "partition-a");
        var pipeline = EtlArtifactJson.Write(EtlTestPipelines.Create());
        var hash = EtlArtifactJson.Hash(pipeline);
        var artifact = new EtlArtifact(1, EtlArtifactJson.CreateArtifactId(1, "orders", 1, hash), hash, "orders", 1, pipeline);
        var runId = EtlNativeBoundary.RunId(scope, "workflow-1", "activity-1");
        var request = new RunEtlRequest(scope, runId, artifact, "workflow-1", "activity-1", "executable-1", new('a', 64), "node-1");
        var state = new RunEtlState(1, EtlRuntimeJson.WriteElement(request));
        return new(new("activity-1", "workflow-1", "node-1", "authored-1", RunEtl.ActivityType, "1"),
            ActivityExecutionStatus.Suspended, "TriggerWaiting", 1, Now, Now, null, null, "parent-sequence", null, null,
            ActivitySchedulingProvenance.Empty, 1, [], [], 0, 0, Metadata,
            PrivateState: new("activity-1", 1, ValueEnvelope.Inline(new(typeof(RunEtlState).FullName!, schemaVersion: 1),
                JsonSerializer.SerializeToElement(state), ValueProtectionPolicy.InstanceInline), "attempt-1", Now),
            TriggerRegistrations: [new("actual-durable-registration-not-an-attempt-formula", "activity-1", "node-1:etl-completed",
                new(typeof(RunEtlCompletion).FullName!, schemaVersion: 1), EtlNativeBoundary.StimulusType, runId, ActivityTriggerDeduplicationPolicy.Once)]);
    }

    internal static BookmarkState Bookmark(ActivityExecutionState activity)
    {
        var address = EtlNativeBoundary.Invocation(activity).ResultAddress;
        return new(address.BookmarkId, address.WorkflowExecutionId, address.ActivityExecutionId, address.NodeId,
            address.ResumeTargetId, address.StimulusType, address.StimulusHash, null, Metadata, Now, null);
    }

    private static RuntimeCheckpointCommit Commit(ActivityExecutionState? activity, string name, BookmarkState? bookmark = null, bool wholeParentCancelled = false)
    {
        var parent = new WorkflowExecutionState("workflow-1", new("executable-1", "definition-1", "version-1", "1", new('a', 64)),
            WorkflowExecutionStatus.Cancelled, null, Now, Now, Now, Now, null, null, "tenant-a", Metadata);
        return new("commit-1", new("checkpoint-1", name, "workflow-1", Now, activity is null ? [] : [activity.InvocationId], Metadata),
            new(wholeParentCancelled ? new("workflow-1", RuntimeStateChangeOperation.Upsert, parent, Metadata) : null,
                null, activity is null ? [] : [new(activity.InvocationId, RuntimeStateChangeOperation.Upsert, activity, Metadata)],
                bookmark is null ? [] : [new(bookmark.BookmarkId, RuntimeStateChangeOperation.Upsert, bookmark, Metadata)], [], [], []), [], Metadata);
    }
}
