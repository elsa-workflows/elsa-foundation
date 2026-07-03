using System.Globalization;
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
/// lockstep rather than each re-deriving it. All state is Runtime-owned (the workflow-execution state, the pinned
/// executable identity, the durable-value projections); nothing here touches <c>Elsa.Workflows.Design.*</c>, so
/// the carrier remains Design-free (constitution §E2.2 / §E2.6). Every input is best-effort: a null
/// <paramref name="workflowState"/> degrades identity to null (correlation id / name) and version to the pinned
/// artifact-version major rather than faulting the activity, mirroring the invoke path.
/// </remarks>
public static class RuntimeExecutionExpressionCarrier
{
    /// <summary>
    /// Assembles the carrier state from the workflow-execution state, the pinned executable identity, and the
    /// caller's already-computed durable-value projections. Callers pass projections they already hold (each
    /// handler projects them once for its input-binding resolution context) so nothing is projected twice.
    /// </summary>
    public static RuntimeExecutionExpressionCarrierState Create(
        WorkflowExecutionState? workflowState,
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
            CorrelationId: workflowState?.CorrelationId,
            WorkflowName: workflowState is null ? null : ResolveInstanceName(workflowState),
            WorkflowDefinitionVersion: ResolveWorkflowDefinitionVersion(pinnedExecutable),
            WorkflowInputs: workflowInputs,
            WorkflowVariables: workflowVariables,
            ActivityOutputValues: activityOutputValues);
    }

    /// <summary>
    /// Best-effort load of the workflow-execution state used to resolve carrier identity (correlation id / name /
    /// definition version). Every scheduler work handler that builds an execution-time context needs the same load,
    /// so it lives here rather than being re-derived at each site. Returns null when no <see
    /// cref="IWorkflowExecutionStateStore"/> is registered or the state is absent, degrading identity to null rather
    /// than faulting the activity — matching the invoke, resume, and parent-completion paths.
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

    // Resolves the current workflow instance name for the execution-time expression carrier (ADR 0030) from the
    // same system-metadata key SetName writes (see WorkflowInvokeActivitySchedulerWorkHandler.ApplyInstanceName).
    // Null when no name has been assigned.
    private static string? ResolveInstanceName(WorkflowExecutionState workflowState) =>
        workflowState.SystemMetadata.TryGetValue(RuntimeMetadataKeys.InstanceName, out var name) && !string.IsNullOrWhiteSpace(name)
            ? name
            : null;

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
    IReadOnlyDictionary<string, object?> ActivityOutputValues);
