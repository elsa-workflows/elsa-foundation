using System.Text.Json.Nodes;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// A single schema-migration step for one runtime document kind: rewrites content JSON from
/// <see cref="FromVersion"/> to <c>FromVersion + 1</c>. Steps are chained by
/// <see cref="IGroundworkRuntimeDocumentUpcasterRegistry"/> so a document at any supported historical
/// version reaches the kind's current version before deserialization.
/// </summary>
/// <remarks>
/// Contribution surface (fan-in): register any number of implementations in the service collection;
/// the registry discovers them. When bumping a kind's version in
/// <see cref="ElsaRuntimeDocumentVersions"/>, register an upcaster for the previous version in the
/// same change unless the change explicitly advances the minimum-readable clean-break floor.
/// Upcasting is lazy and read-time only; the write path always stamps the current version, so
/// documents re-baseline on their next save.
/// </remarks>
public interface IGroundworkRuntimeDocumentUpcaster
{
    /// <summary>The document kind this step applies to (a kind constant from <see cref="ElsaRuntimeStorageManifest"/>).</summary>
    string DocumentKind { get; }

    /// <summary>The schema version this step reads. The step produces <c>FromVersion + 1</c>.</summary>
    int FromVersion { get; }

    /// <summary>Rewrites content JSON from <see cref="FromVersion"/> to the next version.</summary>
    JsonObject Upcast(JsonObject content);
}
