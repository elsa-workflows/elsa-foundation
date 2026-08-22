using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

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
        item.Path.Select(Reference).ToArray(),
        item.MemberUsage
            .OrderBy(x => x.MemberKind, StringComparer.Ordinal)
            .ThenBy(x => x.ReferenceKey, StringComparer.Ordinal)
            .ThenBy(x => x.UsageKind, StringComparer.Ordinal)
            .ToArray());

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
