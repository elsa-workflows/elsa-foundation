using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Documents.Store;

namespace Elsa.Foundation.Identity.Persistence.Groundwork.Stores;

public sealed class GroundworkIdentityAuthority(IGroundworkStoreSessionFactory sessionFactory)
{
    public async ValueTask<DocumentEnvelope?> LoadAsync(
        string documentKind,
        string documentId,
        CancellationToken cancellationToken = default)
    {
        await using var session = await sessionFactory.CreateAsync(PersistenceAccessPolicy.Ordinary, cancellationToken);
        return await session.DocumentStore.LoadAsync(documentKind, documentId, cancellationToken);
    }
}
