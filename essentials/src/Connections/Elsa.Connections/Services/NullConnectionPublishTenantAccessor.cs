using Elsa.Connections.Core.Contracts;

namespace Elsa.Connections.Services;

internal sealed class NullConnectionPublishTenantAccessor : IConnectionPublishTenantAccessor
{
    public string? TenantId => null;
}

internal sealed class FixedConnectionPublishTenantAccessor(string tenantId) : IConnectionPublishTenantAccessor
{
    public string? TenantId => tenantId;
}
