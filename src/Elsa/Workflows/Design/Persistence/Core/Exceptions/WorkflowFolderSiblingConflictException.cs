namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when a tenant's sibling-name uniqueness invariant rejects a folder create.</summary>
public sealed class WorkflowFolderSiblingConflictException(Exception? innerException = null)
    : Exception("A workflow folder with the same normalized sibling name already exists.", innerException);
