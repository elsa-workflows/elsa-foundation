using Elsa.Activities.Design.Core.Models;
using System.Text.Json;

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
