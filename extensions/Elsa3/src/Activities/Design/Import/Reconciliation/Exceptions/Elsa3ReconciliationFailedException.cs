namespace Elsa3.Activities.Design.Import.Reconciliation.Exceptions;

/// <summary>
/// The import of Elsa 3 files at engine start could not complete: an input path is missing or unreadable, a refusal
/// the import does not attribute to one item was raised, or strict mode found skipped items. Thrown from the startup
/// task, so the shell does not become ready.
/// </summary>
public sealed class Elsa3ReconciliationFailedException : Exception
{
    public Elsa3ReconciliationFailedException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
