namespace Elsa.Integrations.Sdk;

/// <summary>
/// Tenant-scoped resolver that yields a short-lived handle plus an authenticated <see cref="HttpClient"/>.
/// The SDK never returns raw long-lived secrets.
/// </summary>
internal interface IConnectionContext
{
    string TenantId { get; }

    ValueTask<IConnectionHandle> ResolveAsync(ConnectionReference reference, CancellationToken cancellationToken = default);
}
