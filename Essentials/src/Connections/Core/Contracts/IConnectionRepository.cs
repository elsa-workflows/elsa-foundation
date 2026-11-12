using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Persistence of connection definition and state. The default is in-memory; a durable package
/// replaces it. Lookups are tenant-scoped.
/// </summary>
public interface IConnectionRepository
{
    /// <summary>Reports the selected metadata backend. Unknown providers do not imply restart persistence.</summary>
    ValueTask<ConnectionRepositoryCapabilities> GetCapabilitiesAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new ConnectionRepositoryCapabilities("unspecified", false));
    }

    ValueTask<ConnectionDocument?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks persisted connection fields for a tenant-scoped secret reference, optionally excluding
    /// the connection whose next state the caller is preparing. This is an existence check, not an
    /// atomic guard against a concurrent rebind and delete.
    /// </summary>
    ValueTask<bool> IsSecretReferencedAsync(
        string tenantId,
        string secretName,
        string? excludedConnectionName = null,
        CancellationToken cancellationToken = default);

    ValueTask<IReadOnlyList<ConnectionDocument>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Adds the document when the tenant holds no connection under its name, and returns false, adding nothing, when it
    /// does. The document is stored at its own <see cref="ConnectionState.Version"/>, unchanged, so a caller that read the
    /// version before the add can delete the document with
    /// <see cref="DeleteAsync(string, string, int, CancellationToken)"/> at that version. The manager rolls a failed create
    /// back that way; a repository that bumped the version on insert would leave every failed create's row behind.
    /// </summary>
    ValueTask<bool> TryAddAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    /// <summary>
    /// Saves a document with compare-and-swap on <see cref="ConnectionState.Version"/>. The save succeeds
    /// only when the stored version equals the document's, and then stores the document with the version
    /// incremented by one. Otherwise, including when the stored document was deleted, it throws
    /// <see cref="Exceptions.ConnectionConflictException"/> and writes nothing. The manager relies on this
    /// so that a concurrent update, revoke or test never overwrites another one. Whether the new version is
    /// also written back onto <paramref name="document"/> is up to the implementation, so read the document
    /// again before saving it a second time.
    /// </summary>
    ValueTask SaveAsync(ConnectionDocument document, CancellationToken cancellationToken = default);

    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>
    /// Deletes the document only while its stored <see cref="ConnectionState.Version"/> equals
    /// <paramref name="expectedVersion"/>, checking and deleting atomically. Returns false, and deletes nothing, when
    /// there is no document or it is at another version. The manager rolls a failed create back with it, so the
    /// rollback cannot remove a document that another request has saved since.
    /// </summary>
    ValueTask<bool> DeleteAsync(string tenantId, string name, int expectedVersion, CancellationToken cancellationToken = default);
}
