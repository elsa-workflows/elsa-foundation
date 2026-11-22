namespace Elsa.Foundation.Identity.Core.Authentication;

/// <summary>An authentication persistence operation failed; a mutation may have committed before acknowledgement failed.</summary>
public sealed class AuthenticationStoreException(string operation, string tenantId, string? recordId, Exception innerException)
    : InvalidOperationException($"Authentication persistence failed during {operation}.", innerException)
{
    public string Operation { get; } = operation;
    public string TenantId { get; } = tenantId;
    public string? RecordId { get; } = recordId;
}
