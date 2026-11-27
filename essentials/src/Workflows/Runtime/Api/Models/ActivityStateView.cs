using System.Text.Json;

namespace Elsa.Workflows.Runtime.Api.Models;

/// <summary>
/// The read-only activity state view (spec 260 R-6): a module's projection of a live structural activity's private
/// state, with the activity it belongs to. When the view is not available, <see cref="UnavailableReason"/> says why
/// (<see cref="StateCleared"/> or <see cref="NoProjector"/>) and <see cref="View"/> is null.
/// </summary>
public sealed record ActivityStateView(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    string ActivityStatus,
    bool Available,
    string? UnavailableReason,
    string? ViewKind,
    int? ViewVersion,
    bool CanInspectValues,
    JsonElement? View)
{
    /// <summary>The activity keeps no private state, such as a completed compensation scope.</summary>
    public const string StateCleared = "StateCleared";

    /// <summary>No projector is registered for the private state's type alias.</summary>
    public const string NoProjector = "NoProjector";
}

/// <summary>The caller's permissions for one run (spec 260 FR-416). A display hint; the endpoints still enforce them.</summary>
public sealed record WorkflowInstancePermissionsView(bool CanManage, bool CanInspectValues);
