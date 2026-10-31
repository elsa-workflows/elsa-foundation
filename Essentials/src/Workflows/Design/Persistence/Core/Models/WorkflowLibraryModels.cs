using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

public sealed record WorkflowFolder(string Id, string? ParentId, string Name, string NormalizedName,
    DateTimeOffset CreatedAt, DateTimeOffset LastModifiedAt, string Revision);
public sealed record WorkflowFolderDetail(WorkflowFolder Folder, IReadOnlyList<WorkflowFolder> Ancestors);
public sealed record WorkflowFolderPage(IReadOnlyList<WorkflowFolder> Items, string? NextContinuationToken);

public sealed record WorkflowTagValue(string Id, string Name, bool Deprecated = false);
public sealed record WorkflowTag(string Id, string Name, string? Description, string Mode, string Cardinality,
    bool Deprecated, string Revision, IReadOnlyList<WorkflowTagValue> Values);
public sealed record WorkflowTagInput(string Name, string? Description = null, string Mode = "marker",
    string Cardinality = "single", bool Deprecated = false, IReadOnlyList<WorkflowTagValue>? Values = null,
    string? Revision = null);
public sealed record WorkflowTagPage(IReadOnlyList<WorkflowTag> Items, string? NextContinuationToken);
public sealed record WorkflowTagAssignment(string TagId, string? ValueId = null);
public sealed record WorkflowTagFilter(string TagId, string Operator = "exists", IReadOnlyList<string>? ValueIds = null);

public sealed record WorkflowOrganization(string DefinitionId, string? FolderId,
    IReadOnlyList<WorkflowFolder> FolderBreadcrumb, IReadOnlyList<WorkflowTagAssignment> Tags, string Revision);

public sealed record WorkflowLibraryQuery(string? SearchTerm = null, string State = "active", string? FolderId = null,
    bool Unfiled = false, bool IncludeDescendants = false, IReadOnlyList<WorkflowTagFilter>? TagFilters = null,
    bool FavoritesOnly = false, string Sort = "name", int PageSize = 50, string? ContinuationToken = null,
    string? PublicationState = null);

public sealed record WorkflowLibraryDefinition(WorkflowDefinition Definition, WorkflowOrganization Organization,
    bool Favorite, string? DraftId = null, string? LatestVersionId = null, string? LatestVersion = null,
    int VersionCount = 0, string? DraftSourceVersionId = null, string? DraftSourceVersion = null,
    string? PublishedVersionId = null, string? PublishedVersion = null, string? PublicationState = null);
public sealed record WorkflowLibraryPage(IReadOnlyList<WorkflowLibraryDefinition> Items,
    string? NextContinuationToken, int TotalCount);

public sealed record WorkflowTagEdit(string DefinitionId, string Revision);
public sealed record WorkflowTagEditOutcome(string DefinitionId, string Status, string? Revision = null,
    string? Message = null);
public sealed record WorkflowTagBulkEdit(IReadOnlyList<WorkflowTagEdit> Targets,
    IReadOnlyList<WorkflowTagAssignment> Add, IReadOnlyList<WorkflowTagAssignment> Remove, string OperationKey);

public sealed record WorkflowLibraryView(string Id, string Name, bool Shared, string OwnerId,
    WorkflowLibraryQuery Query, IReadOnlyList<string> Columns, string Revision, int SchemaVersion = 1);
public sealed record WorkflowLibraryViewInput(string Name, WorkflowLibraryQuery Query, bool Shared = false,
    IReadOnlyList<string>? Columns = null, string? Revision = null);
public sealed record WorkflowOrganizationChange(string Id, string TargetId, string ActorId, string Operation,
    DateTimeOffset Timestamp, string Before, string After, string OperationKey);

/// <summary>A stale organization or hierarchy write; callers must reload before intentionally retrying.</summary>
public sealed class WorkflowLibraryConflictException(string message) : Exception(message);
