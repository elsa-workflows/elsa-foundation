using Elsa.Workflows.Design.Core.Models;

namespace Elsa.Workflows.Design.Core.Contracts;

/// <summary>
/// The workflow library's organization commands and the reads they bind to, for consumers outside the Workflows Design
/// API (such as Weaver) that may couple only to <c>.Core</c> contracts: tag assignment with explicit tag creation,
/// favorites, folder moves, and saving or sharing a workflow library view.
/// </summary>
/// <remarks>
/// This is a single-implementation replacement contract in the current application context: exactly one implementation
/// is installed, and a second registration fails startup. Consumers must fail closed when no implementation or several
/// implementations are installed. The default, in the Workflows Design API, makes exactly the native granular authority
/// decisions the library endpoints make, for the explicit actor and tenant, which
/// must be the current request's own; any other actor or tenant is denied. Every command reports a status from
/// <see cref="WorkflowLibraryCommandStatus"/>: with workflow organization disabled each is
/// <see cref="WorkflowLibraryCommandStatus.Unavailable"/> with the backend's reason. Messages are cause-free. Delete and
/// permanent delete are not offered. An infrastructure failure propagates: for a create, its outcome is then decided by
/// rereading the resource by its operation key. A replacement decides Weaver's library writes; the library's HTTP
/// endpoints keep the built-in decisions.
/// </remarks>
public interface IWorkflowLibraryCommands
{
    /// <summary>The actor's library permission flags as the native API reports them, or unavailable with its reason.</summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryPermissionFlags>> GetPermissionsAsync(WorkflowLibraryActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>A definition's current organization, under the library's read decisions for the definition and its folders.</summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryOrganizationSnapshot>> GetOrganizationAsync(WorkflowLibraryActor actor, string definitionId,
        CancellationToken cancellationToken = default);

    /// <summary>The first page (at most 100) of tags whose normalized name starts with the search, or all tags when it is null.</summary>
    ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTagSnapshot>>> ListTagsAsync(WorkflowLibraryActor actor, string? search,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Creates a tag under tag-management authority. With a client operation key a repeat with the same key and input
    /// returns that tag (a store without key support answers <see cref="WorkflowLibraryCommandStatus.Unsupported"/>); without
    /// one it is the library's ordinary, non-idempotent create, as its HTTP route offers it. Weaver always passes a key.
    /// </summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> CreateTagAsync(WorkflowLibraryActor actor, WorkflowLibraryTagCreate input,
        string? operationKey, CancellationToken cancellationToken = default);

    /// <summary>Rereads the tag a keyed create made; applied with a null value when the key created none.</summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryTagSnapshot>> FindTagByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey,
        CancellationToken cancellationToken = default);

    /// <summary>Adds and removes tag assignments on each target bound by its revision, reporting one outcome per target.</summary>
    ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> EditTagsAsync(WorkflowLibraryActor actor, WorkflowLibraryTagEdit edit,
        CancellationToken cancellationToken = default);

    /// <summary>Sets or clears the actor's favorite on each target, an idempotent set reported per target.</summary>
    ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> SetFavoritesAsync(WorkflowLibraryActor actor,
        IReadOnlyList<string> definitionIds, bool favorite, CancellationToken cancellationToken = default);

    /// <summary>Moves the targets into a folder (null: unfiled) as one atomic, idempotent set: every target reports the same
    /// outcome. Without an operation key the library generates its own.</summary>
    ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryTargetOutcome>>> MoveDefinitionsAsync(WorkflowLibraryActor actor,
        IReadOnlyList<string> definitionIds, string? folderId, string? operationKey, CancellationToken cancellationToken = default);

    /// <summary>The views the actor can see: its own private views and every shared view.</summary>
    ValueTask<WorkflowLibraryCommandResult<IReadOnlyList<WorkflowLibraryViewSnapshot>>> ListViewsAsync(WorkflowLibraryActor actor,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a view: a new one (<paramref name="viewId"/> null), under a client operation key when one is given (as Weaver always
    /// does) or as the library's ordinary non-idempotent create otherwise; or an existing one bound by its revision, which
    /// takes no key (<see cref="WorkflowLibraryCommandStatus.Invalid"/> with one). Sharing requires view-sharing authority.
    /// </summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> SaveViewAsync(WorkflowLibraryActor actor, string? viewId,
        WorkflowLibraryViewSave input, string? operationKey, CancellationToken cancellationToken = default);

    /// <summary>Rereads the view the actor's keyed create made; applied with a null value when the key created none.</summary>
    ValueTask<WorkflowLibraryCommandResult<WorkflowLibraryViewSnapshot>> FindViewByOperationKeyAsync(WorkflowLibraryActor actor, string operationKey,
        CancellationToken cancellationToken = default);
}
