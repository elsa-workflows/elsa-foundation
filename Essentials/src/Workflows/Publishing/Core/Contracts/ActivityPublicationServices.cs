using ActivityContract = Elsa.Activities.Design.Core.Models.ActivityContract;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Workflows.Publishing.Core.Contracts;

/// <summary>Replacement contract: one shared candidate compilation/verdict service per shell.</summary>
public interface IActivityPublicationCandidateEvaluator
{
    Task<EvaluatedPublicationCandidate> EvaluateAsync(ActivityTemplateCompilerRequest request, ActivityDraftValidation validation, CancellationToken cancellationToken);
}
/// <summary>Replacement contract: selected Runtime and durable-storage publication readiness.</summary>
public interface IActivityPublicationReadiness
{
    IReadOnlyList<PublicationCapabilityReadiness> StorageReadiness(IEnumerable<RuntimeStorageDriverRequirement> requirements);
    IReadOnlyList<PublicationCapabilityReadiness> RuntimeReadiness(IEnumerable<RuntimeRequirement> requirements);
    IEnumerable<ActivityDiagnostic> ReadinessDiagnostics(ActivityDiagnosticSubject subject, IEnumerable<RuntimeRequirement> runtime, IEnumerable<RuntimeStorageDriverRequirement> storage);
    string Fingerprint(string artifactHash, IEnumerable<RuntimeRequirement> runtime, IEnumerable<RuntimeStorageDriverRequirement> storage);
}
/// <summary>Replacement contract: derived read-only catalog projections of validated compiler output.</summary>
public interface IActivityCatalogProjection
{
    ActivityDefinitionVersion CatalogVersion(ActivityDefinition definition, ActivityContract contract, ActivityProviderManifest provider, string versionId, string version, string sourceKind, string sourceId, ExecutableActivityTemplate template, DateTimeOffset now);
    ActivityDefinitionVersionPublication Publication(ActivityDefinition definition, ActivityContract contract, ActivityProviderManifest provider, string versionId, string version, string providerFingerprint, string sourceReferenceId, ExecutableActivityTemplate template,
        ActivityResourceMeasurements measurements, DateTimeOffset now, ActivityDefinitionVersionResolutionKind resolutionKind = ActivityDefinitionVersionResolutionKind.ReusableTemplateBoundary, string? sourceDraftId = null, string? sourceVersionId = null);
}
/// <summary>Replacement contract: publication release choices respecting the compatibility floor.</summary>
public interface IActivityPublicationVersionPolicy
{
    IReadOnlyList<string> Choices(string? headVersion, ActivityVersionBump requiredBump);
}
