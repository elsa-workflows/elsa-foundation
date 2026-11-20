using System.Text.Json;
using Elsa.Decisions.Core;

namespace Elsa.Decisions.Persistence.Core;

/// <summary>A tenant-scoped editable business policy. Source bytes remain authoritative.</summary>
public sealed record DecisionAsset(string Id, string Name, string? Description, string Revision,
    string Xml, string SourceHash, bool IsSensitive, string? PublishedVersionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>An immutable policy version and its exact executable semantics.</summary>
public sealed record DecisionVersion(string Id, string AssetId, int Number, string Name, string Xml,
    string SourceHash, DecisionArtifact Artifact, bool IsSensitive, DateTimeOffset PublishedAt);

/// <summary>A revisioned saved expectation, independent of the policy's current draft.</summary>
public sealed record DecisionCase(string Id, string AssetId, string Name, string Revision,
    IReadOnlyDictionary<string, DecisionValue> Inputs, string ExpectedStatus, JsonElement ExpectedResult,
    bool IsSensitive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

/// <summary>Exact identities retained without raw source, inputs or outputs.</summary>
public sealed record DecisionCaseIdentity(string Id, string Revision, string Hash);
public sealed record DecisionCaseComparison(string CaseId, string Name, bool Passed, string Status,
    string? FailurePath, JsonElement? Expected, JsonElement? Actual, IReadOnlyList<string> MatchedRules, IReadOnlyList<string> SelectedRules, IReadOnlyList<DecisionRuleTrace> Trace);

/// <summary>A completed test receipt; there is no accepted pending job or recovery state.</summary>
public sealed record DecisionTestReceipt(string Id, string AssetId, string ActorId, string RequestFingerprint,
    string CandidateId, string CandidateHash, IReadOnlyList<DecisionCaseIdentity> Cases,
    IReadOnlyList<DecisionCaseComparison> Comparisons, bool Passed, bool HasDetailedPayload, bool IsSensitive,
    DateTimeOffset EvaluatedAt, DateTimeOffset CompletedAt)
{
    /// <summary>Authorized transient or explicitly retained input snapshots; absent for summary-only history.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<string, DecisionValue>>? CaseInputs { get; init; }
}

/// <summary>Authoritative command confirmation, separate from aggregate queries.</summary>
public sealed record DecisionWriteConfirmation(string Id, string Revision, bool Replayed = false);

/// <summary>Idempotency is scoped to a trusted tenant and actor.</summary>
public sealed record DecisionOperation(string TenantId, string ActorId, string OperationId);
public sealed record DecisionOperationReceipt(string Kind, string Fingerprint, DecisionWriteConfirmation Confirmation);
public sealed record SaveDecision(string? Id, string? ExpectedRevision, string Name, string? Description,
    string Xml, string SourceHash, bool IsSensitive);
public sealed record PublishDecision(string AssetId, string ExpectedRevision, DecisionArtifact Artifact);
public sealed record SaveDecisionCase(string AssetId, string? Id, string? ExpectedRevision, string Name,
    IReadOnlyDictionary<string, DecisionValue> Inputs, string ExpectedStatus, JsonElement ExpectedResult, bool IsSensitive);

/// <summary>Reads never mutate, repair or resolve an alternative version.</summary>
public interface IDecisionReader
{
    ValueTask<IReadOnlyList<DecisionAsset>> ListAssetsAsync(string tenantId, CancellationToken cancellationToken = default);
    ValueTask<DecisionAsset?> FindAssetAsync(string tenantId, string assetId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<DecisionVersion>> ListVersionsAsync(string tenantId, string assetId, CancellationToken cancellationToken = default);
    ValueTask<DecisionVersion?> FindVersionAsync(string tenantId, string assetId, string versionId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<DecisionCase>> ListCasesAsync(string tenantId, string assetId, CancellationToken cancellationToken = default);
    ValueTask<DecisionCase?> FindCaseAsync(string tenantId, string assetId, string caseId, CancellationToken cancellationToken = default);
    ValueTask<IReadOnlyList<DecisionTestReceipt>> ListRunsAsync(string tenantId, string assetId, CancellationToken cancellationToken = default);
    ValueTask<DecisionTestReceipt?> FindRunAsync(string tenantId, string assetId, string runId, CancellationToken cancellationToken = default);
    ValueTask<DecisionOperationReceipt?> FindOperationAsync(DecisionOperation operation, CancellationToken cancellationToken = default);
    ValueTask<DecisionTestReceipt?> FindCompletedOperationAsync(DecisionOperation operation, string assetId, CancellationToken cancellationToken = default);
}

/// <summary>Provider mutations enforce tenant identity, revision CAS and immutable operation replay.</summary>
public interface IDecisionWriter
{
    ValueTask<DecisionWriteConfirmation> SaveAssetAsync(DecisionOperation operation, SaveDecision command, CancellationToken cancellationToken = default);
    ValueTask<DecisionWriteConfirmation> PublishAsync(DecisionOperation operation, PublishDecision command, CancellationToken cancellationToken = default);
    ValueTask<DecisionWriteConfirmation> SaveCaseAsync(DecisionOperation operation, SaveDecisionCase command, CancellationToken cancellationToken = default);
    ValueTask<DecisionWriteConfirmation> DeleteCaseAsync(DecisionOperation operation, string assetId, string caseId, string expectedRevision, CancellationToken cancellationToken = default);
    ValueTask<DecisionWriteConfirmation> SaveCompletedRunAsync(DecisionOperation operation, DecisionTestReceipt receipt, CancellationToken cancellationToken = default);
    ValueTask<DecisionWriteConfirmation> DeleteRunAsync(DecisionOperation operation, string assetId, string runId, CancellationToken cancellationToken = default);
}

/// <summary>Safe fault crossing persistence and API boundaries.</summary>
public sealed class DecisionDesignException : Exception
{
    public DecisionDesignException(string code, string message, Exception? innerException = null) : base(message, innerException) => Code = code;
    public string Code { get; }
}
