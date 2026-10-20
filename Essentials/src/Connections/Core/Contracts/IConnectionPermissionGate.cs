namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Authorizes connection create, update, delete, revoke, test, and secret-field binding. The default is a no-op so
/// hosts without Identity still compose; the API replaces it with a principal-backed gate.
/// </summary>
/// <remarks>
/// Binding a connection in a workflow is not a connection right, so it has no method here. The routes that write and
/// promote workflow state authorize it through <c>workflows/design:manage</c>; draft validation and runtime resolution
/// never ask the caller.
/// </remarks>
public interface IConnectionPermissionGate
{
    /// <summary>Required to create, update, delete, or revoke a connection. The HTTP gate refuses when there is no principal.</summary>
    ValueTask EnsureManageAsync(string tenantId, CancellationToken cancellationToken = default);

    /// <summary>Required to test a connection.</summary>
    ValueTask EnsureTestAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default);
}
