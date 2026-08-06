using System.Text.Json.Serialization;

namespace Elsa.Workflows.Runtime.Core.Models;

public enum RuntimeLogicalCheckpointLedgerStatus
{
    Prepared,
    Committed,
    Skipped,
    Failed
}

/// <summary>Bounded canonical input reservation for one logical checkpoint.</summary>
public sealed record RuntimeLogicalCheckpointLedgerEntry(
    string CommitId,
    string LedgerToken,
    RuntimeCheckpointProvenance Provenance,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? SourceIdentity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? OperationIdentity,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeCheckpoint? RawCheckpoint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeCheckpointStateChangeSet? RawStateChanges,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyList<RuntimePostCommitIntent>? RawPostCommitIntents,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] IReadOnlyDictionary<string, string>? RawCommitMetadata,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeExecutionContextSnapshot? RequestedExecutionContext,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedOrderRevision,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ExpectedContextRevision,
    RuntimeExecutionFence? ExpectedFence,
    string InputFingerprint,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CanonicalInputPayload,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? CanonicalInputReference,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeCheckpointPersistenceMode? InitialPersistenceMode,
    RuntimeLogicalCheckpointLedgerStatus Status,
    string? CommitFingerprint = null,
    RuntimeCheckpointCommitStoreResult? Receipt = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] bool? ReconciledLegacyMarker = false,
    string? WorkflowExecutionId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? TerminalFoldFingerprint = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeExecutionContextSnapshot? TerminalExecutionContext = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeCheckpointPreparationToken? TerminalPreparationToken = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeCheckpointRecoveryAuthority? RecoveryAuthority = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] RuntimeExecutionFence? CurrentAuthorityFence = null,
    long AuthorityRevision = 1)
{
    [JsonIgnore]
    public bool HasCanonicalRecoveryInput => Status == RuntimeLogicalCheckpointLedgerStatus.Prepared &&
                                             RawCheckpoint is not null &&
                                             CanonicalInputPayload is not null;

    public RuntimeLogicalCheckpointLedgerEntry CompactTerminal(
        RuntimeLogicalCheckpointLedgerStatus status,
        RuntimeCheckpointCommitStoreResult receipt,
        string? commitFingerprint,
        string foldFingerprint,
        string workflowExecutionId) => this with
    {
        Status = status,
        CommitFingerprint = commitFingerprint,
        Receipt = receipt,
        SourceIdentity = null,
        OperationIdentity = null,
        RawCheckpoint = null,
        RawStateChanges = null,
        RawPostCommitIntents = null,
        RawCommitMetadata = null,
        RequestedExecutionContext = null,
        ExpectedOrderRevision = null,
        ExpectedContextRevision = null,
        ExpectedFence = null,
        CanonicalInputPayload = null,
        CanonicalInputReference = null,
        InitialPersistenceMode = null,
        ReconciledLegacyMarker = null,
        WorkflowExecutionId = workflowExecutionId,
        TerminalFoldFingerprint = foldFingerprint,
        TerminalExecutionContext = Provenance.ExecutionContext,
        TerminalPreparationToken = ToPreparationToken(),
        Provenance = RuntimeCheckpointProvenance.Terminal(Provenance.WorkflowCheckpointOrder)
    };

    private RuntimeCheckpointPreparationToken ToPreparationToken() => new(
        CommitId,
        LedgerToken,
        Provenance,
        ExpectedOrderRevision ?? throw new InvalidOperationException("Prepared ledger entry has no order authority."),
        ExpectedContextRevision ?? throw new InvalidOperationException("Prepared ledger entry has no context authority."),
        ExpectedFence,
        InputFingerprint,
        CanonicalInputReference ?? throw new InvalidOperationException("Prepared ledger entry has no canonical input reference."),
        InitialPersistenceMode ?? throw new InvalidOperationException("Prepared ledger entry has no candidate persistence mode."),
        RecoveryAuthority);
}
