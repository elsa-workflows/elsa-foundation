namespace Elsa.Integrations.Sdk.Connections;

/// <summary>
/// Creates a tenant-bound <see cref="IConnectionContext"/>. There is no general tenant service in
/// v1, so the caller (host or harness) supplies the tenant id.
/// </summary>
public interface IConnectionContextFactory
{
    IConnectionContext Create(string tenantId);
}
