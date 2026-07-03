using System.Globalization;
using Elsa.Persistence.Groundwork.Exceptions;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// The per-document-kind schema versions of the runtime persistence bridge. Every document saved
/// through the bridge is stamped with the current version of its kind, and every load enforces the
/// stamp: older versions are upcasted through <see cref="IGroundworkRuntimeDocumentUpcaster"/> steps,
/// unknown or future versions fail loudly instead of silently deserializing with default values.
/// </summary>
/// <remarks>
/// Changing the serialized shape of any runtime state record requires, in the same change:
/// bumping that kind's version here, registering an upcaster for the previous version, adding a new
/// golden fixture for the new version, and keeping the historical fixtures. The fixture drift test
/// in <c>Elsa.Persistence.Groundwork.Tests</c> fails when a shape changes without a version bump.
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
        [ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.DurableValueStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.OperationalStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.ControlPlaneStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.IncidentStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.DurableTimerDocumentKind] = 1
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
