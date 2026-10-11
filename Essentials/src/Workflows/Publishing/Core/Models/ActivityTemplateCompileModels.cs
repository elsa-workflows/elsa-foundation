using System.Text.Json;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record ActivityTemplateCompilationRequest(
    string DefinitionId,
    string ActivityTypeKey,
    string DraftId,
    long Revision,
    string CandidateVersion,
    Elsa.Activities.Design.Core.Models.ActivityContract Contract,
    ActivityProviderManifest Provider,
    IReadOnlyList<ActivityResolvedDependency> ResolvedDirectDependencies,
    string ProviderFingerprint);

public sealed record ActivityTemplateCompilation(
    ExecutableNode? ExecutableRoot,
    IReadOnlyDictionary<string, WorkflowExecutableResumeTarget> TemplateLocalResumeTargets,
    IReadOnlyList<ActivityResolvedDependency> DirectDependencies,
    IReadOnlyList<RuntimeRequirement> RuntimeRequirements,
    IReadOnlyList<RuntimeStorageDriverRequirement> StorageDriverRequirements,
    ActivityResourceMeasurements ResourceMeasurements,
    string ProviderFingerprint,
    IReadOnlyList<ActivityVersionChange> ProviderCompatibilityChanges,
    IReadOnlyList<ActivityDiagnostic> Diagnostics,
    IReadOnlyList<ActivityTemplateOccurrenceCompilation>? Occurrences = null)
{
    public IReadOnlyList<ActivityTemplateOccurrenceCompilation> Occurrences { get; init; } = Occurrences ?? [];
}

/// <summary>
/// Provider-owned authored content that must be materialized only when an exact dependency occurrence
/// is placed. Inputs remain opaque canonical JSON until Publishing lowers them against the selected
/// dependency contract; structure is already compiled but still references authored child node ids.
/// </summary>
public sealed record ActivityTemplateOccurrenceCompilation(
    string OccurrenceId,
    JsonElement InputBindings,
    ActivityNodeStructure? Structure);

public sealed record ActivityTemplateDependencyDiscoveryRequest(
    string DefinitionId,
    string DraftId,
    long Revision,
    ActivityProviderManifest Provider);

public sealed record ActivityTemplateDependencyDeclaration(
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

public sealed record ActivityTemplateDependencyDiscovery(
    IReadOnlyList<ActivityTemplateDependencyDeclaration> Dependencies,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);
