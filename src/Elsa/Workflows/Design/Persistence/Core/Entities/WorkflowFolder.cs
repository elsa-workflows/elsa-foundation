using Elsa.Primitives.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Entities;

/// <summary>A tenant-scoped organizational node for workflow definitions.</summary>
public sealed class WorkflowFolder : TenantEntity
{
    /// <summary>Non-null persistence key for sibling uniqueness at the logical root.</summary>
    public const string RootParentKey = "root:";

    /// <summary>Namespace applied to opaque folder identities before they are used as persistence parent keys.</summary>
    public const string ParentFolderKeyPrefix = "folder:";

    public string? ParentFolderId { get; set; }
    public string ParentKey { get; set; } = RootParentKey;
    public string Name { get; set; } = string.Empty;
    public string NormalizedName { get; set; } = string.Empty;

    public static string ToParentKey(string? parentFolderId) =>
        parentFolderId is null ? RootParentKey : $"{ParentFolderKeyPrefix}{parentFolderId}";
}
