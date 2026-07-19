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
    string OriginKey,
    string? ControlledValueId = null)
{
    public static WorkflowDefinitionTagAssertion Manual(string tagDefinitionId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        return new(tagDefinitionId, WorkflowDefinitionTagOriginKinds.Manual, WorkflowDefinitionTagOriginKinds.Manual);
    }

    public static WorkflowDefinitionTagAssertion Manual(WorkflowDefinitionControlledTagValue controlledValue)
    {
        ArgumentNullException.ThrowIfNull(controlledValue);
        return new(
            controlledValue.TagDefinitionId,
            WorkflowDefinitionTagOriginKinds.Manual,
            WorkflowDefinitionTagOriginKinds.Manual,
            controlledValue.ControlledValueId);
    }
}

/// <summary>
/// One manual controlled-value assertion. The stable value identity is deliberately distinct from
/// presentation metadata so catalog renames do not change workflow assignments.
/// </summary>
public sealed record WorkflowDefinitionControlledTagValue(
    string TagDefinitionId,
    string ControlledValueId)
{
    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TagDefinitionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(ControlledValueId);
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
    string? IdempotencyId = null,
    IReadOnlyCollection<WorkflowDefinitionControlledTagValue>? ControlledValues = null);

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
    DateTimeOffset RecordedAt,
    IReadOnlyCollection<string>? AddedControlledValueIds = null,
    IReadOnlyCollection<string>? RemovedControlledValueIds = null);

public enum WorkflowDefinitionMarkerTagOperator
{
    Exists,
    Missing
}

public enum WorkflowDefinitionControlledTagOperator
{
    Exists,
    Missing,
    AnyOf,
    NoneOf,
    Conflicted,
    NotConflicted
}

public sealed record WorkflowDefinitionControlledTagClause(
    string TagDefinitionId,
    WorkflowDefinitionControlledTagOperator Operator,
    IReadOnlyCollection<string>? ControlledValueIds = null)
{
    public const int MaximumValues = 64;

    public void Validate()
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(TagDefinitionId);
        var values = ControlledValueIds ?? [];
        if (values.Count > MaximumValues)
            throw new ArgumentOutOfRangeException(nameof(ControlledValueIds), $"At most {MaximumValues} controlled tag values are supported per clause.");
        if (values.Any(string.IsNullOrWhiteSpace))
            throw new ArgumentException("Controlled tag value identities cannot be blank.", nameof(ControlledValueIds));
        if (Operator is WorkflowDefinitionControlledTagOperator.AnyOf or WorkflowDefinitionControlledTagOperator.NoneOf)
        {
            if (values.Count == 0)
                throw new ArgumentException($"{Operator} requires at least one controlled tag value identity.", nameof(ControlledValueIds));
        }
        else if (values.Count != 0)
            throw new ArgumentException($"{Operator} does not accept controlled tag value identities.", nameof(ControlledValueIds));
    }
}

public enum WorkflowDefinitionTagEffectiveState
{
    Untagged,
    Value,
    Conflicted
}

public sealed record WorkflowDefinitionEffectiveControlledTag(
    string TagDefinitionId,
    WorkflowDefinitionTagEffectiveState State,
    IReadOnlyCollection<string> ControlledValueIds)
{
    public string? SingleControlledValueId =>
        State == WorkflowDefinitionTagEffectiveState.Value ? ControlledValueIds.SingleOrDefault() : null;
}

public static class WorkflowDefinitionTagEffectiveReducer
{
    public const string ConflictProjectionValue = "!conflicted";

    public static WorkflowDefinitionEffectiveControlledTag ReduceControlled(
        string tagDefinitionId,
        IEnumerable<WorkflowDefinitionTagAssertion> assertions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tagDefinitionId);
        ArgumentNullException.ThrowIfNull(assertions);
        var values = assertions
            .Where(assertion => StringComparer.Ordinal.Equals(assertion.TagDefinitionId, tagDefinitionId))
            .Select(assertion => assertion.ControlledValueId)
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Cast<string>()
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();
        return values.Length switch
        {
            0 => new(tagDefinitionId, WorkflowDefinitionTagEffectiveState.Untagged, []),
            1 => new(tagDefinitionId, WorkflowDefinitionTagEffectiveState.Value, values),
            _ => new(tagDefinitionId, WorkflowDefinitionTagEffectiveState.Conflicted, values)
        };
    }
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
