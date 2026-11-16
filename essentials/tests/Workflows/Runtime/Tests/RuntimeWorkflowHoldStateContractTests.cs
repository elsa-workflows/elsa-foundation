using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeWorkflowHoldStateContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void WorkflowHoldState_HoldsAdministrativePauseOutsideWorkflowContinuationState()
    {
        var pause = WorkflowHold.ForWorkflowExecution(
            holdId: "pause-1",
            workflowExecutionId: "wfexec-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance");

        var state = new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [pause],
            metadata: new Dictionary<string, string> { ["Owner"] = "RuntimeControlPlane" });

        Assert.Equal("control-1", state.ControlPlaneStateId);
        Assert.Equal("wfexec-1", state.WorkflowExecutionId);
        Assert.Single(state.ActiveHolds);
        Assert.Equal("RuntimeControlPlane", state.Metadata["Owner"]);
        Assert.DoesNotContain(
            typeof(WorkflowExecutionState).GetProperties(),
            property => property.PropertyType.ToString().Contains(nameof(WorkflowHoldState), StringComparison.Ordinal));
    }

    [Fact]
    public void WorkflowExecutionPause_DefaultsToStrictPause()
    {
        var pause = WorkflowHold.ForWorkflowExecution(
            holdId: "pause-1",
            workflowExecutionId: "wfexec-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance");

        Assert.Equal(WorkflowHoldScope.WorkflowExecution, pause.Scope);
        Assert.Equal(WorkflowHoldStatus.Requested, pause.Status);
        Assert.Equal(RuntimePauseContinuationPolicy.StrictPause, pause.ContinuationPolicy);
        Assert.True(pause.IsEffective);
    }

    [Fact]
    public void HostDrainPause_DefaultsToDrainInFlight()
    {
        var drain = WorkflowHold.ForHostDrain(
            holdId: "drain-1",
            hostId: "host-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "shutdown");

        Assert.Equal(WorkflowHoldScope.HostDrain, drain.Scope);
        Assert.Equal("host-1", drain.HostId);
        Assert.Equal(RuntimePauseContinuationPolicy.DrainInFlight, drain.ContinuationPolicy);
    }

    [Theory]
    [InlineData(WorkflowHoldScope.WorkflowExecution)]
    [InlineData(WorkflowHoldScope.ActivityExecution)]
    [InlineData(WorkflowHoldScope.Generator)]
    [InlineData(WorkflowHoldScope.Ingress)]
    [InlineData(WorkflowHoldScope.WorkerDispatcher)]
    [InlineData(WorkflowHoldScope.HostDrain)]
    public void ControlPlaneHold_ValidatesRequiredTargetForScope(WorkflowHoldScope scope)
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHold(
            holdId: "hold-1",
            scope: scope,
            status: WorkflowHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance"));

        Assert.Contains("require", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneHold_RejectsAmbiguousScopeTargets()
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHold(
            holdId: "pause-1",
            scope: WorkflowHoldScope.WorkflowExecution,
            status: WorkflowHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1"));

        Assert.Contains("not activity-execution scoped", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneHold_RejectsNotPausedContinuationPolicy()
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHold(
            holdId: "pause-1",
            scope: WorkflowHoldScope.WorkflowExecution,
            status: WorkflowHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            continuationPolicy: RuntimePauseContinuationPolicy.NotPaused));

        Assert.Contains("pause continuation policy", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("continuationPolicy", exception.ParamName);
    }

    [Fact]
    public void ControlPlaneState_RejectsDuplicateHoldIds()
    {
        var hold1 = WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");
        var hold2 = WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [hold1, hold2]));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneState_RejectsSameHoldIdAcrossActiveAndReleasedCollections()
    {
        var activeHold = WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");
        var releasedHold = new WorkflowHold(
            holdId: "pause-1",
            scope: WorkflowHoldScope.WorkflowExecution,
            status: WorkflowHoldStatus.Released,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            releasedAt: _now.AddMinutes(1),
            releasedBy: "operator");

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [activeHold],
            releasedHolds: [releasedHold]));

        Assert.Contains("more than one hold collection", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Null(exception.ParamName);
    }

    [Fact]
    public void ControlPlaneState_RejectsReleasedHoldInActiveCollection()
    {
        var releasedHold = new WorkflowHold(
            holdId: "pause-1",
            scope: WorkflowHoldScope.WorkflowExecution,
            status: WorkflowHoldStatus.Released,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            releasedAt: _now.AddMinutes(1),
            releasedBy: "operator");

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [releasedHold]));

        Assert.Contains("Active holds", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("activeHolds", exception.ParamName);
    }

    [Fact]
    public void ControlPlaneState_RejectsEffectiveHoldInReleasedCollection()
    {
        var activeHold = WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            releasedHolds: [activeHold]));

        Assert.Contains("Released holds", exception.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("releasedHolds", exception.ParamName);
    }

    [Fact]
    public void ControlPlaneState_RejectsHoldFromAnotherWorkflowWhenWorkflowScoped()
    {
        var pause = WorkflowHold.ForWorkflowExecution("pause-1", "wfexec-2", _now, "operator", "maintenance");

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [pause]));

        Assert.Contains("same workflow execution", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneState_RejectsGlobalHoldWhenWorkflowScoped()
    {
        var ingress = new WorkflowHold(
            holdId: "ingress-pause-1",
            scope: WorkflowHoldScope.Ingress,
            status: WorkflowHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            ingressSourceId: "http-endpoint-1",
            ingressPolicy: IngressPausePolicy.DefaultFor(RuntimeIngressSourceKind.HttpEndpoint));

        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHoldState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [ingress]));

        Assert.Contains("same workflow execution", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void SchedulerPauseDecision_NamesSafeBoundaryAndBlocksAdvancement()
    {
        var decision = new SchedulerPauseDecision(
            canAdvance: false,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: "pause-1",
            reason: "Workflow execution paused",
            metadata: new Dictionary<string, string> { ["Command"] = "PauseWorkflowExecution" });

        Assert.False(decision.CanAdvance);
        Assert.Equal(RuntimePauseBoundary.BeforeActivityExecutionStart, decision.Boundary);
        Assert.Equal(RuntimePauseContinuationPolicy.StrictPause, decision.ContinuationPolicy);
        Assert.Equal("pause-1", decision.HoldId);
        Assert.Equal("PauseWorkflowExecution", decision.Metadata["Command"]);
    }

    [Fact]
    public void SchedulerPauseDecision_RejectsBlockedDecisionWithoutHoldAndReason()
    {
        Assert.Throws<ArgumentException>(() => new SchedulerPauseDecision(
            canAdvance: false,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: null,
            reason: "Workflow execution paused"));

        Assert.Throws<ArgumentException>(() => new SchedulerPauseDecision(
            canAdvance: false,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: "pause-1",
            reason: " "));
    }

    [Fact]
    public void SchedulerPauseDecision_RejectsBlankReasonWhenProvidedForAllowedDecision()
    {
        Assert.Throws<ArgumentException>(() => new SchedulerPauseDecision(
            canAdvance: true,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
            holdId: null,
            reason: " "));
    }

    [Fact]
    public void SchedulerPauseDecision_RequiresMeaningfulContinuationPolicyForDecisionState()
    {
        Assert.Throws<ArgumentException>(() => new SchedulerPauseDecision(
            canAdvance: true,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.StrictPause,
            holdId: null,
            reason: null));

        Assert.Throws<ArgumentException>(() => new SchedulerPauseDecision(
            canAdvance: false,
            boundary: RuntimePauseBoundary.BeforeActivityExecutionStart,
            continuationPolicy: RuntimePauseContinuationPolicy.NotPaused,
            holdId: "pause-1",
            reason: "Paused"));
    }

    [Fact]
    public void PauseBoundaries_IncludeOnlyNamedSafeBoundaries()
    {
        var boundaries = Enum.GetNames<RuntimePauseBoundary>();

        Assert.Contains(nameof(RuntimePauseBoundary.BeforeActivityExecutionStart), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.AfterActivityCompletedPropagationDrains), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.AfterCheckpointCommit), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.EnteringDurableSuspension), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.RegisteringVolatileWait), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.BeforeGeneratorEmission), boundaries);
        Assert.Contains(nameof(RuntimePauseBoundary.BeforePostCommitDispatch), boundaries);
        Assert.DoesNotContain(boundaries, boundary => boundary.Contains("Mid", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(RuntimeIngressSourceKind.HttpEndpoint, IngressPauseBehavior.Reject, 503)]
    [InlineData(RuntimeIngressSourceKind.Timer, IngressPauseBehavior.SkipWhilePaused, null)]
    [InlineData(RuntimeIngressSourceKind.MessageQueue, IngressPauseBehavior.StopFetchingOrLocking, null)]
    [InlineData(RuntimeIngressSourceKind.RequestResponseWebhook, IngressPauseBehavior.Reject, 503)]
    [InlineData(RuntimeIngressSourceKind.DurableEventStream, IngressPauseBehavior.PauseSubscriptionOrOffset, null)]
    [InlineData(RuntimeIngressSourceKind.ManualApiStart, IngressPauseBehavior.Reject, 503)]
    public void IngressPausePolicy_ProvidesSourceSpecificDefaults(RuntimeIngressSourceKind sourceKind, IngressPauseBehavior behavior, int? statusCode)
    {
        var policy = IngressPausePolicy.DefaultFor(sourceKind);

        Assert.Equal(sourceKind, policy.SourceKind);
        Assert.Equal(behavior, policy.Behavior);
        Assert.Equal(statusCode, policy.ResponseStatusCode);
    }

    [Fact]
    public void IngressPauseHold_RequiresIngressPolicy()
    {
        var exception = Assert.Throws<ArgumentException>(() => new WorkflowHold(
            holdId: "ingress-pause-1",
            scope: WorkflowHoldScope.Ingress,
            status: WorkflowHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            ingressSourceId: "http-endpoint-1"));

        Assert.Contains("ingress pause policy", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RuntimeCommandTerminology_DistinguishesPauseUnpauseResumeAndContinue()
    {
        var names = Enum.GetNames<WorkflowExecutionCommandKind>();

        Assert.Contains(nameof(WorkflowExecutionCommandKind.PauseWorkflowExecution), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.UnpauseWorkflowExecution), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.ResumeBookmark), names);
        Assert.Contains(nameof(WorkflowExecutionCommandKind.ContinueVolatileWait), names);
        Assert.DoesNotContain(names, name => name.Contains("ResumeWorkflowExecution", StringComparison.Ordinal));
    }

    [Fact]
    public void RuntimeCommandTerminology_PreservesExistingCommandKindOrdinals()
    {
        Assert.Equal(0, (int)WorkflowExecutionCommandKind.Start);
        Assert.Equal(1, (int)WorkflowExecutionCommandKind.ResumeBookmark);
        Assert.Equal(2, (int)WorkflowExecutionCommandKind.ContinueVolatileWait);
        Assert.Equal(3, (int)WorkflowExecutionCommandKind.RunSchedulerWork);
        Assert.Equal(4, (int)WorkflowExecutionCommandKind.Cancel);
        Assert.Equal(5, (int)WorkflowExecutionCommandKind.PauseWorkflowExecution);
        Assert.Equal(6, (int)WorkflowExecutionCommandKind.UnpauseWorkflowExecution);
        Assert.Equal(7, (int)WorkflowExecutionCommandKind.ScheduleActivity);
        Assert.Equal(8, (int)WorkflowExecutionCommandKind.CompleteActivity);
        Assert.Equal(9, (int)WorkflowExecutionCommandKind.DeliverSignal);
        Assert.Equal(10, (int)WorkflowExecutionCommandKind.CreateBookmark);
        Assert.Equal(11, (int)WorkflowExecutionCommandKind.Checkpoint);
        Assert.Equal(12, (int)WorkflowExecutionCommandKind.StartActivity);
        Assert.Equal(13, (int)WorkflowExecutionCommandKind.InvokeActivity);
    }
}
