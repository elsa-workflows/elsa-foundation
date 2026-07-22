namespace Elsa.Persistence.Groundwork.Unified.Composition;

/// <summary>
/// An Elsa-owned, applied-plan fingerprint recorded after a successful runtime schema admission so a
/// later boot can prove the composed plan is unchanged and skip the full inspection/validation walk
/// (spec 133, First-Request/Cold-Start Readiness).
///
/// <para>
/// The stamp is deliberately <b>not</b> Groundwork's frozen <c>SchemaVersion</c> legacy stamp and never
/// touches it. It is a separate record scoped to one physical target — the (manifest identity, provider
/// name) pair — mirroring the granularity at which Groundwork itself records applied state.
/// </para>
///
/// <para>
/// The stamp is only ever an optimization token. A crash between applying schema and writing the stamp
/// leaves it absent, so the next boot falls through to today's full walk and re-admits idempotently; a
/// stamp is never a correctness gate. It also intentionally does not cover live provider state, so it
/// cannot detect out-of-band drift introduced while the host was down — that is why skip-if-current is
/// opt-in (see <see cref="GroundworkPhysicalSchemaManifestSource"/> callers).
/// </para>
/// </summary>
/// <param name="FormatVersion">
/// Version of the stamp record's own semantics. Bumping it invalidates every previously written stamp,
/// forcing a full walk on the next boot regardless of fingerprint equality.
/// </param>
/// <param name="TargetFingerprint">
/// Groundwork's authoritative physical-target fingerprint. Covers manifest contents, storage-unit routes,
/// projected columns, index sets and provider identity — everything the diff-plan walk compares against
/// the applied history.
/// </param>
/// <param name="CompositionFingerprint">
/// The wider Elsa host-selection fingerprint (selected features, contributor manifest versions,
/// naming-policy identity and durable requirements). A superset guard: any host-selection change that the
/// physical target happens to not move still invalidates the skip.
/// </param>
/// <param name="ProviderVersion">
/// The composed provider's version. A provider upgrade can change how the same target physicalizes, so a
/// version change must force a re-walk even when both fingerprints are stable.
/// </param>
public sealed record GroundworkAdmissionSkipStamp(
    int FormatVersion,
    string TargetFingerprint,
    string CompositionFingerprint,
    string ProviderVersion)
{
    /// <summary>Current stamp-record format version.</summary>
    public const int CurrentFormatVersion = 1;

    /// <summary>Computes the stamp that describes the currently composed admission plan.</summary>
    public static GroundworkAdmissionSkipStamp ForSource(GroundworkPhysicalSchemaManifestSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new GroundworkAdmissionSkipStamp(
            CurrentFormatVersion,
            source.TargetFingerprint,
            source.CompositionFingerprint,
            source.PhysicalTarget.Provider.Version);
    }

    /// <summary>
    /// True when this persisted stamp proves the <paramref name="current"/> composed plan is byte-for-byte
    /// the plan that was last successfully admitted, so the full inspection walk can be skipped. Every
    /// component must match; any difference falls through to the full walk.
    /// </summary>
    public bool Covers(GroundworkAdmissionSkipStamp current)
    {
        ArgumentNullException.ThrowIfNull(current);
        return FormatVersion == current.FormatVersion &&
               string.Equals(TargetFingerprint, current.TargetFingerprint, StringComparison.Ordinal) &&
               string.Equals(CompositionFingerprint, current.CompositionFingerprint, StringComparison.Ordinal) &&
               string.Equals(ProviderVersion, current.ProviderVersion, StringComparison.Ordinal);
    }
}
