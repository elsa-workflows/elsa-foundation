using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityDefinitionReferenceView(
    string Kind,
    string DefinitionId,
    string? VersionId,
    string? Version,
    string? DraftId,
    long? Revision,
    string? TemplateHash,
    string? TenantId,
    string? Lifecycle);

public sealed record ActivityDependencyQueryView(
    string Direction,
    bool Transitive,
    IReadOnlyList<string> Include);

public sealed record ActivityDependencyConsistencyView(
    string Kind,
    bool IsAuthoritative,
    long? AsOfSequence,
    DateTimeOffset? AsOf,
    string? RebuildId);

public sealed record ActivityDependencyOccurrenceView(
    string OccurrenceId,
    IReadOnlyList<ActivityNodeOrigin> NodeOrigin);

public sealed record ActivityDependencyItemView(
    string RelationshipId,
    ActivityDefinitionReferenceView Owner,
    ActivityDefinitionReferenceView Dependency,
    ActivityDependencyOccurrenceView Occurrence,
    bool IsDirect,
    int Depth,
    IReadOnlyList<ActivityDefinitionReferenceView> Path,
    IReadOnlyList<ActivityContractMemberUsage> MemberUsage);

public sealed record ActivityDependencyPageView(
    ActivityDefinitionReferenceView Root,
    ActivityDependencyQueryView Query,
    ActivityDependencyConsistencyView Consistency,
    IReadOnlyList<ActivityDependencyItemView> Items,
    string? NextCursor);
