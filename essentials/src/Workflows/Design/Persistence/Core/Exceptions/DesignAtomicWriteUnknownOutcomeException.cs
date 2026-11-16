namespace Elsa.Workflows.Design.Persistence.Core.Exceptions;

/// <summary>Raised when a provider commit acknowledgement cannot be classified after bounded recovery.</summary>
public sealed class DesignAtomicWriteUnknownOutcomeException(string message, Exception? innerException = null)
    : InvalidOperationException(message, innerException);
