namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when a concurrent writer prevents an all-or-none definition-folder move.</summary>
public sealed class WorkflowDefinitionFolderMoveConflictException(Exception? innerException = null)
    : Exception("The workflow-definition move conflicted with another change. Refresh and try again.", innerException);
