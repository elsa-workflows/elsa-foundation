namespace Elsa.Activities.Testing;

/// <summary>
/// The one classifier that decides whether an assembly name belongs to the design/publish family — the half a
/// runtime-only engine must not reach (spec 151, SC-B-001 / SC-B-005).
/// </summary>
/// <remarks>
/// <para>
/// <b>Why it lives here.</b> Four guards in two different test assemblies ask this same question:
/// <c>RuntimeOnlyArtifactCompositionTests</c>, <c>ArtifactCompositionMatrixTests</c> and
/// <c>ArtifactExportImportRoundTripTests</c> in <c>tests/Elsa/Architecture</c>, and
/// <c>RuntimeOnlyLoadedAssemblyTests</c> in <c>tests/Elsa/Workflows/Runtime/Reconciliation/Tests</c>. Each carried
/// its own copy, and when the <c>.Design</c>-segment rule below was added it landed in exactly one of them — a
/// boundary that three of four sites no longer enforced. <c>Elsa.Activities.Testing</c> is the repository's only
/// shared test-support library (<c>IsTestProject=false</c>) and is already referenced by both assemblies, so it is
/// the only existing place a single definition can serve all four.
/// </para>
/// <para>
/// <b>Read as a ban or as a classifier.</b> In a runtime-only shape a match is a violation; in the combined shape
/// both families are expected and a match merely says which side served a contract. Same predicate either way —
/// see <see cref="IsDesignOrPublishing"/>.
/// </para>
/// </remarks>
public static class DesignTimeAssemblies
{
    /// <summary>
    /// The assembly-name prefixes that mark the design/publish family. <c>Elsa.Workflows.Publishing</c> has no
    /// trailing dot: the engine assembly itself is in the family, not only its sub-packages.
    /// </summary>
    /// <remarks>
    /// These prefixes do not describe design assemblies exhaustively, which is why
    /// <see cref="IsDesignOrPublishing"/> also matches a <c>Design</c> name segment. T128 split the composite
    /// activity packages into design and runtime halves named <c>Elsa.Activities.Sequence.Design</c> and five
    /// siblings — a family none of these prefixes match. They were caught only because each one happens to
    /// reference a <c>*.Design.Core</c> that a prefix <em>does</em> match, so the boundary held transitively and by
    /// coincidence, and the failure named the core rather than the real offender. The first design half that needs
    /// no core would have walked into a "runtime-only" engine unseen.
    /// </remarks>
    public static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "Elsa.Workflows.Design",
        "Elsa.Workflows.Publishing",
        "Elsa.Activities.Design"
    ];

    /// <summary>Whether <paramref name="assemblyName"/> belongs to the design/publish family.</summary>
    public static bool IsDesignOrPublishing(string assemblyName) =>
        ForbiddenAssemblyPrefixes.Any(prefix => assemblyName.StartsWith(prefix, StringComparison.Ordinal))
        || HasDesignSegment(assemblyName);

    /// <summary>
    /// Whether the name carries <c>Design</c> as a whole dot-separated segment — the T128
    /// <c>.Design</c>/<c>.Runtime</c> naming, wherever it appears in the name.
    /// </summary>
    /// <remarks>
    /// Segment-exact on purpose. A bare <c>Contains("Design")</c> would also sweep up
    /// <c>Elsa.Persistence.Groundwork.DesignConformance</c>, which is a conformance suite rather than a design
    /// assembly and belongs in a runtime-only closure; requiring the segment to end at a dot or at the end of the
    /// name keeps it out while still catching every <c>X.Design</c>.
    /// </remarks>
    public static bool HasDesignSegment(string assemblyName) =>
        assemblyName.EndsWith(".Design", StringComparison.Ordinal)
        || assemblyName.Contains(".Design.", StringComparison.Ordinal);
}
