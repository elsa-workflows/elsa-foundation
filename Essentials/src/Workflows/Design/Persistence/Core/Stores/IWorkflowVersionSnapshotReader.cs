using System.Text;

namespace Elsa.Workflows.Design.Persistence.Core.Stores;

public sealed class WorkflowVersionSnapshotLimits
{
    public int MaxSourceBytes { get; init; } = 32 * 1024 * 1024;
    public int MaxIdentityBytes { get; init; } = 4096;
    public void Validate()
    {
        if (MaxSourceBytes is < 1 or > 64 * 1024 * 1024 || MaxIdentityBytes is < 1 or > 16 * 1024)
            throw new ArgumentOutOfRangeException(nameof(WorkflowVersionSnapshotLimits));
    }
}

/// <summary>Exact immutable selected version metadata and its original persisted StateSource text, without deserialization.</summary>
public sealed class WorkflowVersionSnapshot
{
    public WorkflowVersionSnapshot(string definitionId, string versionId, string version, string? tenantId, string stateSource,
        WorkflowVersionSnapshotLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits); limits.Validate(); var utf8 = new UTF8Encoding(false, true);
        try
        {
            foreach (var value in new[] { definitionId, versionId, version })
                if (string.IsNullOrWhiteSpace(value) || utf8.GetByteCount(value) > limits.MaxIdentityBytes) throw new WorkflowVersionSnapshotException("workflow_version_snapshot_identity_invalid");
            if (tenantId is not null && utf8.GetByteCount(tenantId) > limits.MaxIdentityBytes || string.IsNullOrWhiteSpace(stateSource) || utf8.GetByteCount(stateSource) > limits.MaxSourceBytes)
                throw new WorkflowVersionSnapshotException("workflow_version_snapshot_byte_limit");
        }
        catch (EncoderFallbackException) { throw new WorkflowVersionSnapshotException("workflow_version_snapshot_unicode_invalid"); }
        DefinitionId = definitionId; VersionId = versionId; Version = version; TenantId = tenantId; StateSource = stateSource;
    }
    public string DefinitionId { get; }
    public string VersionId { get; }
    public string Version { get; }
    public string? TenantId { get; }
    public string StateSource { get; }
}

/// <summary>Replacement owner port: check actual stored types and UTF8 lengths before hydrating the source document.</summary>
/// <remarks>Expected partition is compared with the actual selected source accessor and row identity; it grants no
/// authorization. A complete copied snapshot may outlive the short read transaction. Missing qualification refuses.</remarks>
public interface IWorkflowVersionSnapshotReader
{
    ValueTask<WorkflowVersionSnapshot?> FindAsync(string versionId, string expectedPartition, WorkflowVersionSnapshotLimits limits,
        CancellationToken cancellationToken = default);
}

public sealed class WorkflowVersionSnapshotException(string code, bool unsupported = false) : Exception("Workflow version snapshot was refused (" + code + ").")
{
    public string Code { get; } = code;
    public bool Unsupported { get; } = unsupported;
}
