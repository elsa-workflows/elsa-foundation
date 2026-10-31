using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;

internal sealed class WorkflowFolderEntity : TenantEntity
{
    public string? ParentId { get; set; }
    public string ParentKey { get; set; } = "";
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string NameHash { get; set; } = "";
    public string Path { get; set; } = "";
    public int Depth { get; set; }
    public string Revision { get; set; } = "";
}

internal sealed class WorkflowOrganizationEntity : TenantEntity
{
    public string DefinitionHash { get; set; } = "";
    public string DefinitionId { get; set; } = "";
    public string? FolderId { get; set; }
    public bool Published { get; set; }
    public string Revision { get; set; } = "";
}

internal sealed class WorkflowTagEntity : TenantEntity
{
    public string Name { get; set; } = "";
    public string NormalizedName { get; set; } = "";
    public string NameHash { get; set; } = "";
    public string? Description { get; set; }
    public string Mode { get; set; } = "marker";
    public string Cardinality { get; set; } = "single";
    public bool Deprecated { get; set; }
    public bool EverAssigned { get; set; }
    public string Revision { get; set; } = "";
    public string ValuesJson { get; set; } = "[]";
}

internal sealed class WorkflowTagAssignmentEntity : TenantEntity
{
    public string DefinitionHash { get; set; } = "";
    public string TagId { get; set; } = "";
    public string ValueKey { get; set; } = "";
}

internal sealed class WorkflowLibraryViewEntity : TenantEntity
{
    public string Name { get; set; } = "";
    public string OwnerId { get; set; } = "";
    public string OwnerHash { get; set; } = "";
    public bool Shared { get; set; }
    public string Revision { get; set; } = "";
    public string ContentJson { get; set; } = "";
}

internal sealed class WorkflowFavoriteEntity : TenantEntity
{
    public string DefinitionHash { get; set; } = "";
    public string SubjectId { get; set; } = "";
    public string SubjectHash { get; set; } = "";
}

internal sealed class WorkflowLibraryAuditEntity : TenantEntity
{
    public string TargetId { get; set; } = "";
    public string ActorId { get; set; } = "";
    public string Operation { get; set; } = "";
    public string OperationKey { get; set; } = "";
    public string BeforeJson { get; set; } = "";
    public string AfterJson { get; set; } = "";
}

internal sealed class WorkflowLibraryFenceEntity : TenantEntity
{
    public string Revision { get; set; } = "";
}
