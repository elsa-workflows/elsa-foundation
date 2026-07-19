namespace Elsa.Workflows.Design.Persistence.Core.Models;

public static class WorkflowDefinitionTagOriginKinds
{
    public const string Manual = "manual";
}

public static class WorkflowDefinitionTagRevision
{
    public const string Initial = "wft:00000000000000000000";

    public static string FromVersion(long version)
    {
        if (version < 0)
            throw new ArgumentOutOfRangeException(nameof(version));
        return $"wft:{version:D20}";
    }

    public static bool TryGetVersion(string? revision, out long version)
    {
        version = 0;
        return revision is not null
               && revision.StartsWith("wft:", StringComparison.Ordinal)
               && revision.Length == 24
               && long.TryParse(revision.AsSpan(4), out version)
               && version >= 0;
    }
}

public sealed record WorkflowDefinitionTagAssertion(
    string TagDefinitionId,
    string OriginKind,
    string OriginKey)
{
    public static WorkflowDefinitionTagAssertion Manual(string tagDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        return new(tagDefinitionId, WorkflowDefinitionTagOriginKinds.Manual, WorkflowDefinitionTagOriginKinds.Manual);
    }
}

public sealed record WorkflowDefinitionTagSet(
    string WorkflowDefinitionId,
    string? TenantId,
    string Revision,
    IReadOnlyCollection<WorkflowDefinitionTagAssertion> Assertions);

public sealed record ReplaceWorkflowDefinitionManualTags(
    string WorkflowDefinitionId,
    string? TenantId,
    string ExpectedRevision,
    IReadOnlyCollection<string> TagDefinitionIds,
    string ActorId,
    string CorrelationId,
    string? IdempotencyId = null);

public enum WorkflowDefinitionTagReplaceStatus
{
    Saved,
    Conflict
}

public sealed record WorkflowDefinitionTagReplaceResult(
    WorkflowDefinitionTagReplaceStatus Status,
    WorkflowDefinitionTagSet? TagSet = null,
    string? CurrentRevision = null);

public sealed record WorkflowDefinitionTagAuditFact(
    string Id,
    string WorkflowDefinitionId,
    string? TenantId,
    string Origin,
    string ActorId,
    string CorrelationId,
    string? IdempotencyId,
    string PreviousRevision,
    string NewRevision,
    IReadOnlyCollection<string> AddedTagDefinitionIds,
    IReadOnlyCollection<string> RemovedTagDefinitionIds,
    DateTimeOffset RecordedAt);

public enum WorkflowDefinitionMarkerTagOperator
{
    Exists,
    Missing
}

public sealed record WorkflowDefinitionMarkerTagClause(
    string TagDefinitionId,
    WorkflowDefinitionMarkerTagOperator Operator)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TagDefinitionId);
    }
}
