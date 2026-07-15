using System.Text.Json;
using Elsa.Persistence.Core;
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
        return JsonDocumentStoreExtensions.ToSaveDocumentRequest(
            documentKind,
            entity.Id,
            schemaVersion,
            new GroundworkDocument<TEntity>(collection, entity),
            jsonOptions);
    }

    /// <summary>
    /// Validates a tenant-bearing entity against the active provider-neutral persistence context
    /// before producing a request that can be staged by a provider.
    /// </summary>
    public static SaveDocumentRequest ToTenantScopedSaveRequest<TEntity>(
        string documentKind,
        string collection,
        string schemaVersion,
        TEntity entity,
        JsonSerializerOptions jsonOptions,
        PersistenceAccessContext accessContext)
        where TEntity : TenantEntity
    {
        ArgumentNullException.ThrowIfNull(accessContext);
        accessContext.EnsureTenantScope(entity.TenantId);
        return ToSaveRequest(documentKind, collection, schemaVersion, entity, jsonOptions);
    }

    /// <summary>Produces an unconditional delete request for the given document kind and id.</summary>
    public static DeleteDocumentRequest ToDeleteRequest(string documentKind, string id) =>
        new(documentKind, id, null);
}
