using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Models;

public sealed record ActivityTemplateCompilationRequest(
    string DefinitionId,
    string ActivityTypeKey,
    string DraftId,
    long Revision,
    string CandidateVersion,
    ActivityContract Contract,
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
    IReadOnlyList<ActivityDiagnostic> Diagnostics);

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
    int ChildIndex = 0);

public sealed record ActivityTemplateDependencyDiscovery(
    IReadOnlyList<ActivityTemplateDependencyDeclaration> Dependencies,
    IReadOnlyList<ActivityDiagnostic> Diagnostics);
