namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Signals that a folder hierarchy mutation lost its optimistic-concurrency fence.</summary>
public sealed class WorkflowFolderRestructureConflictException(Exception? innerException = null)
    : Exception("The workflow-folder hierarchy changed concurrently. Refresh and try again.", innerException);
