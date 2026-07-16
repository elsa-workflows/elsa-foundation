using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowDispatchIdentityTests
{
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
}
