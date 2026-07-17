using System.Globalization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// The per-document-kind schema versions of the runtime persistence bridge. Every document saved
/// through the bridge is stamped with the current version of its kind, and every load enforces the
/// stamp: only explicitly retained compatible versions are readable, while versions below a kind's
/// minimum-readable boundary plus unknown or future versions fail loudly instead of silently
/// deserializing with default values.
/// </summary>
/// <remarks>
/// The clean-baseline policy resets changed kinds to their current fixture unless an explicitly supported
/// rolling window is required. Workflow executables retain v5 as the first such window and migrate through
/// Groundwork's <c>IDocumentJsonUpcaster</c> contract. The fixture drift test in
/// <c>Elsa.Persistence.Groundwork.Tests</c> fails on unversioned shape changes. See
/// <c>docs/serialization.md</c> for the full evolution contract.
/// </remarks>
public static class ElsaRuntimeDocumentVersions
{
    private static readonly IReadOnlyDictionary<string, int> Current = new Dictionary<string, int>(StringComparer.Ordinal)
    {
        [ElsaRuntimeStorageManifest.BookmarkStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind] = 6,
        [ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.ExecutableActivityTemplateHashClaimDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutableSourceReferenceDocumentKind] = 4,
        [ElsaRuntimeStorageManifest.ActivityExecutionStateDocumentKind] = 4,
        [ElsaRuntimeStorageManifest.ActivityExecutionInspectionDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.ActivityExecutionHierarchyDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowExecutionStateDocumentKind] = 4,
        [ElsaRuntimeStorageManifest.WorkflowTestScopeDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.DurableValueStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.ExecutionLivenessStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.WorkflowHoldStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.IncidentStateDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.CheckpointCommitDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.PostCommitOutboxDocumentKind] = 3,
        [ElsaRuntimeStorageManifest.WorkflowDispatchDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.SchedulerWorkItemDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.SchedulerPoisonDocumentKind] = 1,
        [ElsaRuntimeStorageManifest.DurableTimerDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.WorkflowTriggerBindingDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.RecurringTriggerScheduleDocumentKind] = 2,
        [ElsaRuntimeStorageManifest.PublicationProjectionStateDocumentKind] = 1
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
    /// Returns the oldest schema version this build may deserialize for the given document kind.
    /// Versions below this boundary belong to an unsupported clean-break generation.
    /// </summary>
    /// <exception cref="ArgumentException">The document kind is not a runtime document kind.</exception>
    public static int MinimumReadableFor(string documentKind)
    {
        var currentVersion = CurrentFor(documentKind);
        return documentKind switch
        {
            ElsaRuntimeStorageManifest.WorkflowExecutableDocumentKind => 5,
            ElsaRuntimeStorageManifest.ExecutableActivityTemplateDocumentKind => 1,
            _ => currentVersion
        };
    }

    /// <summary>
    /// Parses a persisted schema-version stamp. Anything that is not a positive integer fails loudly
    /// rather than being treated as compatible.
    /// </summary>
    /// <exception cref="FormatException">The stamp is not a recognized version.</exception>
    public static int Parse(string documentKind, string schemaVersion)
    {
        if (int.TryParse(schemaVersion, NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) && parsed >= 1)
            return parsed;

        throw new FormatException(
            $"Document kind '{documentKind}' carries unrecognized schema version stamp '{schemaVersion}'. " +
            "Expected a positive integer. Refusing to deserialize.");
    }

    /// <summary>Formats a version as the stamp written to <c>SaveDocumentRequest.SchemaVersion</c>.</summary>
    public static string Stamp(int version) => version.ToString(CultureInfo.InvariantCulture);
}
