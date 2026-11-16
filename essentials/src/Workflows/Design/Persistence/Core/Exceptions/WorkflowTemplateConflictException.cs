namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

public sealed class WorkflowTemplateConflictException() : InvalidOperationException(
    "This template changed in another session. Reload it before saving or deleting; your local edits have been preserved.");
