using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class RuntimeControlPlaneContractTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void ControlPlaneState_HoldsAdministrativePauseOutsideWorkflowContinuationState()
    {
        var pause = ControlPlaneHold.ForWorkflowExecution(
            holdId: "pause-1",
            workflowExecutionId: "wfexec-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance");

        var state = new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [pause],
            metadata: new Dictionary<string, string> { ["Owner"] = "RuntimeControlPlane" });

        Assert.Equal("control-1", state.ControlPlaneStateId);
        Assert.Equal("wfexec-1", state.WorkflowExecutionId);
        Assert.Single(state.ActiveHolds);
        Assert.Equal("RuntimeControlPlane", state.Metadata["Owner"]);
        Assert.DoesNotContain(nameof(ControlPlaneState), typeof(WorkflowExecutionState).GetProperties().Select(property => property.PropertyType.Name));
    }

    [Fact]
    public void WorkflowExecutionPause_DefaultsToStrictPause()
    {
        var pause = ControlPlaneHold.ForWorkflowExecution(
            holdId: "pause-1",
            workflowExecutionId: "wfexec-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance");

        Assert.Equal(ControlPlaneHoldScope.WorkflowExecution, pause.Scope);
        Assert.Equal(ControlPlaneHoldStatus.Requested, pause.Status);
        Assert.Equal(RuntimePauseContinuationPolicy.StrictPause, pause.ContinuationPolicy);
        Assert.True(pause.IsEffective);
    }

    [Fact]
    public void HostDrainPause_DefaultsToDrainInFlight()
    {
        var drain = ControlPlaneHold.ForHostDrain(
            holdId: "drain-1",
            hostId: "host-1",
            requestedAt: _now,
            requestedBy: "operator",
            reason: "shutdown");

        Assert.Equal(ControlPlaneHoldScope.HostDrain, drain.Scope);
        Assert.Equal("host-1", drain.HostId);
        Assert.Equal(RuntimePauseContinuationPolicy.DrainInFlight, drain.ContinuationPolicy);
    }

    [Theory]
    [InlineData(ControlPlaneHoldScope.WorkflowExecution)]
    [InlineData(ControlPlaneHoldScope.ActivityExecution)]
    [InlineData(ControlPlaneHoldScope.Generator)]
    [InlineData(ControlPlaneHoldScope.Ingress)]
    [InlineData(ControlPlaneHoldScope.WorkerDispatcher)]
    [InlineData(ControlPlaneHoldScope.HostDrain)]
    public void ControlPlaneHold_ValidatesRequiredTargetForScope(ControlPlaneHoldScope scope)
    {
        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneHold(
            holdId: "hold-1",
            scope: scope,
            status: ControlPlaneHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance"));

        Assert.Contains("require", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneHold_RejectsAmbiguousScopeTargets()
    {
        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneHold(
            holdId: "pause-1",
            scope: ControlPlaneHoldScope.WorkflowExecution,
            status: ControlPlaneHoldStatus.Requested,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            activityExecutionId: "actexec-1"));

        Assert.Contains("not activity-execution scoped", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneState_RejectsDuplicateHoldIds()
    {
        var hold1 = ControlPlaneHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");
        var hold2 = ControlPlaneHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");

        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [hold1, hold2]));

        Assert.Contains("duplicated", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneState_RejectsSameHoldIdAcrossActiveAndReleasedCollections()
    {
        var activeHold = ControlPlaneHold.ForWorkflowExecution("pause-1", "wfexec-1", _now, "operator", "maintenance");
        var releasedHold = new ControlPlaneHold(
            holdId: "pause-1",
            scope: ControlPlaneHoldScope.WorkflowExecution,
            status: ControlPlaneHoldStatus.Released,
            requestedAt: _now,
            requestedBy: "operator",
            reason: "maintenance",
            workflowExecutionId: "wfexec-1",
            releasedAt: _now.AddMinutes(1),
            releasedBy: "operator");

        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [activeHold],
            releasedHolds: [releasedHold]));

        Assert.Contains("more than one hold collection", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ControlPlaneState_RejectsHoldFromAnotherWorkflowWhenWorkflowScoped()
    {
        var pause = ControlPlaneHold.ForWorkflowExecution("pause-1", "wfexec-2", _now, "operator", "maintenance");

        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneState(
            controlPlaneStateId: "control-1",
            workflowExecutionId: "wfexec-1",
            activeHolds: [pause]));

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
            continuationPolicy: RuntimePauseContinuationPolicy.DrainInFlight,
            holdId: null,
            reason: " "));
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
        var exception = Assert.Throws<ArgumentException>(() => new ControlPlaneHold(
            holdId: "ingress-pause-1",
            scope: ControlPlaneHoldScope.Ingress,
            status: ControlPlaneHoldStatus.Requested,
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
    }
}
