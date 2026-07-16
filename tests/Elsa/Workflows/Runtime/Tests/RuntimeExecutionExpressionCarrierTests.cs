using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Locks the identity resolution of the ADR-0030 execution-time expression carrier helper. Correlation id and
/// instance name flow through from the caller's durable-value projection set (spec 083 review — the handlers project
/// them once via <see cref="RuntimeInputBindingStateProjection.ProjectAll"/> and pass the set in; the carrier no
/// longer reads a <c>WorkflowExecutionState</c>). The definition version is display identity for scripts
/// (<c>getWorkflowDefinitionVersion()</c>), sourced solely from the pinned executable's artifact-version major —
/// there is no system-metadata override (structurally impossible now the helper takes no state).
/// </summary>
public sealed class RuntimeExecutionExpressionCarrierTests
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    [Fact]
    public void ResolvesDefinitionVersionFromArtifactVersionMajor()
    {
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "7.3.1"), Empty);

        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void NonNumericArtifactVersionYieldsZeroRatherThanThrowing()
    {
        // Display version, not an execution precondition — a non-numeric format degrades to 0, never faults.
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "draft"), Empty);

        Assert.Equal(0, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void ProjectedIdentityFlowsThrough_AndBlankDegradesToNull()
    {
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var assigned = RuntimeExecutionExpressionCarrier.Create(Projections("corr-1", "Instance A"), pinned, Empty);
        Assert.Equal("corr-1", assigned.CorrelationId);
        Assert.Equal("Instance A", assigned.WorkflowName);

        // A blank/whitespace projection (e.g. a cleared assignment) degrades to null rather than an empty string.
        var cleared = RuntimeExecutionExpressionCarrier.Create(Projections("  ", null), pinned, Empty);
        Assert.Null(cleared.CorrelationId);
        Assert.Null(cleared.WorkflowName);
    }

    [Fact]
    public void AbsentIdentityResolvesToNullWithVersionFromPinnedExecutable()
    {
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "7.0.0"), Empty);

        Assert.Null(state.CorrelationId);
        Assert.Null(state.WorkflowName);
        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void TriggerNodeIdFlowsThrough_AndBlankDegradesToNull()
    {
        // Spec 089 D (T003): the trigger-node identity rides through the projection set onto the carrier, blank → null.
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var assigned = RuntimeExecutionExpressionCarrier.Create(Projections(triggerNodeId: "node-http"), pinned, Empty);
        Assert.Equal("node-http", assigned.TriggerNodeId);

        var blank = RuntimeExecutionExpressionCarrier.Create(Projections(triggerNodeId: "  "), pinned, Empty);
        Assert.Null(blank.TriggerNodeId);

        var absent = RuntimeExecutionExpressionCarrier.Create(Projections(), pinned, Empty);
        Assert.Null(absent.TriggerNodeId);
    }

    [Fact]
    public void ResumeInputIsCarriedOnlyWhenSupplied()
    {
        // Spec 089 D (T002): the resume input is a per-invocation carrier value, null unless the caller passes it.
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        Assert.Null(RuntimeExecutionExpressionCarrier.Create(Projections(), pinned, Empty).ResumeInput);

        var resumeInput = System.Text.Json.JsonSerializer.SerializeToElement(new { path = "/cb" });
        var carried = RuntimeExecutionExpressionCarrier.Create(Projections(), pinned, Empty, resumeInput);
        Assert.NotNull(carried.ResumeInput);
        Assert.Equal("/cb", carried.ResumeInput!.Value.GetProperty("path").GetString());
    }

    private static RuntimeInputBindingStateProjectionSet Projections(string? correlationId = null, string? instanceName = null, string? triggerNodeId = null) =>
        new(WorkflowInputs: Empty, WorkflowVariables: Empty, ActivityOutputValues: Empty, CorrelationId: correlationId, InstanceName: instanceName, StimulusInput: null, TriggerNodeId: triggerNodeId);

    private static WorkflowExecutableIdentity NewIdentity(string artifactVersion) =>
        new("artifact-1", "definition-1", "version-7", artifactVersion, "sha256:test");
}
