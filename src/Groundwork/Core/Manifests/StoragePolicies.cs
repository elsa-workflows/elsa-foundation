namespace Groundwork.Core.Manifests;

public sealed record LifecyclePolicy(StorageLifecycleKind Kind)
{
    public static LifecyclePolicy Mutable { get; } = new(StorageLifecycleKind.Mutable);
    public static LifecyclePolicy AppendOnly { get; } = new(StorageLifecycleKind.AppendOnly);
}

public enum StorageLifecycleKind
{
    Mutable,
    AppendOnly,
    DraftPublishedRetired,
    Retained
}

public sealed record IdentityPolicy(StorageIdentityKind Kind, string FieldName)
{
    public static IdentityPolicy StringId(string fieldName = "id") => new(StorageIdentityKind.String, fieldName);
}

public enum StorageIdentityKind
{
    String,
    Guid,
    Composite
}

public sealed record TenancyPolicy(TenancyKind Kind, string? PartitionField = null)
{
    public static TenancyPolicy None { get; } = new(TenancyKind.None);
    public static TenancyPolicy TenantPartition(string fieldName = "tenantId") => new(TenancyKind.TenantPartition, fieldName);
}

public enum TenancyKind
{
    None,
    TenantPartition,
    CustomPartition
}

public sealed record ConcurrencyPolicy(ConcurrencyKind Kind, string? TokenField = null)
{
    public static ConcurrencyPolicy None { get; } = new(ConcurrencyKind.None);
    public static ConcurrencyPolicy Optimistic(string tokenField = "version") => new(ConcurrencyKind.Optimistic, tokenField);
}

public enum ConcurrencyKind
{
    None,
    Optimistic,
    AppendOnly
}

public sealed record SerializationPolicy(SerializationKind Kind, string? SchemaField = null)
{
    public static SerializationPolicy Json(string schemaField = "schemaVersion") => new(SerializationKind.Json, schemaField);
}

public enum SerializationKind
{
    Json,
    Binary,
    ProviderNative
}

public sealed record PhysicalizationPolicy(PhysicalizationKind Kind)
{
    public static PhysicalizationPolicy Portable { get; } = new(PhysicalizationKind.Portable);
    public static PhysicalizationPolicy Optimized { get; } = new(PhysicalizationKind.Optimized);
    public static PhysicalizationPolicy Specialized { get; } = new(PhysicalizationKind.Specialized);
}

public enum PhysicalizationKind
{
    Portable,
    Optimized,
    Specialized
}
