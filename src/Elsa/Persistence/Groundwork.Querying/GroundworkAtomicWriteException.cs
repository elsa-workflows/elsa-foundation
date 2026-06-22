using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Thrown when a staged document write in a <see cref="GroundworkAtomicWrite"/> batch returns a non-success
/// status (e.g. <see cref="DocumentStoreWriteStatus.ConcurrencyConflict"/>). The surrounding unit of work has
/// already been rolled back, so no partial state is persisted.
/// </summary>
public sealed class GroundworkAtomicWriteException(string documentKind, string id, DocumentStoreWriteStatus status)
    : InvalidOperationException($"Atomic write aborted: document '{id}' of kind '{documentKind}' returned status '{status}'.")
{
    public string DocumentKind { get; } = documentKind;
    public string Id { get; } = id;
    public DocumentStoreWriteStatus Status { get; } = status;
}
