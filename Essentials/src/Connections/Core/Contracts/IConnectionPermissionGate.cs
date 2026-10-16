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
    /// Required to bind a connection in a workflow. <c>ConnectionDraftValidator</c> checks it against the
    /// current caller on every draft validation pass that meets a bound connection input, not only at
    /// publish. That includes read paths such as the validations endpoint and the dashboard's invalid-draft
    /// count. A refusal is an Error, so it blocks promotion and shows the draft as invalid to that caller.
    /// <see cref="IConnectionResolver"/> also checks it unless
    /// <see cref="Models.ConnectionResolveRequest.CheckUsePermission"/> is false, as it is for a published
    /// workflow's run and for a connection test. Nothing is recorded against the publisher.
    /// </summary>
    ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Required to test a connection. Distinct from <see cref="EnsureUseAsync"/>.</summary>
    ValueTask EnsureTestAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default);
}
