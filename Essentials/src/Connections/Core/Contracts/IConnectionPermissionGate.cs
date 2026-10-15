namespace Elsa.Connections.Core.Contracts;

/// <summary>
/// Authorizes connection use and secret-field binding. The default is a no-op so hosts without
/// Identity still compose; the API replaces it with a principal-backed gate.
/// </summary>
public interface IConnectionPermissionGate
{
    ValueTask EnsureUseAsync(string tenantId, CancellationToken cancellationToken = default);

    ValueTask EnsureBindSecretAsync(string tenantId, CancellationToken cancellationToken = default);
}
