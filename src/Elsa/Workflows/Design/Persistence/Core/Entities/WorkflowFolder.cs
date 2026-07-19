using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>A tenant-scoped organizational node for workflow definitions.</summary>
public sealed class WorkflowFolder : TenantEntity
{
    /// <summary>Non-null persistence key for sibling uniqueness; root uses a reserved sentinel.</summary>
    public const string RootParentKey = "@root";

    public string? ParentFolderId { get; set; }
    public string ParentKey { get; set; } = RootParentKey;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;
}
