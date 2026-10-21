using Elsa.Connections.Core.Exceptions;
using Elsa.Connections.Core.Models;

namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Lifecycle facade for service connections. Runtime value resolution lives on <see cref="IConnectionResolver"/>.
/// Every write member asks the permission gate before it writes anything, and the gate throws
/// <see cref="ConnectionUnavailableException"/> when the caller is refused. Create and Update validate the request
/// first, so an invalid request is an <see cref="ArgumentException"/> whatever the caller's permissions. Revoke,
/// Delete and Test ask the gate first.
/// </summary>
public interface IConnectionManager
{
    /// <summary>Creates a connection and writes the secrets of its inline credential fields.</summary>
    /// <exception cref="ArgumentException">
    /// The name, owner, type, scheme, settings or credential sources are invalid, a referenced secret is unavailable, or an
    /// existing secret holds the name of a managed credential (including one a concurrent writer created).
    /// </exception>
    /// <exception cref="ConnectionConflictException">
    /// The name is already in use in the tenant, or the save lost a compare-and-swap against a concurrent change.
    /// </exception>
    /// <exception cref="ConnectionUnavailableException">The permission gate refused the caller.</exception>
    /// <exception cref="ConnectionStoreException">
    /// The connection or secret store failed before the create was confirmed saved. The create removes what it wrote on a
    /// best-effort basis (a step that fails is logged), but keeps a connection whose save committed before the store failed.
    /// Retry unless the message says the store configuration must be fixed.
    /// </exception>
    ValueTask<ConnectionMetadata> CreateAsync(string tenantId, CreateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Finds a connection, or returns null when there is none.</summary>
    ValueTask<ConnectionMetadata?> FindAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Lists the tenant's connections that match the query.</summary>
    ValueTask<IReadOnlyList<ConnectionMetadata>> ListAsync(string tenantId, ConnectionQuery? query = null, CancellationToken cancellationToken = default);

    /// <summary>Updates a connection, or returns null when there is none.</summary>
    /// <exception cref="ArgumentException">
    /// The settings or credential sources are invalid, a referenced secret is unavailable, or an existing secret holds the name of
    /// a managed credential (including one a concurrent writer created).
    /// </exception>
    /// <exception cref="ConnectionConflictException">
    /// The save lost a compare-and-swap against a concurrent change and nothing was saved, or the update was saved but a credential
    /// changed by another request, so its new value was not written (the message names the fields).
    /// </exception>
    /// <exception cref="ConnectionUnavailableException">The permission gate refused the caller.</exception>
    /// <exception cref="ConnectionStoreException">
    /// The connection or secret store failed before the update was confirmed saved, so the update is not confirmed saved. Retry
    /// unless the message says the store configuration must be fixed. A secret the update created for a field moving to inline
    /// storage is removed on a best-effort basis, unless the stored connection references it.
    /// </exception>
    /// <exception cref="ConnectionCredentialWriteException">
    /// A credential write or the cleanup of a replaced secret failed after the update committed; the message says the update was saved.
    /// </exception>
    ValueTask<ConnectionMetadata?> UpdateAsync(string tenantId, string name, UpdateConnectionRequest request, CancellationToken cancellationToken = default);

    /// <summary>Tests a connection. A missing or unavailable connection is reported in the result rather than thrown.</summary>
    /// <exception cref="ConnectionUnavailableException">The permission gate refused the caller.</exception>
    /// <exception cref="ConnectionConflictException">Recording the test result lost a compare-and-swap against a concurrent change, such as a revoke.</exception>
    ValueTask<ConnectionTestResult> TestAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Revokes a connection, or returns null when there is none.</summary>
    /// <exception cref="ConnectionUnavailableException">The permission gate refused the caller.</exception>
    /// <exception cref="ConnectionConflictException">The save lost a compare-and-swap against a concurrent change.</exception>
    ValueTask<ConnectionMetadata?> RevokeAsync(string tenantId, string name, CancellationToken cancellationToken = default);

    /// <summary>Deletes a connection and the secrets it owns, and returns false when there is none.</summary>
    /// <exception cref="ConnectionUnavailableException">The permission gate refused the caller.</exception>
    ValueTask<bool> DeleteAsync(string tenantId, string name, CancellationToken cancellationToken = default);
}
