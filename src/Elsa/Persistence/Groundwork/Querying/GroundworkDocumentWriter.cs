using System.Text.Json;
using Elsa.Persistence.Core;
using Elsa.Persistence.Core.Design;
using Elsa.Primitives.Entities;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Builds Groundwork document write requests from domain entities. It wraps an entity in a
/// <see cref="GroundworkDocument{TEntity}"/> envelope — constant <c>Collection</c> value stamped into the
/// canonical JSON plus the entity payload — and serializes it with the lane's JSON options, so a document
/// written here reads back through the matching bounded design routes unchanged. Keeping read and write on one
/// envelope shape is what lets a Groundwork-backed write command and its read port stay in sync.
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
        PersistenceAccessContext accessContext,
        DesignPersistenceDomain? persistenceDomain = null,
        string? failureContext = null)
        where TEntity : TenantEntity
    {
        ArgumentNullException.ThrowIfNull(accessContext);
        accessContext.EnsureTenantScope(entity.TenantId);
        return persistenceDomain is null
            ? ToSaveRequest(documentKind, collection, schemaVersion, entity, jsonOptions)
            : GroundworkDesignSerialization.Execute(
                persistenceDomain.Value,
                "save",
                failureContext ?? documentKind,
                () => ToSaveRequest(documentKind, collection, schemaVersion, entity, jsonOptions));
    }

    /// <summary>Produces an unconditional delete request for the given document kind and id.</summary>
    public static DeleteDocumentRequest ToDeleteRequest(string documentKind, string id) =>
        new(documentKind, id, null);
}
