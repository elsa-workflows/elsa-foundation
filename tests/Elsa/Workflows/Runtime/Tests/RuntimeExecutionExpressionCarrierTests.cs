using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Locks the identity resolution of the ADR-0030 execution-time expression carrier helper. Correlation id and
/// instance name flow through from the caller's durable-value projection (spec 083 review — the handlers project
/// them once and pass them in; the carrier no longer reads a <c>WorkflowExecutionState</c>). The definition version
/// is display identity for scripts (<c>getWorkflowDefinitionVersion()</c>), sourced solely from the pinned
/// executable's artifact-version major — there is no system-metadata override (none was ever written; the branch
/// inherited from #367 was dead and removed, and is now structurally impossible since the helper takes no state).
/// </summary>
public sealed class RuntimeExecutionExpressionCarrierTests
{
    private static readonly IReadOnlyDictionary<string, object?> Empty = new Dictionary<string, object?>();

    [Fact]
    public void ResolvesDefinitionVersionFromArtifactVersionMajor()
    {
        var state = Create(NewIdentity(artifactVersion: "7.3.1"));

        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void NonNumericArtifactVersionYieldsZeroRatherThanThrowing()
    {
        // Display version, not an execution precondition — a non-numeric format degrades to 0, never faults.
        var state = Create(NewIdentity(artifactVersion: "draft"));

        Assert.Equal(0, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void ProjectedIdentityFlowsThrough_AndBlankDegradesToNull()
    {
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var assigned = RuntimeExecutionExpressionCarrier.Create("corr-1", "Instance A", pinned, Empty, Empty, Empty);
        Assert.Equal("corr-1", assigned.CorrelationId);
        Assert.Equal("Instance A", assigned.WorkflowName);

        // A blank/whitespace projection (e.g. a cleared assignment) degrades to null rather than an empty string.
        var cleared = RuntimeExecutionExpressionCarrier.Create("  ", null, pinned, Empty, Empty, Empty);
        Assert.Null(cleared.CorrelationId);
        Assert.Null(cleared.WorkflowName);
    }

    [Fact]
    public void AbsentIdentityResolvesToNullWithVersionFromPinnedExecutable()
    {
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var state = RuntimeExecutionExpressionCarrier.Create(null, null, pinned, Empty, Empty, Empty);

        Assert.Null(state.CorrelationId);
        Assert.Null(state.WorkflowName);
        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    private static RuntimeExecutionExpressionCarrierState Create(WorkflowExecutableIdentity pinned) =>
        RuntimeExecutionExpressionCarrier.Create(correlationId: null, instanceName: null, pinned, Empty, Empty, Empty);

    private static WorkflowExecutableIdentity NewIdentity(string artifactVersion) =>
        new("artifact-1", "definition-1", "version-7", artifactVersion, "sha256:test");
}
