using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

/// <summary>
/// Locks the identity resolution of the ADR-0030 execution-time expression carrier helper. The definition version
/// is display identity for scripts (<c>getWorkflowDefinitionVersion()</c>), sourced solely from the pinned
/// executable's artifact-version major — there is no system-metadata override (none was ever written; the branch
/// inherited from #367 was dead and removed). These tests keep that sourcing honest and guard against the override
/// being reintroduced.
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
    public void SystemMetadataDefinitionVersionKeyIsIgnored()
    {
        // Regression guard: the retired "runtime.definitionVersion" override key has no writer and must not be
        // honored — the version still comes from the artifact-version major, not the seeded metadata.
        var workflowState = NewWorkflowState(NewIdentity(artifactVersion: "7.0.0"), systemMetadata: new Dictionary<string, string>
        {
            ["runtime.definitionVersion"] = "42"
        });

        var state = RuntimeExecutionExpressionCarrier.Create(workflowState, workflowState.PinnedExecutable, Empty, Empty, Empty);

        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    [Fact]
    public void NullWorkflowStateStillResolvesVersionFromPinnedExecutable()
    {
        var pinned = NewIdentity(artifactVersion: "7.0.0");

        var state = RuntimeExecutionExpressionCarrier.Create(null, pinned, Empty, Empty, Empty);

        Assert.Null(state.CorrelationId);
        Assert.Null(state.WorkflowName);
        Assert.Equal(7, state.WorkflowDefinitionVersion);
    }

    private static RuntimeExecutionExpressionCarrierState Create(WorkflowExecutableIdentity pinned) =>
        RuntimeExecutionExpressionCarrier.Create(NewWorkflowState(pinned), pinned, Empty, Empty, Empty);

    private static WorkflowExecutableIdentity NewIdentity(string artifactVersion) =>
        new("artifact-1", "definition-1", "version-7", artifactVersion, "sha256:test");

    private static WorkflowExecutionState NewWorkflowState(WorkflowExecutableIdentity pinned, IReadOnlyDictionary<string, string>? systemMetadata = null) =>
        new(
            WorkflowExecutionId: "wfexec-1",
            PinnedExecutable: pinned,
            Status: WorkflowExecutionStatus.Running,
            SubStatus: null,
            CreatedAt: default,
            StartedAt: default,
            UpdatedAt: default,
            CompletedAt: null,
            CorrelationId: "corr-1",
            ParentWorkflowExecutionId: null,
            TenantId: null,
            SystemMetadata: systemMetadata ?? new Dictionary<string, string> { [RuntimeMetadataKeys.InstanceName] = "Instance A" });
}
