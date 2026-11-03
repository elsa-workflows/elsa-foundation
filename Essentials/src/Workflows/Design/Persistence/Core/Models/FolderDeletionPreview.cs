namespace Elsa.Workflows.Design.Persistence.Core.Models;

/// <summary>Authoritative deletion targets. Authorize them before exposing inventory to a caller.</summary>
public sealed record FolderDeletionPreview(WorkflowFolder Folder, WorkflowFolder? Parent,
    IReadOnlyList<WorkflowFolder> Descendants, IReadOnlyList<FolderDeletionWorkflow> Workflows, string Revision);

public sealed record FolderDeletionWorkflow(string DefinitionId, bool Deleted);
