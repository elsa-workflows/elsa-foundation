namespace Elsa.Diagnostics.StructuredLogs.Core.Models;

/// <summary>
/// Provider-neutral identity of the tenant, host storage scope and logical stream to which a concrete
/// structured-log store is bound. It is persistence routing context, not payload data or authorization.
/// </summary>
public sealed record StructuredLogStoreBinding(string TenantId, string ScopeId, string StreamId)
{
    public static StructuredLogStoreBinding Default { get; } = new("default", "default", "structured-logs");
}
