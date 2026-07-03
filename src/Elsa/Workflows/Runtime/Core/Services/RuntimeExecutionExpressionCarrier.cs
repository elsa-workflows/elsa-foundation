using System.Globalization;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Services;

/// <summary>
/// Builds the live execution-time expression carrier state (ADR 0030) that populates a
/// <see cref="SimpleActivityExecutionContext"/> so that expressions evaluated <em>during</em> activity execution
/// (a <c>Run JavaScript</c> resume callback, a composite container's child-completion logic, a leaf invoke) read
/// correct workflow identity, inputs, variables, and prior activity outputs through
/// <see cref="Elsa.Workflows.Runtime.Core.Contracts.IExecutionExpressionState"/>.
/// </summary>
/// <remarks>
/// This is the single home for the carrier's identity resolution — the same logic every scheduler work handler
/// that constructs an execution-time context needs — so the invoke, resume, and parent-completion paths stay in
/// lockstep rather than each re-deriving it. All state is Runtime-owned (the durable-value projections and the
/// pinned executable identity); nothing here touches <c>Elsa.Workflows.Design.*</c>, so the carrier remains
/// Design-free (constitution §E2.2 / §E2.6). Correlation id and instance name are projected from the
/// <see cref="RuntimeMetadataKeys.IdentityName"/>-tagged durable values every handler already re-lists
/// (spec 083 review: <see cref="RuntimeIdentityStateProjection"/>), so a <c>Correlate</c>/<c>SetName</c> in one
/// branch is visible to a concurrent sibling branch and no path pays a per-invocation workflow-execution-state
/// read. Every input is best-effort: absent identity degrades to null and the version to the pinned
/// artifact-version major rather than faulting the activity.
/// </remarks>
public static class RuntimeExecutionExpressionCarrier
{
    /// <summary>
    /// Assembles the carrier state from the identity projected off the durable values (correlation id / instance
    /// name), the pinned executable identity, and the caller's already-computed durable-value projections for
    /// inputs/variables/outputs. Callers pass projections they already hold (each handler projects them once for
    /// its input-binding resolution context) so nothing is projected twice.
    /// </summary>
    public static RuntimeExecutionExpressionCarrierState Create(
        string? correlationId,
        string? instanceName,
        WorkflowExecutableIdentity pinnedExecutable,
        IReadOnlyDictionary<string, object?> workflowInputs,
        IReadOnlyDictionary<string, object?> workflowVariables,
        IReadOnlyDictionary<string, object?> activityOutputValues)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);
        ArgumentNullException.ThrowIfNull(workflowInputs);
        ArgumentNullException.ThrowIfNull(workflowVariables);
        ArgumentNullException.ThrowIfNull(activityOutputValues);

        return new RuntimeExecutionExpressionCarrierState(
            CorrelationId: string.IsNullOrWhiteSpace(correlationId) ? null : correlationId,
            WorkflowName: string.IsNullOrWhiteSpace(instanceName) ? null : instanceName,
            WorkflowDefinitionVersion: ResolveWorkflowDefinitionVersion(pinnedExecutable),
            WorkflowInputs: workflowInputs,
            WorkflowVariables: workflowVariables,
            ActivityOutputValues: activityOutputValues);
    }

    // Resolves the workflow definition version for the execution-time expression carrier (ADR 0030) from the pinned
    // executable's artifact-version major — artifact-only identity (§E2.6), independent of any workflow-execution-state
    // read (spec 083 follow-up). A non-numeric value yields 0 (the default the accessor returned before this unit)
    // rather than faulting the activity — the version is display identity for scripts, not an execution precondition.
    private static int ResolveWorkflowDefinitionVersion(WorkflowExecutableIdentity pinnedExecutable)
    {
        var majorVersion = pinnedExecutable.ArtifactVersion.Split('.', 2)[0];
        return int.TryParse(majorVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var version) ? version : 0;
    }
}

/// <summary>
/// The populated execution-time expression carrier values (ADR 0030): identity plus the durable-value
/// projections for inputs, variables, and prior activity outputs. Spread into the
/// <see cref="SimpleActivityExecutionContext"/> constructor's carrier parameters by each scheduler work handler.
/// </summary>
public readonly record struct RuntimeExecutionExpressionCarrierState(
    string? CorrelationId,
    string? WorkflowName,
    int WorkflowDefinitionVersion,
    IReadOnlyDictionary<string, object?> WorkflowInputs,
    IReadOnlyDictionary<string, object?> WorkflowVariables,
    IReadOnlyDictionary<string, object?> ActivityOutputValues);
