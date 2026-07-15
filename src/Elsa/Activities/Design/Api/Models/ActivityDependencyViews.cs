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
    IReadOnlyList<ActivityDefinitionReferenceView> Path);

public sealed record ActivityDependencyPageView(
    ActivityDefinitionReferenceView Root,
    ActivityDependencyQueryView Query,
    ActivityDependencyConsistencyView Consistency,
    IReadOnlyList<ActivityDependencyItemView> Items,
    string? NextCursor);

public static class ActivityDependencyViewMappings
{
    public static ActivityDependencyPageView ToView(this ActivityDependencyPage page) => new(
        Reference(page.Root),
        new(page.Query.Direction.ToString(), page.Query.Transitive, page.Query.Include.Order(StringComparer.Ordinal).ToArray()),
        new(
            page.Consistency.Kind.ToString(),
            page.Consistency.IsAuthoritative,
            page.Consistency.AsOfSequence,
            page.Consistency.AsOf,
            page.Consistency.RebuildId),
        page.Items.Select(Item).ToArray(),
        page.NextCursor);

    private static ActivityDependencyItemView Item(ActivityDependencyItem item) => new(
        item.RelationshipId,
        Reference(item.Owner),
        Reference(item.Dependency),
        new(item.Occurrence.OccurrenceId, item.Occurrence.NodeOrigin),
        item.IsDirect,
        item.Depth,
        item.Path.Select(Reference).ToArray());

    private static ActivityDefinitionReferenceView Reference(ActivityDefinitionReference reference) => new(
        reference.Kind,
        reference.DefinitionId,
        reference.VersionId,
        reference.Version,
        reference.DraftId,
        reference.Revision,
        reference.TemplateHash,
        reference.TenantId,
        reference.Lifecycle?.ToString());
}
