namespace Elsa.Workflows.Runtime.Core.Models;

/// <summary>One owner command's durable lifecycle. Staging grants no fresh-start authority.</summary>
public enum RuntimeMaterialStageStatus { Ready = 0, Committed = 1, Revoked = 2 }

/// <summary>Exact material plus inert original provenance. The calling owner adapter verifies wrapper/body association.</summary>
/// <remarks>Null ExpectedIncarnationId means Missing, not adopt-any. Typed legacy graphs are frozen by the selected
/// owner before I/O. Original payload bytes are provenance, never target scope, authorization or execution authority.</remarks>
public sealed class RuntimeMaterialStageMember
{
    private readonly byte[] payload;
    public RuntimeMaterialStageMember(RuntimeDeploymentMember sourceMember, ReadOnlyMemory<byte> originalPayload,
        string? originalSourceReferenceId, string? originalIncarnationId, string? expectedIncarnationId,
        WorkflowExecutable? workflow = null, RuntimeModelMaterial? model = null, ExecutableActivityTemplate? template = null)
    {
        ArgumentNullException.ThrowIfNull(sourceMember);
        if (originalPayload.Length is <= 0 or > RuntimeMaterialStageCommand.MaximumMemberBytes)
            throw new RuntimeMaterialStagingException("runtime_stage_payload_limit");
        if ((workflow is null ? 0 : 1) + (model is null ? 0 : 1) + (template is null ? 0 : 1) != 1 ||
            sourceMember.Kind != (workflow is not null ? "workflow-executable" : model is not null ? "runtime-model" : "activity-template") ||
            sourceMember.Identity != (workflow?.Identity.ArtifactId ?? model?.Identity.ArtifactId ?? template!.TemplateId))
            throw new RuntimeMaterialStagingException("runtime_stage_member_identity_invalid");
        RuntimeDeploymentCaptureGuard.OptionalText(originalSourceReferenceId, 4096);
        RuntimeDeploymentCaptureGuard.OptionalText(originalIncarnationId, 4096);
        RuntimeDeploymentCaptureGuard.OptionalText(expectedIncarnationId, 4096);
        SourceMember = sourceMember; payload = originalPayload.ToArray(); OriginalSourceReferenceId = originalSourceReferenceId;
        OriginalIncarnationId = originalIncarnationId; ExpectedIncarnationId = expectedIncarnationId;
        Workflow = workflow; Model = model; Template = template;
    }
    public RuntimeDeploymentMember SourceMember { get; }
    public string? OriginalSourceReferenceId { get; }
    public string? OriginalIncarnationId { get; }
    public string? ExpectedIncarnationId { get; }
    public WorkflowExecutable? Workflow { get; }
    public RuntimeModelMaterial? Model { get; }
    public ExecutableActivityTemplate? Template { get; }
    public int OriginalPayloadBytes => payload.Length;
    public byte[] GetOriginalPayloadBytes() => (byte[])payload.Clone();
}

/// <summary>A fixed complete Runtime-owned closure batch; ETL and catalog material belong to their other owners.</summary>
public sealed class RuntimeMaterialStageCommand
{
    public const int MaximumMembers = 128;
    public const int MaximumMemberBytes = 4 * 1024 * 1024;
    public const int MaximumBatchBytes = 32 * 1024 * 1024;
    public RuntimeMaterialStageCommand(string commandId, string attemptId, string actorId, RuntimeDeploymentScope targetScope,
        IReadOnlyList<RuntimeMaterialStageMember> members)
    {
        RuntimeDeploymentCaptureGuard.Text(commandId, 256); RuntimeDeploymentCaptureGuard.Text(attemptId, 256);
        RuntimeDeploymentCaptureGuard.Text(actorId, 256); ArgumentNullException.ThrowIfNull(targetScope);
        ArgumentNullException.ThrowIfNull(members);
        if (members.Count is <= 0 or > MaximumMembers) throw new RuntimeMaterialStagingException("runtime_stage_member_limit");
        var copy = new List<RuntimeMaterialStageMember>(); var identities = new HashSet<RuntimeDeploymentMember>(); long bytes = 0;
        foreach (var member in members)
        {
            if (copy.Count >= MaximumMembers || member is null || !identities.Add(member.SourceMember) ||
                (bytes += member.OriginalPayloadBytes) > MaximumBatchBytes)
                throw new RuntimeMaterialStagingException("runtime_stage_member_limit");
            copy.Add(member);
        }
        if (copy.Count != members.Count) throw new RuntimeMaterialStagingException("runtime_stage_member_limit");
        CommandId = commandId; AttemptId = attemptId; ActorId = actorId; TargetScope = targetScope; Members = copy.AsReadOnly();
    }
    public string CommandId { get; }
    public string AttemptId { get; }
    public string ActorId { get; }
    public RuntimeDeploymentScope TargetScope { get; }
    public IReadOnlyList<RuntimeMaterialStageMember> Members { get; }
}

/// <summary>Immutable owner evidence, separating original wrapper identity from the actual frozen owner body.</summary>
/// <remarks>IncarnationId identifies physical material. It is never a target source-reference ID.</remarks>
public sealed class RuntimeMaterialStageResult
{
    public RuntimeMaterialStageResult(RuntimeDeploymentMember sourceMember, string originalPayloadDigest,
        string ownerBodyDigest, string incarnationId, WorkflowExecutableIdentity? originalPin, string? templateHash,
        string? originalSourceReferenceId, string? originalIncarnationId)
    {
        ArgumentNullException.ThrowIfNull(sourceMember); RuntimeDeploymentCaptureGuard.Digest(originalPayloadDigest);
        RuntimeDeploymentCaptureGuard.Digest(ownerBodyDigest); RuntimeDeploymentCaptureGuard.Text(incarnationId, 4096);
        if (sourceMember.Kind is not ("workflow-executable" or "runtime-model" or "activity-template"))
            throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid");
        RuntimeDeploymentCaptureGuard.OptionalText(originalSourceReferenceId, 4096); RuntimeDeploymentCaptureGuard.OptionalText(originalIncarnationId, 4096);
        if (sourceMember.Kind == "activity-template")
        { if (originalPin is not null || templateHash is null) throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid"); RuntimeDeploymentCaptureGuard.Text(templateHash, 4096); }
        else
        { if (templateHash is not null || originalPin is null || originalPin.ArtifactId != sourceMember.Identity) throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid"); RuntimeDeploymentCaptureGuard.Pin(originalPin); }
        SourceMember = sourceMember; OriginalPayloadDigest = originalPayloadDigest; OwnerBodyDigest = ownerBodyDigest;
        IncarnationId = incarnationId; OriginalPin = originalPin is null ? null : originalPin with { }; TemplateHash = templateHash;
        OriginalSourceReferenceId = originalSourceReferenceId; OriginalIncarnationId = originalIncarnationId;
    }
    public RuntimeDeploymentMember SourceMember { get; }
    public string OriginalPayloadDigest { get; }
    public string OwnerBodyDigest { get; }
    public string IncarnationId { get; }
    public WorkflowExecutableIdentity? OriginalPin { get; }
    public string? TemplateHash { get; }
    public string? OriginalSourceReferenceId { get; }
    public string? OriginalIncarnationId { get; }
}

public sealed class RuntimeMaterialStageReceipt
{
    public RuntimeMaterialStageReceipt(string commandId, string fingerprint, long fence, RuntimeMaterialStageStatus status,
        IReadOnlyList<RuntimeMaterialStageResult> materials)
    {
        RuntimeDeploymentCaptureGuard.Text(commandId, 256); RuntimeDeploymentCaptureGuard.Digest(fingerprint);
        if (fence <= 0 || !Enum.IsDefined(status) || materials is null || materials.Count > RuntimeMaterialStageCommand.MaximumMembers ||
            (status == RuntimeMaterialStageStatus.Committed ? materials.Count == 0 : materials.Count != 0))
            throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid");
        var copy = new List<RuntimeMaterialStageResult>(); var identities = new HashSet<RuntimeDeploymentMember>();
        foreach (var material in materials)
        {
            if (copy.Count >= RuntimeMaterialStageCommand.MaximumMembers || material is null || !identities.Add(material.SourceMember))
                throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid");
            copy.Add(material);
        }
        if (copy.Count != materials.Count) throw new RuntimeMaterialStagingException("runtime_stage_receipt_invalid");
        CommandId = commandId; Fingerprint = fingerprint; Fence = fence; Status = status; Materials = copy.AsReadOnly();
    }
    public string CommandId { get; }
    public string Fingerprint { get; }
    public long Fence { get; }
    public RuntimeMaterialStageStatus Status { get; }
    public IReadOnlyList<RuntimeMaterialStageResult> Materials { get; }
}

/// <summary>Value-free owner refusal. Unknown infrastructure/commit outcomes retain their actual persistence cause.</summary>
public sealed class RuntimeMaterialStagingException(string code, bool isUnsupported = false)
    : Exception($"Runtime material staging was refused ({code}).")
{
    public string Code { get; } = code;
    public bool IsUnsupported { get; } = isUnsupported;
}

/// <summary>Commit acknowledgement is uncertain. Reconcile this exact command; cancellation is not proof of rollback.</summary>
public sealed class RuntimeMaterialStageCommitUnknownException(string commandId, string fingerprint, Exception innerException)
    : Exception("Runtime material staging commit is unknown; reconcile the exact owner command receipt.", innerException)
{
    public string CommandId { get; } = commandId;
    public string Fingerprint { get; } = fingerprint;
}

/// <summary>Physical inert retention discovered by the actual material owner; never a source/publication reference.</summary>
public sealed class RuntimeMaterialStageRetentionRoot
{
    public RuntimeMaterialStageRetentionRoot(string kind, string materialId, string incarnationId)
    {
        if (kind is not ("workflow-executable" or "runtime-model" or "activity-template")) throw new RuntimeMaterialStagingException("runtime_stage_retention_invalid");
        RuntimeDeploymentCaptureGuard.Text(materialId, 4096); RuntimeDeploymentCaptureGuard.Text(incarnationId, 4096);
        Kind = kind; MaterialId = materialId; IncarnationId = incarnationId;
    }
    public string Kind { get; }
    public string MaterialId { get; }
    public string IncarnationId { get; }
}
