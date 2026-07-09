using System.Globalization;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

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
/// Design-free (constitution §E2.2 / §E2.6). Correlation id and instance name come from the durable-value identity
/// projection every handler already computes (spec 083 review: <see cref="RuntimeIdentityStateProjection"/>), so a
/// <c>Correlate</c>/<c>SetName</c> in one branch is visible to a concurrent sibling branch and no path pays a
/// per-invocation workflow-execution-state read for the carrier. Every input is best-effort: absent identity
/// degrades to null and the version to the pinned artifact-version major rather than faulting the activity.
/// </remarks>
public static class RuntimeExecutionExpressionCarrier
{
    /// <summary>
    /// Assembles the carrier state from the durable-value projection set (identity + inputs/variables/outputs, all
    /// projected once from the handler's single <c>ListAsync</c>) and the pinned executable identity. Nothing is
    /// projected twice — the caller passes the <see cref="RuntimeInputBindingStateProjectionSet"/> it already holds.
    /// </summary>
    public static RuntimeExecutionExpressionCarrierState Create(
        RuntimeInputBindingStateProjectionSet projections,
        WorkflowExecutableIdentity pinnedExecutable,
        JsonElement? resumeInput = null)
    {
        ArgumentNullException.ThrowIfNull(pinnedExecutable);

        return new RuntimeExecutionExpressionCarrierState(
            CorrelationId: string.IsNullOrWhiteSpace(projections.CorrelationId) ? null : projections.CorrelationId,
            WorkflowName: string.IsNullOrWhiteSpace(projections.InstanceName) ? null : projections.InstanceName,
            WorkflowDefinitionVersion: ResolveWorkflowDefinitionVersion(pinnedExecutable),
            WorkflowInputs: projections.WorkflowInputs,
            WorkflowVariables: projections.WorkflowVariables,
            ActivityOutputValues: projections.ActivityOutputValues,
            StimulusInput: projections.StimulusInput,
            TriggerNodeId: string.IsNullOrWhiteSpace(projections.TriggerNodeId) ? null : projections.TriggerNodeId,
            ResumeInput: resumeInput?.Clone());
    }

    /// <summary>
    /// Best-effort load of the workflow-execution state for the paths that still need it — the invoke handler's
    /// control-leaf change (Finish/Correlate/SetName mutates the authoritative queryable state). Carrier identity no
    /// longer uses this (it projects from durable values), so a plain activity invocation never calls it. Returns
    /// null when no <see cref="IWorkflowExecutionStateStore"/> is registered or the state is absent.
    /// </summary>
    public static async ValueTask<WorkflowExecutionState?> LoadWorkflowStateAsync(
        IServiceProvider serviceProvider,
        string workflowExecutionId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        var workflowExecutionStateStore = serviceProvider.GetService<IWorkflowExecutionStateStore>();
        return workflowExecutionStateStore is null
            ? null
            : await workflowExecutionStateStore.FindAsync(workflowExecutionId, cancellationToken);
    }

    // Resolves the workflow definition version for the execution-time expression carrier (ADR 0030) from the pinned
    // executable's artifact-version major. A non-numeric value yields 0 (the default the accessor returned before
    // this unit) rather than faulting the activity — the version is display identity for scripts, not an execution
    // precondition, so a display-version format must not throw.
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
    IReadOnlyDictionary<string, object?> ActivityOutputValues,
    object? StimulusInput,
    string? TriggerNodeId,
    JsonElement? ResumeInput);
