using Elsa.Connections.Core.Contracts;

namespace Elsa.Connections.Services;

public sealed class NullConnectionPublishTenantAccessor : IConnectionPublishTenantAccessor
{
    public string? TenantId => null;
}

public sealed class FixedConnectionPublishTenantAccessor(string tenantId) : IConnectionPublishTenantAccessor
{
    public string? TenantId => tenantId;
}
