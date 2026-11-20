namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when a promotion retry key is reused with different normalized selection intent.</summary>
public sealed class WorkflowPromotionOperationConflictException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
