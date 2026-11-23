using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Core.Models;
using System.Text.Json;

namespace Elsa.Activities.Design.Api.Models;

public static class ActivityVersionDiffViewMappings
{
    public static ActivityVersionDiffView ToView(this ActivityVersionDiff diff) => new(
        Identity(diff.From),
        Identity(diff.To),
        diff.Compatibility.ToString(),
        diff.RequiredBump.ToString(),
        diff.BehaviorChanged,
        new(diff.Provider.FromKey, diff.Provider.FromSchemaVersion, diff.Provider.ToKey, diff.Provider.ToSchemaVersion, diff.Provider.Changed),
        new(diff.Summary.Breaking, diff.Summary.Additive, diff.Summary.NonBehavioral, diff.Summary.Warnings),
        diff.Changes.Select(Change).ToArray(),
        diff.Diagnostics);

    private static ActivityVersionDiffIdentityView Identity(ActivityVersionIdentity identity) => new(
        identity.Kind,
        identity.DefinitionId,
        identity.VersionId,
        identity.DraftId,
        identity.Revision,
        identity.Version,
        identity.TemplateHash);

    private static ActivityVersionChangeView Change(ActivityVersionChange change) => new(
        change.ChangeId,
        change.Area.ToString(),
        change.Kind,
        new(change.Subject.MemberKind, change.Subject.ReferenceKey, change.Subject.DependencyVersionId, change.Subject.OccurrenceId),
        Clone(change.Before),
        Clone(change.After),
        change.Impact.ToString(),
        change.RequiredBump.ToString(),
        change.Message);

    private static JsonElement? Clone(JsonElement? value) => value is { } element ? element.Clone() : null;
}
