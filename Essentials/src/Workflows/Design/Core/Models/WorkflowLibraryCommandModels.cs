namespace Elsa.Workflows.Design.Core.Models;

/// <summary>The explicit actor and tenant a workflow library command runs for. They must be the current request's own.</summary>
public sealed record WorkflowLibraryActor(string ActorId, string TenantId);

/// <summary>The stable statuses of a workflow library command and of each target it reports.</summary>
public static class WorkflowLibraryCommandStatus
{
    /// <summary>The owning library service persisted the change.</summary>
    public const string Applied = "applied";
    /// <summary>The actor lacks the native granular authority for the change or target; nothing changed.</summary>
    public const string Denied = "denied";
    /// <summary>Workflow organization is disabled or unsupported by this backend; nothing changed.</summary>
    public const string Unavailable = "unavailable";
    /// <summary>The bound revision is stale, or the operation key was used for a different change; nothing changed.</summary>
    public const string Conflict = "conflict";
    /// <summary>The target is not visible or does not exist; nothing changed.</summary>
    public const string NotFound = "not-found";
    /// <summary>The library service refused the request's content; nothing changed.</summary>
    public const string Invalid = "invalid";
    /// <summary>The library backend does not support client operation keys, so a keyed create is unavailable; nothing changed.</summary>
    public const string Unsupported = "unsupported";
}

/// <summary>The result of one workflow library command: a status, a cause-free message and, when applied or read, its value.</summary>
public sealed record WorkflowLibraryCommandResult<T>(string Status, string? Message = null, T? Value = default)
{
    /// <summary>Whether the command was applied (for a read: answered).</summary>
    public bool Succeeded => Status == WorkflowLibraryCommandStatus.Applied;
}

/// <summary>One target's outcome of a multi-target library command. A partial command never reports its refused targets as applied.</summary>
public sealed record WorkflowLibraryTargetOutcome(string DefinitionId, string Status, string? Revision = null, string? Message = null);

/// <summary>The actor's library permission flags, exactly as the native library API reports them.</summary>
public sealed record WorkflowLibraryPermissionFlags(bool CanAssignTags, bool CanManageFolders, bool CanManageTags, bool CanShareViews);

/// <summary>A tag assignment: a marker tag, or one value of a controlled tag.</summary>
public sealed record WorkflowLibraryTagReference(string TagId, string? ValueId = null);

/// <summary>A value of a controlled tag.</summary>
public sealed record WorkflowLibraryTagValueSnapshot(string Id, string Name, bool Deprecated);

/// <summary>A tag as the owning library service reports it.</summary>
public sealed record WorkflowLibraryTagSnapshot(string Id, string Name, string? Description, string Mode, string Cardinality,
    bool Deprecated, string Revision, IReadOnlyList<WorkflowLibraryTagValueSnapshot> Values);

/// <summary>The input of an explicit tag creation, complete as the library's own tag create accepts it. Controlled values are
/// named, optionally retired; their identities are generated.</summary>
public sealed record WorkflowLibraryTagCreate(string Name, string? Description = null, string Mode = "marker", string Cardinality = "single",
    IReadOnlyList<WorkflowLibraryTagValueCreate>? Values = null, bool Deprecated = false);

/// <summary>A controlled value of a tag being created. Its identity is generated: a supplied <paramref name="Id"/> is refused by
/// the library as its own create refuses it.</summary>
public sealed record WorkflowLibraryTagValueCreate(string Name, bool Deprecated = false, string? Id = null);

/// <summary>A definition's current organization: its folder, tags and the revision a tag edit binds to.</summary>
public sealed record WorkflowLibraryOrganizationSnapshot(string DefinitionId, string? FolderId, IReadOnlyList<WorkflowLibraryTagReference> Tags, string Revision);

/// <summary>A definition and the organization revision a tag edit is bound to.</summary>
public sealed record WorkflowLibraryRevisionTarget(string DefinitionId, string Revision);

/// <summary>One bulk tag edit: per-target revisions, the assignments to add and remove, and its operation key.</summary>
public sealed record WorkflowLibraryTagEdit(IReadOnlyList<WorkflowLibraryRevisionTarget> Targets,
    IReadOnlyList<WorkflowLibraryTagReference> Add, IReadOnlyList<WorkflowLibraryTagReference> Remove, string OperationKey);

/// <summary>The visible library query a view stores.</summary>
public sealed record WorkflowLibraryViewQuery(string? SearchTerm = null, string State = "active", string? FolderId = null,
    bool Unfiled = false, bool IncludeDescendants = false, IReadOnlyList<WorkflowLibraryTagFilterSnapshot>? TagFilters = null,
    bool FavoritesOnly = false, string Sort = "name", int PageSize = 50, string? PublicationState = null);

/// <summary>A tag or value filter of the visible library query.</summary>
public sealed record WorkflowLibraryTagFilterSnapshot(string TagId, string Operator = "exists", IReadOnlyList<string>? ValueIds = null);

/// <summary>The input of a view save: a new view, or an existing view bound by its revision.</summary>
public sealed record WorkflowLibraryViewSave(string Name, WorkflowLibraryViewQuery Query, bool Shared = false,
    IReadOnlyList<string>? Columns = null, string? Revision = null);

/// <summary>A workflow library view as the owning library service reports it, with the actor's own edit right.</summary>
public sealed record WorkflowLibraryViewSnapshot(string Id, string Name, bool Shared, string OwnerId, WorkflowLibraryViewQuery Query,
    IReadOnlyList<string> Columns, string Revision, bool CanEdit)
{
    /// <summary>The stored view's schema version, as the library reports it.</summary>
    public int SchemaVersion { get; init; } = 1;
}
