namespace Elsa.Activities.Design.Reconciliation.Core;

/// <summary>
/// Thrown when a reconciliation pass observes a (DefinitionId, Version) that already exists
/// in the catalog but with a different content hash than the incoming candidate. Under the
/// Model X reconciliation policy this is NOT a duplicate — it is a real version-mismatch
/// conflict: the source has produced the same logical identity with different content,
/// which indicates a source-side bug (a content edit without bumping the Version number)
/// or a misconfigured reconciliation source (two sources contributing under the same
/// logical key).
///
/// Throwing surfaces the breakage loudly to the operator at reconciliation time rather than
/// tolerating silent drift. Knowing the source is broken is useful knowledge, not a problem
/// the system should paper over.
/// </summary>
public sealed class ActivityVersionHashMismatchException : Exception
{
    public ActivityVersionHashMismatchException(
        string definitionId,
        string activityTypeKey,
        string persistedVersion,
        string incomingVersion,
        string? persistedHash,
        string incomingHash)
        : base(
            $"Activity definition '{activityTypeKey}' (id = '{definitionId}') already has version {persistedVersion} persisted with hash '{persistedHash}', but the reconciliation source contributed the same logical version ({incomingVersion}) with hash '{incomingHash}'. " +
            "Same logical identity must imply same content (build metadata is ignored when matching versions). The source is broken — either fix the source-side projection or bump the Version number.")
    {
        DefinitionId = definitionId;
        ActivityTypeKey = activityTypeKey;
        PersistedVersion = persistedVersion;
        IncomingVersion = incomingVersion;
        PersistedHash = persistedHash;
        IncomingHash = incomingHash;
    }

    public string DefinitionId { get; }
    public string ActivityTypeKey { get; }

    /// <summary>The full version string of the already-persisted row, including its build metadata.</summary>
    public string PersistedVersion { get; }

    /// <summary>The full version string the source contributed, including its build metadata. Shares
    /// the same SemVer core as <see cref="PersistedVersion"/> — that core-equality is what makes the
    /// two the same logical version — but the build metadata may differ.</summary>
    public string IncomingVersion { get; }

    public string? PersistedHash { get; }
    public string IncomingHash { get; }
}
