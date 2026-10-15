using Elsa.Connections.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts;

namespace Elsa.Connections.Design.Services;

/// <summary>
/// Publish-time tenant is the persistence partition already selected for the draft operation.
/// The API may replace this with a principal-based accessor; owner/run-as stays reserved (D5).
/// </summary>
public sealed class PersistenceScopeConnectionPublishTenantAccessor(IPersistenceAccessContextAccessor access)
    : IConnectionPublishTenantAccessor
{
    public string? TenantId => access.Current.Scope?.Value;
}
