using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeVolatileWaitPolicyTests
{
    [Fact]
    public void DefaultPolicy_AllowsVolatileWaitWhenHostSupportsInMemoryContinuation()
    {
        var policy = new DefaultRuntimeVolatileWaitPolicy();
        var request = NewRequest(hostSupportsInMemoryContinuation: true);

        var decision = policy.Decide(request);

        Assert.True(decision.IsAllowed);
        Assert.Null(decision.Reason);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.MaximumDuration);
        Assert.Equal(VolatileWaitHostShutdownBehavior.CancelWait, decision.HostShutdownBehavior);
        Assert.Equal(VolatileWaitCancellationBehavior.CompleteWithCancellationResult, decision.CancellationBehavior);
        Assert.Equal(VolatileWaitDurableFallbackPolicy.AllowedWhenBookmarkDeclared, decision.DurableFallbackPolicy);
        Assert.Equal(nameof(DefaultRuntimeVolatileWaitPolicy), decision.Metadata["runtime.volatileWait.policy"]);
        Assert.Equal("wfexec-1", decision.Metadata["runtime.volatileWait.workflowExecutionId"]);
        Assert.Equal("actexec-1", decision.Metadata["runtime.volatileWait.activityExecutionId"]);
        Assert.Equal("timer", decision.Metadata["runtime.volatileWait.awaitableKind"]);
    }

    [Fact]
    public void DefaultPolicy_DeniesVolatileWaitWhenHostDoesNotSupportInMemoryContinuation()
    {
        var policy = new DefaultRuntimeVolatileWaitPolicy();
        var request = NewRequest(hostSupportsInMemoryContinuation: false);

        var decision = policy.Decide(request);

        Assert.False(decision.IsAllowed);
        Assert.Contains("Host does not support", decision.Reason);
        Assert.Equal(TimeSpan.FromSeconds(5), decision.MaximumDuration);
        Assert.Equal(VolatileWaitDurableFallbackPolicy.AllowedWhenBookmarkDeclared, decision.DurableFallbackPolicy);
    }

    [Fact]
    public void DefaultPolicy_DoesNotEmitDurableResumeOrCallbackMetadata()
    {
        var policy = new DefaultRuntimeVolatileWaitPolicy();

        var decision = policy.Decide(NewRequest(hostSupportsInMemoryContinuation: true));

        Assert.DoesNotContain(decision.Metadata.Keys, key =>
            key.Contains("bookmark", StringComparison.OrdinalIgnoreCase)
            || key.Contains("resume", StringComparison.OrdinalIgnoreCase)
            || key.Contains("callback", StringComparison.OrdinalIgnoreCase));
    }

    private static RuntimeVolatileWaitPolicyRequest NewRequest(bool hostSupportsInMemoryContinuation) =>
        new(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1",
            branchId: "branch-a",
            awaitableKind: "timer",
            requestedDuration: TimeSpan.FromSeconds(5),
            hostSupportsInMemoryContinuation: hostSupportsInMemoryContinuation,
            requestedHostShutdownBehavior: VolatileWaitHostShutdownBehavior.CancelWait,
            requestedCancellationBehavior: VolatileWaitCancellationBehavior.CompleteWithCancellationResult,
            durableFallbackPolicy: VolatileWaitDurableFallbackPolicy.AllowedWhenBookmarkDeclared);
}
