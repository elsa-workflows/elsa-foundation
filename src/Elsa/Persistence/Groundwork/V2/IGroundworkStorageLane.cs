namespace Elsa.Persistence.Groundwork.Composition;

/// <summary>
/// Names one persistence lane so cross-lane operations can ask which Groundwork target owns it.
/// <para>
/// This is identity only, deliberately: a lane that declares its storage units directly against the
/// public v2 catalog contributes no composed host manifest, but a caller spanning lanes still has to
/// know whether those lanes share a database before it can decide how to commit. Such a lane implements
/// this and nothing more, which is what keeps it out of the v1 document-store closure. A lane that still
/// contributes a composed host manifest implements <c>IGroundworkStorageManifestSource</c>, which
/// extends this.
/// </para>
/// </summary>
public interface IGroundworkStorageLane
{
    /// <summary>Gets the stable feature identity used for ordering, ownership and diagnostics.</summary>
    string FeatureIdentity { get; }
}
