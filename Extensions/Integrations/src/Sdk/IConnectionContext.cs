namespace Elsa.Integrations.Sdk;

/// <summary>
/// Tenant-scoped resolver that yields a short-lived handle. Secret material is resolved per
/// activation through <c>ISecretValueResolver</c>; this context does not resolve or cache secrets
/// itself (#2726 rule 5). Owner/run-as is reserved; only tenant scope is valid in v1 (plan D5).
/// </summary>
public interface IConnectionContext
{
    string TenantId { get; }

    ValueTask<IConnectionHandle> ResolveAsync(ConnectionReference reference, CancellationToken cancellationToken = default);
}
