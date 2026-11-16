namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>Finite source read/closure budgets. They grant no scope, source authority, import or execution permission.</summary>
public sealed class RuntimeSourceReadLimits
{
    public int MaxPayloadBytes { get; init; } = 32 * 1024 * 1024;
    public int MaxMetadataBytes { get; init; } = 4096;
    public long MaxAggregatePayloadBytes { get; init; } = 256 * 1024 * 1024;
    public int MaxJsonDepth { get; init; } = 128;
    public int MaxMaterials { get; init; } = 10_000;
    public int MaxDependencies { get; init; } = 100_000;
    public int MaxDependencyDepth { get; init; } = 128;
    public void Validate()
    {
        if (MaxPayloadBytes is < 1 or > 64 * 1024 * 1024 || MaxMetadataBytes is < 1 or > 64 * 1024 || MaxAggregatePayloadBytes < MaxPayloadBytes || MaxAggregatePayloadBytes > 512L * 1024 * 1024 ||
            MaxJsonDepth is < 1 or > 128 || MaxMaterials is < 1 or > 10_000 || MaxDependencies is < 1 or > 100_000 || MaxDependencyDepth is < 1 or > 128)
            throw new ArgumentOutOfRangeException(nameof(RuntimeSourceReadLimits));
    }
}

/// <summary>A bounded owner decode from one actual physical source namespace. Legacy typed views must not be mutated.</summary>
public sealed class RuntimeMaterialSourceSnapshot
{
    public RuntimeMaterialSourceSnapshot(RuntimeExecutionMaterial material, PersistenceScope scope, int storedPayloadBytes)
    { ArgumentNullException.ThrowIfNull(material); ArgumentNullException.ThrowIfNull(scope); ArgumentOutOfRangeException.ThrowIfNegative(storedPayloadBytes); Material = material; Scope = scope; StoredPayloadBytes = storedPayloadBytes; }
    public RuntimeExecutionMaterial Material { get; }
    public PersistenceScope Scope { get; }
    public int StoredPayloadBytes { get; }
}
public sealed class RuntimeTemplateSourceSnapshot
{
    public RuntimeTemplateSourceSnapshot(ExecutableActivityTemplate template, PersistenceScope scope, int storedPayloadBytes)
    { ArgumentNullException.ThrowIfNull(template); ArgumentNullException.ThrowIfNull(scope); ArgumentOutOfRangeException.ThrowIfNegative(storedPayloadBytes); Template = template; Scope = scope; StoredPayloadBytes = storedPayloadBytes; }
    public ExecutableActivityTemplate Template { get; }
    public PersistenceScope Scope { get; }
    public int StoredPayloadBytes { get; }
}
public sealed class RuntimeReferenceSourceSnapshot
{
    public RuntimeReferenceSourceSnapshot(WorkflowExecutableSourceReference reference, PersistenceScope scope, int storedPayloadBytes)
    { ArgumentNullException.ThrowIfNull(reference); ArgumentNullException.ThrowIfNull(scope); ArgumentOutOfRangeException.ThrowIfNegative(storedPayloadBytes); Reference = reference; Scope = scope; StoredPayloadBytes = storedPayloadBytes; }
    public WorkflowExecutableSourceReference Reference { get; }
    public PersistenceScope Scope { get; }
    public int StoredPayloadBytes { get; }
}

/// <summary>Value-free refusal of a bounded source profile; provider failures retain their existing owner boundary.</summary>
public sealed class RuntimeSourceReadException(string code, bool unsupported = false) : Exception("Runtime source read was refused (" + code + ").")
{
    public string Code { get; } = code;
    public bool Unsupported { get; } = unsupported;
}
