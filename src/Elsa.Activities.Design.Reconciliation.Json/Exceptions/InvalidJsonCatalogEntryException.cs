namespace Elsa.Activities.Design.Reconciliation.Json.Exceptions;

/// <summary>
/// Raised when a JSON catalog entry cannot be validated or mapped into the activity
/// model — unknown <c>implementationKind</c>, malformed descriptor payload, missing
/// required field. Carries enough context (entry index, kind, activity-type-key) to
/// localise the failure in the source file. Replaces raw <c>JsonException</c> /
/// <c>InvalidOperationException</c> escaping the JSON reader.
/// </summary>
public sealed class InvalidJsonCatalogEntryException : Exception
{
    public int EntryIndex { get; }
    public string? ActivityTypeKey { get; }
    public string? ImplementationKind { get; }

    public InvalidJsonCatalogEntryException(int entryIndex, string? activityTypeKey, string? implementationKind, string message)
        : base(BuildMessage(entryIndex, activityTypeKey, implementationKind, message))
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        ImplementationKind = implementationKind;
    }

    public InvalidJsonCatalogEntryException(int entryIndex, string? activityTypeKey, string? implementationKind, string message, Exception inner)
        : base(BuildMessage(entryIndex, activityTypeKey, implementationKind, message), inner)
    {
        EntryIndex = entryIndex;
        ActivityTypeKey = activityTypeKey;
        ImplementationKind = implementationKind;
    }

    private static string BuildMessage(int entryIndex, string? activityTypeKey, string? implementationKind, string message)
    {
        var context = string.Join(", ", new[]
        {
            $"entryIndex={entryIndex}",
            activityTypeKey is null ? null : $"activityTypeKey='{activityTypeKey}'",
            implementationKind is null ? null : $"implementationKind='{implementationKind}'",
        }.Where(x => x is not null));

        return $"Invalid JSON catalog entry ({context}): {message}";
    }
}
