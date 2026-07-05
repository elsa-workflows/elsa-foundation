using System.Text.Json;
using Groundwork.Documents.Store;

namespace Elsa.Workflows.Runtime.Distributed.Persistence.Groundwork;

/// <summary>
/// Owns the frozen serializer options and the schema-version stamp of the distributed persistence bridge. Web
/// defaults emit camelCase property names, so the declared keyword index fields (<c>workflowExecutionId</c>,
/// <c>collection</c>) match the serialized JSON the providers index, and the nested transport item / placement
/// lease serialize in exactly the frozen v1 wire shape pinned by the leaf's committed golden fixtures.
/// </summary>
/// <remarks>
/// Every document is stamped with <see cref="DistributedGroundworkStorageManifest.SchemaVersion"/> on write, and
/// reads fail loudly on any other stamp instead of silently deserializing with default values. There is no
/// upcaster chain yet because only v1 exists; introducing v2 requires a version bump, an upcaster, and a new
/// versioned golden fixture alongside the kept v1 fixture.
/// </remarks>
public static class DistributedGroundworkDocuments
{
    public static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    public static string Serialize<T>(T document) => JsonSerializer.Serialize(document, SerializerOptions);

    public static T Deserialize<T>(DocumentEnvelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.Equals(envelope.SchemaVersion, DistributedGroundworkStorageManifest.SchemaVersion, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Document '{envelope.Id}' of kind '{envelope.DocumentKind}' carries schema version '{envelope.SchemaVersion}', " +
                $"but this build only understands '{DistributedGroundworkStorageManifest.SchemaVersion}'. Refusing to deserialize.");
        }

        return JsonSerializer.Deserialize<T>(envelope.ContentJson, SerializerOptions)
               ?? throw new InvalidOperationException(
                   $"Document '{envelope.Id}' of kind '{envelope.DocumentKind}' deserialized to null content.");
    }
}
