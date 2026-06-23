using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Stages a set of document writes across aggregates and commits them atomically through a Groundwork
/// <see cref="IDocumentUnitOfWork"/>. All-or-nothing is caller-enforced per the Groundwork document-UoW contract:
/// any non-<see cref="DocumentStoreWriteStatus.Saved"/> result (or exception) aborts before commit, and the
/// <c>await using</c> dispose rolls the transaction back. Used by the multi-document design write commands so a
/// definition and its first child (draft/version) land together or not at all.
/// </summary>
public static class GroundworkAtomicWrite
{
    public static async Task SaveAllAsync(
        IDocumentStore store,
        DocumentCommitScope scope,
        IReadOnlyList<SaveDocumentRequest> saves,
        CancellationToken cancellationToken = default)
    {
        await using var unitOfWork = await store.BeginAsync(scope, cancellationToken);

        foreach (var save in saves)
        {
            var result = await unitOfWork.SaveAsync(save, cancellationToken);

            // Caller-enforced all-or-nothing: bail before commit so dispose rolls the staged writes back.
            if (result.Status != DocumentStoreWriteStatus.Saved)
                throw new GroundworkAtomicWriteException(save.DocumentKind, save.Id, result.Status);
        }

        await unitOfWork.CommitAsync(cancellationToken);
    }
}
