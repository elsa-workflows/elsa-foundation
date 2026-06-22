using System.Text.Json;
using Elsa.Primitives.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Builds Groundwork document write requests from domain entities, the write-side counterpart of
/// <see cref="GroundworkReadStore{TEntity}"/>. It wraps an entity in the same <see cref="GroundworkDocument{TEntity}"/>
/// envelope the read store expects — constant <c>Collection</c> partition value plus the entity payload — and
/// serializes it with the lane's JSON options, so a document written here reads back through the matching read
/// store unchanged. Keeping read and write on one envelope shape is what lets a Groundwork-backed write command
/// and its read port stay in sync.
/// </summary>
public static class GroundworkDocumentWriter
{
    /// <summary>Wraps <paramref name="entity"/> in a <see cref="GroundworkDocument{TEntity}"/> and produces a save request keyed by the entity id.</summary>
    public static SaveDocumentRequest ToSaveRequest<TEntity>(
        string documentKind,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions)
        where TEntity : Entity
    {
        var document = new GroundworkDocument<TEntity>(collection, entity);
        var content = JsonSerializer.Serialize(document, jsonOptions);
        return new SaveDocumentRequest(documentKind, entity.Id, schemaVersion, content);
    }

    /// <summary>Produces an unconditional delete request for the given document kind and id.</summary>
    public static DeleteDocumentRequest ToDeleteRequest(string documentKind, string id) =>
        new(documentKind, id, null);
}
