namespace Elsa.Workflows.Publishing.Api.Contracts;

/// <summary>
/// Request-scoped authorization boundary for Publishing operations that dereference exact Design
/// identities. Endpoint permission checks authorize the operation; this seam constrains which tenant's
/// identities that operation may read.
/// </summary>
public interface IActivityPublishingAuthorizationContext
{
    string? TenantId { get; }

    bool CanAccessTenant(string? tenantId);
}
