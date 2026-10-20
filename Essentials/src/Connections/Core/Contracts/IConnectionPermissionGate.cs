namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Authorizes connection create, update, delete, revoke, test, use, and secret-field binding. The
/// default is a no-op so hosts without Identity still compose; the API replaces it with a
/// principal-backed gate.
/// </summary>
public interface IConnectionPermissionGate
{
    /// <summary>Required to create, update, delete, or revoke a connection. The HTTP gate refuses when there is no principal.</summary>
    ValueTask EnsureManageAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Required to publish a workflow that binds a connection. Recorded against the publisher at
    /// publish time. Runtime of an already-published workflow does not call this against the run trigger.
    /// </summary>
    ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Required to test a connection. Distinct from <see cref="EnsureUseAsync"/>.</summary>
    ValueTask EnsureTestAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default);
}
