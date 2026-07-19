using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Api.Services;

namespace Elsa.Workflows.Publishing.Api.Models;

public sealed record PublishedActivityProviderView(
    string ProviderKey,
    string SchemaVersion,
    string Fingerprint);

public sealed record PublishedActivityVersionChangeSubjectView(
    string? MemberKind,
    string? ReferenceKey,
    string? DependencyVersionId,
    string? OccurrenceId);

public sealed record PublishedActivityVersionChangeView(
    string ChangeId,
    string Area,
    string Kind,
    PublishedActivityVersionChangeSubjectView Subject,
    JsonElement? Before,
    JsonElement? After,
    string Impact,
    string RequiredBump,
    string Message);

public sealed record PublishedActivityDiffView(
    string Compatibility,
    string RequiredBump,
    bool BehaviorChanged,
    IReadOnlyList<PublishedActivityVersionChangeView> Changes);

public sealed record PublishedActivityRuntimeRequirementView(
    string ConsumerKey,
    string SchemaVersion);

public sealed record PublishedActivityDefinitionView(
    string DefinitionId,
    string VersionId,
    string Version,
    string DraftId,
    string TemplateId,
    string TemplateHash,
    string SourceReferenceId,
    PublishedActivityProviderView Provider,
    int DirectDependencyCount,
    int ClosedTemplateCount,
    IReadOnlyList<PublishedActivityRuntimeRequirementView> RuntimeRequirements,
    PublishedActivityDiffView? Diff,
    DateTimeOffset PublishedAt)
{
    public static PublishedActivityDefinitionView From(PublishActivityDefinitionResult result)
    {
        var publication = result.VersionPublication;
        return new(
            publication.DefinitionId,
            publication.DefinitionVersionId,
            publication.Version,
            result.Publication.DraftId,
            result.Template.TemplateId,
            result.Template.TemplateHash,
            result.SourceReference.SourceReferenceId,
            new(publication.Provider.ProviderKey, publication.Provider.SchemaVersion, publication.ProviderFingerprint),
            result.Template.DirectDependencies.Count,
            result.Template.ClosedTemplates.Count,
            result.Template.RuntimeRequirements
                .OrderBy(x => x.ConsumerKey, StringComparer.Ordinal)
                .ThenBy(x => x.SchemaVersion, StringComparer.Ordinal)
                .Select(x => new PublishedActivityRuntimeRequirementView(x.ConsumerKey, x.SchemaVersion))
                .ToArray(),
            result.Diff is null ? null : DiffView(result.Diff),
            result.Publication.PublishedAt);
    }

    private static PublishedActivityDiffView DiffView(ActivityVersionDiff diff) => new(
        diff.Compatibility.ToString(),
        diff.RequiredBump.ToString(),
        diff.BehaviorChanged,
        diff.Changes.Select(ChangeView).ToArray());

    private static PublishedActivityVersionChangeView ChangeView(ActivityVersionChange change) => new(
        change.ChangeId,
        change.Area.ToString(),
        change.Kind,
        new(
            change.Subject.MemberKind,
            change.Subject.ReferenceKey,
            change.Subject.DependencyVersionId,
            change.Subject.OccurrenceId),
        change.Before?.Clone(),
        change.After?.Clone(),
        change.Impact.ToString(),
        change.RequiredBump.ToString(),
        change.Message);
}
