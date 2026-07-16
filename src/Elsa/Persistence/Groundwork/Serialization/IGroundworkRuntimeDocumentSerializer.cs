using Groundwork.Documents.Store;
using Groundwork.Documents.Serialization;

namespace Elsa.Persistence.Groundwork.Serialization;

/// <summary>
/// Serializes and deserializes runtime documents persisted through the Groundwork bridge, owning
/// both the frozen bridge serializer options and the per-document-kind schema-version contract:
/// writes stamp the kind's current version and reads enforce its minimum-readable boundary according to
/// the declared per-kind version policy.
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
    /// <exception cref="DocumentSchemaVersionException">
    /// No Elsa schema policy exists for <paramref name="documentKind"/>
    /// (<see cref="DocumentSchemaVersionFailure.UnknownDocumentKind"/>).
    /// </exception>
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
    /// through <see cref="Deserialize{T}"/> so version enforcement applies.
    /// </summary>
    /// <exception cref="DocumentSchemaVersionException">
    /// The document kind has no Elsa policy (<see cref="DocumentSchemaVersionFailure.UnknownDocumentKind"/>),
    /// or its stamp is malformed (<see cref="DocumentSchemaVersionFailure.MalformedStamp"/>). Recognized
    /// non-current recognized versions return <see langword="false"/>.
    /// </exception>
    bool IsCurrentVersion(DocumentEnvelope envelope);

    /// <summary>
    /// Deserializes a fragment of a current-version document with the frozen bridge options. Guard
    /// with <see cref="IsCurrentVersion"/> first.
    /// </summary>
    T DeserializeElement<T>(System.Text.Json.JsonElement element);

    /// <summary>
    /// Deserializes a persisted envelope, enforcing its schema-version stamp: the current version
    /// deserializes directly, while versions below the kind's minimum-readable boundary plus unrecognized or
    /// future versions fail loudly. Before GA, minimum-readable equals current for every kind.
    /// </summary>
    /// <exception cref="DocumentSchemaVersionException">
    /// The kind is unknown (<see cref="DocumentSchemaVersionFailure.UnknownDocumentKind"/>), the stamp is
    /// malformed (<see cref="DocumentSchemaVersionFailure.MalformedStamp"/>), the version is below the
    /// readable boundary or newer than this build (<see cref="DocumentSchemaVersionFailure.TooOld"/> or
    /// <see cref="DocumentSchemaVersionFailure.Future"/>), content is invalid for the declared/current
    /// shape (<see cref="DocumentSchemaVersionFailure.InvalidContent"/>). Policy, format, and chain
    /// configuration failures are raised eagerly when the default serializer is constructed.
    /// </exception>
    T Deserialize<T>(DocumentEnvelope envelope);
}
