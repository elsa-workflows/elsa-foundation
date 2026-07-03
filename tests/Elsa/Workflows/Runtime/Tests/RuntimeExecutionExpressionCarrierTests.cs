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
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "7.3.1"));

        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void NonNumericArtifactVersionYieldsZeroRatherThanThrowing()
    {
        // Display version, not an execution precondition — a non-numeric format degrades to 0, never faults.
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "draft"));

        Assert.Equal(0, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void ProjectedIdentityFlowsThrough_AndBlankDegradesToNull()
    {
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var assigned = RuntimeExecutionExpressionCarrier.Create(Projections("corr-1", "Instance A"), pinned);
        Assert.Equal("corr-1", assigned.CorrelationId);
        Assert.Equal("Instance A", assigned.WorkflowName);

        // A blank/whitespace projection (e.g. a cleared assignment) degrades to null rather than an empty string.
        var cleared = RuntimeExecutionExpressionCarrier.Create(Projections("  ", null), pinned);
        Assert.Null(cleared.CorrelationId);
        Assert.Null(cleared.WorkflowName);
    }

    [Fact]
    public void AbsentIdentityResolvesToNullWithVersionFromPinnedExecutable()
    {
        var state = RuntimeExecutionExpressionCarrier.Create(Projections(), NewIdentity(artifactVersion: "7.0.0"));

        Assert.Null(state.CorrelationId);
        Assert.Null(state.WorkflowName);
        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    private static RuntimeInputBindingStateProjectionSet Projections(string? correlationId = null, string? instanceName = null) =>
        new(WorkflowInputs: Empty, WorkflowVariables: Empty, ActivityOutputValues: Empty, CorrelationId: correlationId, InstanceName: instanceName);

    private static WorkflowExecutableIdentity NewIdentity(string artifactVersion) =>
        new("artifact-1", "definition-1", "version-7", artifactVersion, "sha256:test");
}
