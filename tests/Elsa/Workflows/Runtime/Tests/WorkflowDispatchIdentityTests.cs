using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchIdentityTests
{
    [Fact]
    public void DeliveryRecoveryIdentities_AreDeterministicGenerationScopedAndValidated()
    {
        var first = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var replay = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var other = new WorkflowDispatchIdentity("parent-1", "activity-2");
        var startOutboxItemId = RuntimePostCommitOutboxItems.OutboxItemId("commit-1", first.StartIntentId);

        Assert.Equal(first.DeliveryIncidentId(0), replay.DeliveryIncidentId(0));
        Assert.Equal(first.WaitFailureResumeOutboxItemId(0), replay.WaitFailureResumeOutboxItemId(0));
        Assert.Equal($"commit-1:{first.StartIntentId}", startOutboxItemId);
        Assert.True(first.MatchesStartOutboxItemId(startOutboxItemId));
        Assert.False(first.MatchesStartOutboxItemId(
            RuntimePostCommitOutboxItems.OutboxItemId("commit-1", other.StartIntentId)));
        Assert.False(first.MatchesStartOutboxItemId("outbox-forged"));
        Assert.False(first.MatchesStartOutboxItemId($" :{first.StartIntentId}"));
        Assert.False(first.MatchesStartOutboxItemId($"commit-secret\npayload:{first.StartIntentId}"));
        Assert.False(first.MatchesStartOutboxItemId(null));
        Assert.NotEqual(first.DeliveryIncidentId(0), first.DeliveryIncidentId(1));
        Assert.NotEqual(first.WaitFailureResumeOutboxItemId(0), first.WaitFailureResumeOutboxItemId(1));
        Assert.StartsWith("incident:dispatch-delivery:v1:", first.DeliveryIncidentId(0));
        Assert.StartsWith("outbox:dispatch-failed-resume:v1:", first.WaitFailureResumeOutboxItemId(0));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.DeliveryIncidentId(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => first.WaitFailureResumeOutboxItemId(-1));
    }

    [Fact]
    public void Outbox_identifiers_compact_long_commit_and_intent_components_deterministically()
    {
        var identity = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var longCommitId = new string('c', RuntimePostCommitOutboxIdentity.MaximumLength);
        var compactedCommit = identity.ParentResumeOutboxItemId(longCommitId);
        var longIntentId = new string('i', RuntimePostCommitOutboxIdentity.MaximumLength);
        var compactedBoth = RuntimePostCommitOutboxIdentity.Create(longCommitId, longIntentId);

        Assert.True(compactedCommit.Length <= RuntimePostCommitOutboxIdentity.MaximumLength);
        Assert.EndsWith($":{identity.ParentResumeIntentId}", compactedCommit);
        Assert.True(identity.MatchesStartOutboxItemId(
            RuntimePostCommitOutboxIdentity.Create(longCommitId, identity.StartIntentId)));
        Assert.True(compactedBoth.Length <= RuntimePostCommitOutboxIdentity.MaximumLength);
        Assert.Equal(compactedBoth, RuntimePostCommitOutboxIdentity.Create(longCommitId, longIntentId));
        Assert.NotEqual(compactedBoth, RuntimePostCommitOutboxIdentity.Create(longCommitId, $"{longIntentId}x"));

        var reservedSeparator = compactedBoth.LastIndexOf(':');
        Assert.NotEqual(
            compactedBoth,
            RuntimePostCommitOutboxIdentity.Create(
                compactedBoth[..reservedSeparator],
                compactedBoth[(reservedSeparator + 1)..]));
    }

    [Fact]
    public void WaitAndResumeIdentities_AreDeterministicAndNamespaceDistinct()
    {
        var first = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var replay = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var other = new WorkflowDispatchIdentity("parent-1", "activity-2");

        Assert.Equal(first.WaitBookmarkId, replay.WaitBookmarkId);
        Assert.Equal(first.WaitStimulusHash, replay.WaitStimulusHash);
        Assert.Equal(first.ParentResumeIntentId, replay.ParentResumeIntentId);
        Assert.Equal(first.ParentResumeIdempotencyKey, replay.ParentResumeIdempotencyKey);
        Assert.DoesNotContain(first.WaitBookmarkId, new[] { first.DispatchId, first.ChildWorkflowExecutionId, first.StartIntentId });
        Assert.DoesNotContain(first.ParentResumeIntentId, new[] { first.StartIntentId, first.ParentResumeIdempotencyKey });
        Assert.NotEqual(first.WaitBookmarkId, other.WaitBookmarkId);
        Assert.StartsWith("bookmark:dispatch-wait:v1:", first.WaitBookmarkId);
        Assert.StartsWith("stimulus:dispatch-wait:v1:", first.WaitStimulusHash);
        Assert.StartsWith("intent:dispatch-resume:v1:", first.ParentResumeIntentId);
        Assert.StartsWith("dispatch-resume:v1:", first.ParentResumeIdempotencyKey);
        Assert.Equal($"commit-child-completed:{first.ParentResumeIntentId}", first.ParentResumeOutboxItemId("commit-child-completed"));
        Assert.Throws<ArgumentException>(() => first.ParentResumeOutboxItemId(" "));
    }

    [Fact]
    public void ChildCancelIdentities_AreDeterministicAndNamespaceDistinct()
    {
        var first = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var replay = new WorkflowDispatchIdentity("parent-1", "activity-1");
        var other = new WorkflowDispatchIdentity("parent-1", "activity-2");

        Assert.Equal(first.ChildCancelIntentId, replay.ChildCancelIntentId);
        Assert.Equal(first.ChildCancelIdempotencyKey, replay.ChildCancelIdempotencyKey);
        Assert.Equal(first.ChildCancelCommandId, replay.ChildCancelCommandId);
        Assert.Equal(first.ChildCancelEnvelopeId, replay.ChildCancelEnvelopeId);
        Assert.DoesNotContain(first.ChildCancelIntentId, new[] { first.StartIntentId, first.ParentResumeIntentId });
        Assert.DoesNotContain(first.ChildCancelIdempotencyKey, new[] { first.StartIdempotencyKey, first.ParentResumeIdempotencyKey });
        Assert.NotEqual(first.ChildCancelIntentId, other.ChildCancelIntentId);
        Assert.StartsWith("intent:dispatch-cancel-child:v1:", first.ChildCancelIntentId);
        Assert.StartsWith("dispatch-cancel-child:v1:", first.ChildCancelIdempotencyKey);
        Assert.StartsWith("command:dispatch-cancel-child:v1:", first.ChildCancelCommandId);
        Assert.StartsWith("envelope:dispatch-cancel-child:v1:", first.ChildCancelEnvelopeId);
        Assert.Equal($"commit-parent-cancelled:{first.ChildCancelIntentId}", first.ChildCancelOutboxItemId("commit-parent-cancelled"));
        Assert.Throws<ArgumentException>(() => first.ChildCancelOutboxItemId(" "));
    }
}
