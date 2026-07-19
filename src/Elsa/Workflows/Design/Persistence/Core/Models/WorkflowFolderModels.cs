using Elsa.Workflows.Design.Persistence.Core.Entities;

namespace Elsa.Workflows.Design.Persistence.Core.Models;

public sealed record WorkflowFolderDetails(WorkflowFolder Folder, IReadOnlyList<WorkflowFolder> Ancestors);
public sealed record WorkflowFolderPage(IReadOnlyList<WorkflowFolder> Items, string? NextContinuationToken);
public sealed record WorkflowFolderPageRequest(string? ParentFolderId, int PageSize = 100, string? ContinuationToken = null)
{
    public void Validate()
    {
        if (ParentFolderId is not null)
            WorkflowFolderNames.ValidateIdentifier(ParentFolderId, nameof(ParentFolderId));
        if (PageSize is <= 0 or > 100)
            throw new ArgumentOutOfRangeException(nameof(PageSize), "Workflow-folder page size must be between 1 and 100.");
        if (ContinuationToken is not null && string.IsNullOrWhiteSpace(ContinuationToken))
            throw new ArgumentException("The workflow-folder continuation token cannot be blank.", nameof(ContinuationToken));
    }
}

public static class WorkflowFolderNames
{
    public const int MaximumDepth = 16;
    public const int MaximumNameLength = 128;
    public const int MaximumIdentifierLength = 64;

    public static void ValidateIdentifier(string identifier, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(identifier))
            throw new ArgumentException("A workflow-folder identifier is required.", parameterName);
        if (identifier.Length > MaximumIdentifierLength)
            throw new ArgumentOutOfRangeException(parameterName, $"A workflow-folder identifier cannot exceed {MaximumIdentifierLength} characters.");
    }

    public static (string Name, string NormalizedName) Normalize(string name)
    {
        var trimmed = name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(trimmed))
            throw new ArgumentException("A workflow-folder name is required.", nameof(name));
        if (trimmed.Length > MaximumNameLength)
            throw new ArgumentOutOfRangeException(nameof(name), $"A workflow-folder name cannot exceed {MaximumNameLength} characters.");

        var normalized = trimmed.ToUpperInvariant();
        if (normalized.Length > MaximumNameLength)
            throw new ArgumentOutOfRangeException(nameof(name), $"A normalized workflow-folder name cannot exceed {MaximumNameLength} characters.");
        return (trimmed, normalized);
    }
}
