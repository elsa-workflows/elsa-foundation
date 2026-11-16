using System.Text.Json;
using Elsa.Decisions.Core;
using Elsa.Decisions.Persistence.Core;

namespace Elsa.Decisions.Design;

/// <summary>Identity supplied by the trusted host, never inferred from request body tenant fields.</summary>
public sealed record DecisionCaller(string TenantId, string SubjectId);

/// <summary>A host replacement port for current permission decisions. No implicit allow policy is provided.</summary>
public interface IDecisionAccessPolicy
{
    ValueTask<bool> IsAllowedAsync(DecisionCaller caller, string permission, object? resource = null, CancellationToken cancellationToken = default);
}

public sealed record DecisionAssetView(string Id, string Name, string? Description, string Revision, string SourceHash,
    string? Xml, bool IsSensitive, string? PublishedVersionId, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record DecisionVersionView(string Id, string AssetId, int Number, string Name, string SourceHash,
    string ContentHash, string ProfileId, string SemanticsId, string? Xml, bool IsSensitive, DateTimeOffset PublishedAt);
public sealed record DecisionCaseView(string Id, string AssetId, string Name, string Revision,
    IReadOnlyDictionary<string, DecisionValue>? Inputs, string ExpectedStatus, JsonElement? ExpectedResult,
    bool IsSensitive, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record DecisionValidation(bool Success, IReadOnlyList<DecisionDiagnostic> Diagnostics, string SourceHash);
public sealed record SaveDecisionRequest(string OperationId, string Name, string Xml, string? Description = null,
    string? ExpectedRevision = null, bool IsSensitive = false);
public sealed record PublishDecisionRequest(string OperationId, string ExpectedRevision);
public sealed record CloneDecisionRequest(string OperationId, string ExpectedRevision, string VersionId);
public sealed record ValidateDecisionRequest(string ExpectedRevision);
public sealed record EvaluateDecisionRequest(string VersionId, IReadOnlyDictionary<string, DecisionValue> Inputs, int Budget = 1000000);
public sealed record SaveDecisionCaseRequest(string OperationId, string Name, IReadOnlyDictionary<string, DecisionValue> Inputs,
    string ExpectedStatus, JsonElement ExpectedResult, string? ExpectedRevision = null, bool IsSensitive = false);
public sealed record DecisionTestCaseSelection(string Id, string Revision);
public sealed record TestDecisionRequest(string OperationId, string CandidateHash, IReadOnlyList<DecisionTestCaseSelection> Cases,
    string? CandidateRevision = null, string? VersionId = null);

/// <summary>Raw-value history is disabled unless the host explicitly enables it.</summary>
public sealed class DecisionDesignOptions
{
    public bool RetainDetailedTestHistory { get; set; }
}
