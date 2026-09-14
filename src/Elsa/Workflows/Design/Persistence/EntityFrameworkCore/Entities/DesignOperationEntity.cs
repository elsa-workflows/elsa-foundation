namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;

public sealed class DesignOperationEntity
{
    public long RowNumber { get; set; }
    public string TenantId { get; set; } = string.Empty;
    public string OperationKind { get; set; } = string.Empty;
    public string OperationKey { get; set; } = string.Empty;
    /// <summary>Provider-neutral exact identity material for <see cref="OperationKind"/>.</summary>
    public string OperationKindLookupHash { get; set; } = string.Empty;
    /// <summary>Provider-neutral exact identity material for <see cref="OperationKey"/>.</summary>
    public string OperationKeyLookupHash { get; set; } = string.Empty;
    public string RequestFingerprint { get; set; } = string.Empty;
    public string ResultFingerprint { get; set; } = string.Empty;
    public string ResultJson { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; }
}
