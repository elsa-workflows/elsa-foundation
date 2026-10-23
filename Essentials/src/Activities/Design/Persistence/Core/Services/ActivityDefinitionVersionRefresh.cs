namespace Elsa.Activities.Design.Persistence.Core.Services;

/// <summary>
/// Shared compare-and-swap outcome for the sanctioned in-place refresh of
/// <c>ActivityDefinitionVersion.Hash</c> and <c>InputsSource</c>.
/// </summary>
public static class ActivityDefinitionVersionRefresh
{
    public enum Outcome
    {
        Write,
        AlreadyWritten,
        Conflict
    }

    /// <summary>
    /// Classifies a stored hash against the expected previous hash and the incoming hash.
    /// A row already at the incoming hash is success (another node won the race). A row still
    /// at the expected previous hash should be written. Any other stored hash is a conflict.
    /// </summary>
    public static Outcome Classify(string? storedHash, string? expectedOldHash, string? incomingHash)
    {
        if (string.Equals(storedHash, incomingHash, StringComparison.Ordinal))
            return Outcome.AlreadyWritten;
        if (string.Equals(storedHash, expectedOldHash, StringComparison.Ordinal))
            return Outcome.Write;
        return Outcome.Conflict;
    }
}
