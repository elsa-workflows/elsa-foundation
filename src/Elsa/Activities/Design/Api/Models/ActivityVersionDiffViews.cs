using System.Text.Json;
using Elsa.Activities.Design.Core.Models;

namespace Elsa.Activities.Design.Api.Models;

public sealed record ActivityVersionDiffIdentityView(
    string Kind,
    string DefinitionId,
    string? VersionId,
    string? DraftId,
    long? Revision,
    string? Version,
    string? TemplateHash);

public sealed record ActivityVersionProviderDiffView(
    string? FromKey,
    string? FromSchemaVersion,
    string? ToKey,
    string? ToSchemaVersion,
    bool Changed);

public sealed record ActivityVersionDiffSummaryView(
    int Breaking,
    int Additive,
    int NonBehavioral,
    int Warnings);

public sealed record ActivityVersionChangeSubjectView(
    string? MemberKind,
    string? ReferenceKey,
    string? DependencyVersionId,
    string? OccurrenceId);

public sealed record ActivityVersionChangeView(
    string ChangeId,
    string Area,
    string Kind,
    ActivityVersionChangeSubjectView Subject,
    JsonElement? Before,
    JsonElement? After,
    string Impact,
    string RequiredBump,
    string Message);

public sealed record ActivityVersionDiffView(
    ActivityVersionDiffIdentityView From,
    ActivityVersionDiffIdentityView To,
    string Compatibility,
    string RequiredBump,
    bool BehaviorChanged,
    ActivityVersionProviderDiffView Provider,
    ActivityVersionDiffSummaryView Summary,
    IReadOnlyList<ActivityVersionChangeView> Changes,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

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
