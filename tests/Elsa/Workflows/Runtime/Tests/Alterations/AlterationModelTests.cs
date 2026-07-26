using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests.Alterations;

public sealed class AlterationModelTests
{
    [Fact]
    public void Envelope_ClonesPayloadAndRejectsInvalidIdentity()
    {
        using var document = JsonDocument.Parse("{\"value\":1}");
        var envelope = new WorkflowAlterationEnvelope("ModifyVariable", 1, document.RootElement);

        Assert.Equal("ModifyVariable", envelope.Kind);
        Assert.Equal(1, envelope.SchemaVersion);
        Assert.Equal("{\"value\":1}", envelope.Payload.GetRawText());
        Assert.Throws<ArgumentException>(() => new WorkflowAlterationEnvelope(" ", 1, document.RootElement));
        Assert.Throws<ArgumentOutOfRangeException>(() => new WorkflowAlterationEnvelope("ModifyVariable", 0, document.RootElement));
    }

    [Fact]
    public void Selector_NormalizesExplicitIdsAndRejectsAmbiguousOrEmptySelectors()
    {
        var selector = WorkflowAlterationTargetSelector.ForExecutionIds([" exec-b ", "exec-a", "exec-b"]);

        Assert.Equal(["exec-a", "exec-b"], selector.ExecutionIds);
        Assert.Throws<ArgumentException>(() => WorkflowAlterationTargetSelector.ForExecutionIds([]));
        Assert.Throws<ArgumentException>(() => new WorkflowAlterationTargetSelector(["exec-a"], new WorkflowAlterationQuerySelector(matchAllAuthorized: true)));
    }

    [Fact]
    public void PlanAndJob_EnforceLifecycleAndClaimInvariants()
    {
        var authority = new WorkflowAlterationAuthorityScope("tenant-a", "system", "operator");
        var provenance = new WorkflowAlterationOperatorProvenance("operator", "correlation");
        var plan = WorkflowAlterationPlanState.CreateCapturing(
            "plan-1", authority, provenance, "key-hash", "request-hash", new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["execution-1"]), DateTimeOffset.UnixEpoch);

        Assert.Equal(WorkflowAlterationPlanStatus.CapturingTargets, plan.Status);
        Assert.Throws<ArgumentException>(() => new WorkflowAlterationJobClaim("", "token", DateTimeOffset.UnixEpoch));
        Assert.Throws<ArgumentException>(() => new WorkflowAlterationJobState("job", "plan", "execution", "tenant-a", 0, WorkflowAlterationJobStatus.Running, null, 0, [], null, null, DateTimeOffset.UnixEpoch, null, null, 0));
    }

    [Fact]
    public void Outcome_RejectsPayloadLikeMetadataAndSerializesSafeShape()
    {
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { ["workflowExecutionId"] = "execution-1" });
        var json = JsonSerializer.Serialize(outcome);

        Assert.Contains("Cancelled", json);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.Throws<ArgumentException>(() => new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "ok", null, DateTimeOffset.UnixEpoch, new Dictionary<string, string> { ["payload"] = "secret" }));
    }
}
