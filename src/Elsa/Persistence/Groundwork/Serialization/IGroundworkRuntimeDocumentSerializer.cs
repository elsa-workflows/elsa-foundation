using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Serializes and deserializes runtime documents persisted through the Groundwork bridge, owning
/// both the frozen bridge serializer options and the per-document-kind schema-version contract:
/// writes stamp the kind's current version, reads enforce the stamp and upcast older documents
/// through the registered <see cref="IGroundworkRuntimeDocumentUpcaster"/> chain.
/// </summary>
/// <remarks>
/// Replacement contract: exactly one implementation is active per runtime host. The default is
/// <see cref="GroundworkRuntimeDocumentSerializer"/>. This is the only sanctioned serialization
/// surface for runtime documents; store bridges must not call <c>System.Text.Json</c> directly.
/// See <c>docs/serialization.md</c>.
/// </remarks>
public interface IGroundworkRuntimeDocumentSerializer
{
    /// <summary>
    /// Serializes a document of the given kind, returning the content JSON together with the
    /// schema-version stamp to write to <c>SaveDocumentRequest.SchemaVersion</c>.
    /// </summary>
    (string SchemaVersion, string ContentJson) Serialize<T>(string documentKind, T document);

    /// <summary>
    /// Serializes a value with the frozen bridge options without stamping a version. Used for
    /// content-equality comparisons, never for persistence.
    /// </summary>
    string SerializeForComparison<T>(T value);

    /// <summary>
    /// Returns whether the envelope's schema-version stamp parses to the current version of its
    /// document kind. Stores may only take partial-read fast paths (deserializing a fragment of
    /// <c>ContentJson</c> instead of the whole document) when this is true; otherwise they must go
    /// through <see cref="Deserialize{T}"/> so upcasting and version enforcement apply.
    /// </summary>
    /// <exception cref="Exceptions.GroundworkRuntimeDocumentVersionException">
    /// The envelope's version stamp is unrecognized.
    /// </exception>
    bool IsCurrentVersion(DocumentEnvelope envelope);

    /// <summary>
    /// Deserializes a fragment of a current-version document with the frozen bridge options. Guard
    /// with <see cref="IsCurrentVersion"/> first.
    /// </summary>
    T DeserializeElement<T>(System.Text.Json.JsonElement element);

    /// <summary>
    /// Deserializes a persisted envelope, enforcing its schema-version stamp: the current version
    /// deserializes directly, older versions are upcasted step-by-step to the current version first,
    /// and unrecognized or future versions fail loudly.
    /// </summary>
    /// <exception cref="Exceptions.GroundworkRuntimeDocumentVersionException">
    /// The envelope's version stamp is unrecognized, newer than this build supports, or older with
    /// an incomplete upcaster chain.
    /// </exception>
    T Deserialize<T>(DocumentEnvelope envelope);
}
