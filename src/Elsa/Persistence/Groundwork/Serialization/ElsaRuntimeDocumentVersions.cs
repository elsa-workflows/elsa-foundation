using System.Globalization;
using Elsa.Persistence.Groundwork.Exceptions;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// The per-document-kind schema versions of the runtime persistence bridge. Every document saved
/// through the bridge is stamped with the current version of its kind, and every load enforces the
/// stamp: supported older versions are upcasted through <see cref="IGroundworkRuntimeDocumentUpcaster"/>
/// steps, while versions below an explicit clean-break floor and unknown/future versions fail loudly.
/// </summary>
/// <remarks>
/// Changing a serialized shape requires a version bump and a current golden fixture. Ordinarily it
/// also requires an upcaster. A deliberate clean break instead advances the kind's minimum-readable
/// version and retains the old fixture as rejection evidence; no compatibility upcaster is registered.
/// See <c>docs/serialization.md</c> for the full evolution contract.
/// </remarks>
public static class ElsaRuntimeDocumentVersions
{
    /// <summary>
    /// The stamp every document written before per-kind versioning carried
    /// (the manifest-wide <see cref="ElsaRuntimeStorageManifest.SchemaVersion"/>).
    /// It reads as version 1 of every kind.
    /// </summary>
    public const string LegacySchemaVersion = "1.0.0";

    private static readonly IReadOnlyDictionary<string, int> Current = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [ElsaRuntimeStorageManifest.BookmarkStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind] = 3,
        [ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind] = 3,
        [ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind] = 3,
        [ElsaRuntimeStorageManifest.DurableValueStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.IncidentStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.DurableTimerDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind] = 1
    };

    private static readonly IReadOnlyDictionary<string, int> MinimumReadable = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        // Elsa 4 deliberately does not translate CLR descriptor-type executable artifacts. Version 2
        // is the first stable consumer/schema wire format and the first hierarchical Source Reference.
        [ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind] = 2
    };

    /// <summary>The current versions of every runtime document kind, keyed by document kind.</summary>
    public static IReadOnlyDictionary<string, int> All => Current;

    /// <summary>Returns the current schema version of the given document kind.</summary>
    /// <exception cref="ArgumentException">The document kind is not a runtime document kind.</exception>
    public static int CurrentFor(string documentKind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentKind);
        return Current.TryGetValue(documentKind, out var version)
            ? version
            : throw new ArgumentException($"'{documentKind}' is not a runtime document kind declared in {nameof(ElsaRuntimeDocumentVersions)}.", nameof(documentKind));
    }

    /// <summary>
    /// Returns the oldest readable version for a kind. Versions below this floor are intentional
    /// clean-break artifacts and are rejected without consulting the upcaster registry.
    /// </summary>
    public static int MinimumReadableFor(string documentKind)
    {
        _ = CurrentFor(documentKind);
        return MinimumReadable.GetValueOrDefault(documentKind, 1);
    }

    /// <summary>
    /// Parses a persisted schema-version stamp. The pre-versioning stamp
    /// (<see cref="LegacySchemaVersion"/>) parses as version 1; anything that is not a positive
    /// integer fails loudly rather than being treated as compatible.
    /// </summary>
    /// <exception cref="GroundworkRuntimeDocumentVersionException">The stamp is not a recognized version.</exception>
    public static int Parse(string documentKind, string schemaVersion)
    {
        if (string.Equals(schemaVersion, LegacySchemaVersion, StringComparison.Ordinal))
            return 1;

        if (int.TryParse(schemaVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return parsed;

        throw new GroundworkRuntimeDocumentVersionException(
            $"Document kind '{documentKind}' carries unrecognized schema version stamp '{schemaVersion}'. " +
            $"Expected a positive integer or the legacy stamp '{LegacySchemaVersion}'. Refusing to deserialize.");
    }

    /// <summary>Formats a version as the stamp written to <c>SaveDocumentRequest.SchemaVersion</c>.</summary>
    public static string Stamp(int version) => version.ToString(CultureInfo.InvariantCulture);
}
