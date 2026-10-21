using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Runtime.Core.Contracts;

/// <summary>
/// Projects a live structural activity's private state into a read-only view for operators (spec 260 runtime seam R-6,
/// the activity state view). A module registers one projector per private-state type alias; the runtime API serves the
/// view at <c>runtime/workflows/instances/{workflowExecutionId}/activity-executions/{activityExecutionId}/state-view</c>
/// after it has checked the run, the caller's grants and the tenant. Several projectors may be registered, at most one
/// per alias; a duplicate fails at startup.
/// </summary>
/// <remarks>
/// A view must carry no captured value: identities, keys and statuses only. Free text that may hold a value, such as a
/// fault message, is included only when <see cref="ActivityStateViewContext.CanInspectSensitiveValues"/> is set.
/// </remarks>
public interface IActivityStateViewProjector
{
    /// <summary>The private-state type alias this projector reads, such as <c>Elsa.CompensationScope.State</c>.</summary>
    string StateTypeAlias { get; }

    /// <summary>The kind of view it produces, such as <c>compensation.journal</c>.</summary>
    string ViewKind { get; }

    /// <summary>The version of the view's shape.</summary>
    int ViewVersion { get; }

    /// <summary>Projects the view for one activity execution whose private state has <see cref="StateTypeAlias"/>.</summary>
    ValueTask<JsonElement> ProjectAsync(ActivityStateViewContext context, CancellationToken cancellationToken = default);
}

/// <summary>
/// What an <see cref="IActivityStateViewProjector"/> receives: the run, the activity execution with its private state,
/// its node in the pinned executable, the run's incidents, and whether the caller may see sensitive values.
/// </summary>
public sealed record ActivityStateViewContext(
    WorkflowExecutionState WorkflowExecution,
    ActivityExecutionState ActivityExecution,
    ExecutableNode ExecutableNode,
    IReadOnlyCollection<IncidentState> Incidents,
    bool CanInspectSensitiveValues);
