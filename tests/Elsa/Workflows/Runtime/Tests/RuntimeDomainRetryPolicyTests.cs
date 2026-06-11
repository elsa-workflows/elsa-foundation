using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeDomainRetryPolicyTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 16, 0, 0, TimeSpan.Zero);

    [Fact]
    public void NoopPolicy_ReturnsExplicitDoNotRetryDecision()
    {
        var policy = new NoopRuntimeDomainRetryPolicy();
        var request = NewRequest(activityExecutionId: "actexec-1");

        var decision = policy.Decide(request);

        Assert.Equal(RuntimeDomainRetryMode.DoNotRetry, decision.Mode);
        Assert.Null(decision.Delay);
        Assert.Contains("does not retry", decision.Reason);
        Assert.Equal(nameof(NoopRuntimeDomainRetryPolicy), decision.Metadata["runtime.domainRetry.policy"]);
        Assert.Equal("wfexec-1", decision.Metadata["runtime.domainRetry.workflowExecutionId"]);
        Assert.Equal("actexec-1", decision.Metadata["runtime.domainRetry.activityExecutionId"]);
    }

    [Fact]
    public void NoopPolicy_OmitsActivityMetadataForWorkflowLevelFailures()
    {
        var policy = new NoopRuntimeDomainRetryPolicy();

        var decision = policy.Decide(NewRequest(activityExecutionId: null));

        Assert.Equal(RuntimeDomainRetryMode.DoNotRetry, decision.Mode);
        Assert.DoesNotContain("runtime.domainRetry.activityExecutionId", decision.Metadata.Keys);
    }

    [Fact]
    public void RuntimeApiFeature_RegistersOverridableDefaultPolicy()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IRuntimeDomainRetryPolicy>(new CustomRuntimeDomainRetryPolicy());

        new WorkflowsRuntimeApiFeature().ConfigureServices(services);

        using var provider = services.BuildServiceProvider();
        Assert.IsType<CustomRuntimeDomainRetryPolicy>(provider.GetRequiredService<IRuntimeDomainRetryPolicy>());
    }

    [Fact]
    public void OperationalRecoveryCandidate_RemainsSeparateFromDomainRetryDecision()
    {
        var policy = new NoopRuntimeDomainRetryPolicy();
        var candidate = new RuntimeRecoveryCandidate(
            workflowExecutionId: "wfexec-1",
            operationalStateId: "operational-1",
            lastCheckpointId: "checkpoint-1",
            reason: RuntimeInterruptionReason.LeaseLost,
            detectedAt: _now,
            requeueFromLastCheckpoint: true);

        var decision = policy.Decide(NewRequest(activityExecutionId: "actexec-1"));

        Assert.True(candidate.RequeueFromLastCheckpoint);
        Assert.Equal(RuntimeInterruptionReason.LeaseLost, candidate.Reason);
        Assert.Equal(RuntimeDomainRetryMode.DoNotRetry, decision.Mode);
        Assert.DoesNotContain(
            candidate.GetType().GetProperties().Select(property => property.Name),
            name => name.Contains("Retry", StringComparison.OrdinalIgnoreCase));
    }

    private RuntimeDomainRetryRequest NewRequest(string? activityExecutionId) =>
        new(
            workflowExecutionId: "wfexec-1",
            activityExecutionId: activityExecutionId,
            failureType: "ActivityFault",
            failureCount: 1,
            requestedAt: _now);

    private sealed class CustomRuntimeDomainRetryPolicy : IRuntimeDomainRetryPolicy
    {
        public RuntimeDomainRetryDecision Decide(RuntimeDomainRetryRequest request) =>
            new(
                mode: RuntimeDomainRetryMode.Fault,
                delay: null,
                reason: "Custom policy owns domain retry decisions.");
    }
}
