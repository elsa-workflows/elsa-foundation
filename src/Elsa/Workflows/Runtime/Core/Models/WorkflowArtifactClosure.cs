namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>
/// The portable export/import unit: one workflow executable plus its complete transitive dependency closure,
/// self-describing and self-contained (FR-B-001 / FR-B-010).
/// </summary>
/// <remarks>
/// <para>
/// Not a persisted store document — a wire/file format with its own versioning discipline. It rides
/// <c>Runtime.Core</c> because both sides of the seam need it: Publishing produces it (export) and
/// <c>Runtime.Reconciliation</c> consumes it (import).
/// </para>
/// <para>
/// <b>The carried collections are provenance, never rows to persist.</b> <see cref="SourceReferences"/> and
/// <see cref="TriggerBindings"/> describe what the <em>exporting</em> engine had live; its activation ids are
/// meaningless on the importing engine. The importer mints its own reference and recomputes its own bindings, and
/// uses the carried collections only as an integrity cross-check (D4).
/// </para>
/// </remarks>
/// <param name="FormatVersion">
/// The envelope's wire version. Readers accept exactly the versions they know — unknown or newer is a loud
/// rejection with no partial import, mirroring the runtime document codec's fail-loud discipline.
/// </param>
/// <param name="RootArtifactId">The exported artifact. Must appear in <paramref name="Artifacts"/>.</param>
/// <param name="Artifacts">
/// The root plus every transitive dependency reachable through <c>Dependencies</c>. Complete by contract: an
/// envelope whose edges only resolve because the target store happens to contain a child is a broken export.
/// </param>
/// <param name="SourceReferences">The exporting engine's <c>Published</c>-scope references. Expectations only.</param>
/// <param name="TriggerBindings">The exporting engine's active bindings for closure members. Expectations only.</param>
public sealed record WorkflowArtifactClosure(
    int FormatVersion,
    string RootArtifactId,
    IReadOnlyList<WorkflowExecutable> Artifacts,
    IReadOnlyList<WorkflowExecutableSourceReference> SourceReferences,
    IReadOnlyList<WorkflowTriggerBinding> TriggerBindings)
{
    /// <summary>
    /// The root plus every transitive dependency. Never null: an envelope that omits the member entirely
    /// deserializes to an empty closure, which the importer rejects as a named diagnostic rather than a null
    /// dereference somewhere downstream.
    /// </summary>
    public IReadOnlyList<WorkflowExecutable> Artifacts { get; init; } = Artifacts ?? [];

    /// <summary>The exporting engine's <c>Published</c>-scope references. Provenance only; never persisted.</summary>
    public IReadOnlyList<WorkflowExecutableSourceReference> SourceReferences { get; init; } = SourceReferences ?? [];

    /// <summary>The exporting engine's active bindings. Expectations only; the importer recomputes its own.</summary>
    public IReadOnlyList<WorkflowTriggerBinding> TriggerBindings { get; init; } = TriggerBindings ?? [];

    /// <summary>The declared root, or the empty string when the envelope named none.</summary>
    public string RootArtifactId { get; init; } = RootArtifactId ?? string.Empty;
}

/// <summary>Version policy for <see cref="WorkflowArtifactClosure"/>'s wire format.</summary>
/// <remarks>
/// Deliberately fail-loud and upcaster-free in v1: envelope evolution adds an upcaster behind a version bump,
/// never in-place shape drift. A silent upcast would let a newer producer's semantics be reinterpreted by an
/// older reader, which for a content-addressed artifact means importing behaviour nobody authored.
/// </remarks>
public static class WorkflowArtifactClosureFormat
{
    /// <summary>The version this build produces.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Every version this build can read.</summary>
    public static IReadOnlyCollection<int> SupportedVersions { get; } = [CurrentVersion];

    /// <summary>True when <paramref name="formatVersion"/> is one this build knows how to read.</summary>
    public static bool IsSupported(int formatVersion) => SupportedVersions.Contains(formatVersion);
}
