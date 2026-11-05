using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record ActivityTemplateDependencyRequest(
    string DefinitionVersionId,
    string OccurrenceId,
    IReadOnlyList<ActivityNodeOrigin> NodeOrigin,
    string? ParentOccurrenceId = null,
    string ChildSlotName = "activity-graph",
    int ChildIndex = 0,
    IReadOnlyList<ActivityContractMemberUsage>? MemberUsage = null)
{
    public IReadOnlyList<ActivityContractMemberUsage> MemberUsage { get; init; } = MemberUsage ?? [];
}

/// <summary>
/// The exact content one compilation reads: a Studio draft revision, or (with its own subject kind) other saved content that
/// carries a provider manifest and a public contract. Diagnostics name the candidate by <see cref="SubjectKind"/> and <see cref="Id"/>.
/// </summary>
public sealed record ActivityTemplateCandidate(string SubjectKind, string Id, long Revision, Elsa.Activities.Design.Core.Models.ActivityContract Contract, ActivityProviderManifest Provider)
{
    public static ActivityTemplateCandidate FromDraft(ActivityDefinitionDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        return new("ActivityDraft", draft.Id, draft.Revision, draft.State.Contract, draft.State.Provider);
    }
}

public sealed record ActivityTemplateCompilerRequest(
    ActivityDefinition Definition,
    ActivityTemplateCandidate Candidate,
    string CandidateDefinitionVersionId,
    string CandidateVersion,
    long LayoutBytes)
{
    /// <summary>Compiles one exact Studio draft revision. Its parameter names match the primary constructor's, so callers that name them keep compiling.</summary>
    public ActivityTemplateCompilerRequest(ActivityDefinition Definition, ActivityDefinitionDraft Draft, string CandidateDefinitionVersionId, string CandidateVersion, long LayoutBytes)
        : this(Definition, ActivityTemplateCandidate.FromDraft(Draft), CandidateDefinitionVersionId, CandidateVersion, LayoutBytes) { }
}

public sealed record ActivityTemplateCompilerResult(
    ExecutableActivityTemplate? Template,
    ActivityResourceMeasurements Measurements,
    IReadOnlyList<ActivityResolvedDependency> DirectDependencies,
    IReadOnlyList<ActivityDiagnostic> Diagnostics)
{
    public bool IsSuccessful => Template is not null && Diagnostics.All(x => x.Severity != ActivityDiagnosticSeverity.Error);
    public IReadOnlyList<ActivityVersionChange> ProviderCompatibilityChanges { get; init; } = [];
}


/// <summary>A compiled candidate with its ordered diagnostics and whether publication may proceed.</summary>
public sealed record EvaluatedPublicationCandidate(
    ActivityTemplateCompilerResult Compilation,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    bool IsPublishable);

