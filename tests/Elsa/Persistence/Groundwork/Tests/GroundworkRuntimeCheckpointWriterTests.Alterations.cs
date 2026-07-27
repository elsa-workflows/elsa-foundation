using Elsa.Persistence.Groundwork.Stores;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Xunit;

namespace Elsa.Persistence.Groundwork.Tests;

public sealed partial class GroundworkRuntimeCheckpointWriterTests
{
    [Fact]
    public async Task Alteration_terminal_evidence_is_atomic_with_workflow_checkpoint_and_replay()
    {
        var documents = new InMemoryDocumentStore(ElsaRuntimeStorageManifest.CreatePhysicalized());
        var alterations = new GroundworkWorkflowAlterationStore(
            documents,
            GroundworkTestSerialization.Serializer,
            GroundworkTestAccess.DefaultAccessContextAccessor,
            documents);
        var plan = (await alterations.AdmitAsync(AlterationPlan())).Plan;
        var captured = await alterations.CaptureAsync(plan.PlanId, plan.Revision,
            [new WorkflowAlterationCapturedTarget("wf-1", GroundworkTestAccess.DefaultScopeValue)], null);
        await alterations.SealAsync(plan.PlanId, captured.Revision, DateTimeOffset.UnixEpoch);
        var claimed = (await alterations.ClaimNextAsync("worker", DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(1)))!;
        var outcome = new WorkflowAlterationOutcome(0, "CancelWorkflow", 1, WorkflowAlterationOutcomeStatus.Succeeded, "Cancelled", null, DateTimeOffset.UnixEpoch);
        var invalid = new WorkflowAlterationJobTerminalChange(claimed.JobId, "stale-claim", WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-alteration", DateTimeOffset.UnixEpoch);
        var baseCommit = BuildCommit("checkpoint-alteration");
        var rejectedCommit = baseCommit with
        {
            StateChanges = baseCommit.StateChanges.WithAlterationJobTerminalChange(invalid)
        };

        await Assert.ThrowsAnyAsync<Exception>(() => CreateWriter(documents).CommitAsync(rejectedCommit, Decision).AsTask());
        Assert.Null(await new GroundworkWorkflowExecutionStateStore(
            documents, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync("wf-1"));
        Assert.Equal(WorkflowAlterationJobStatus.Running, (await alterations.FindJobAsync(claimed.JobId))!.Status);

        var terminal = new WorkflowAlterationJobTerminalChange(claimed.JobId, claimed.Claim!.Token, WorkflowAlterationJobStatus.Succeeded, [outcome], "checkpoint-alteration", DateTimeOffset.UnixEpoch);
        var acceptedCommit = baseCommit with
        {
            StateChanges = baseCommit.StateChanges.WithAlterationJobTerminalChange(terminal)
        };
        var writer = CreateWriter(documents);
        await writer.CommitAsync(acceptedCommit, Decision);
        await writer.CommitAsync(acceptedCommit, Decision);

        Assert.Equal(WorkflowAlterationJobStatus.Succeeded, (await alterations.FindJobAsync(claimed.JobId))!.Status);
        Assert.NotNull(await new GroundworkWorkflowExecutionStateStore(
            documents, GroundworkTestSerialization.Serializer, GroundworkTestAccess.DefaultAccessContextAccessor).FindAsync("wf-1"));
        Assert.NotNull(await documents.LoadAsync(ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind, "checkpoint-alteration"));
    }

    private static WorkflowAlterationPlanState AlterationPlan() =>
        WorkflowAlterationPlanState.CreateCapturing(
            "checkpoint-plan",
            new WorkflowAlterationAuthorityScope(GroundworkTestAccess.DefaultScopeValue, "system", "operator"),
            new WorkflowAlterationOperatorProvenance("operator", "correlation"),
            "key",
            "canonical",
            new ProtectedWorkflowAlterationPayload("development", "A256GCM", "cipher"),
            WorkflowAlterationTargetSelector.ForExecutionIds(["wf-1"]),
            DateTimeOffset.UnixEpoch);
}
