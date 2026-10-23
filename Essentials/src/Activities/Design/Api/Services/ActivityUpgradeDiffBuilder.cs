using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;

namespace Elsa.Activities.Design.Api.Services;

/// <summary>
/// Compiles the exact post-rewrite activity candidate supplied by the Publishing discovery bridge,
/// then applies Design's provider-neutral compatibility floor. Workflow steps are intentionally not
/// activity-definition diffs and therefore return <see langword="null"/>.
/// </summary>
public interface IActivityUpgradeDiffBuilder
{
    ValueTask<ActivityVersionDiff?> BuildAsync(
        ActivityUpgradeOwnerSnapshot owner,
        CancellationToken cancellationToken = default);
}

public sealed class ActivityUpgradeDiffBuilder(
    IActivityDraftDiffCandidateCompiler candidateCompiler,
    IActivityVersionDiffer differ,
    IActivityDefinitionVersionPublicationStore publications,
    IActivityDirectDependencyStore dependencies,
    IActivityDefinitionLayoutStore layouts) : IActivityUpgradeDiffBuilder
{
    public async ValueTask<ActivityVersionDiff?> BuildAsync(
        ActivityUpgradeOwnerSnapshot owner,
        CancellationToken cancellationToken = default)
    {
        var request = owner.DiffCandidate;
        if (request is null)
            return null;

        var baseline = request.BaseVersionId is null
            ? null
            : await publications.FindAsync(request.BaseVersionId, cancellationToken);
        if (request.BaseVersionId is not null && baseline is null)
            return null;

        var baselineDependencies = baseline is null
            ? []
            : await ReadDependenciesAsync(baseline, cancellationToken);
        var baselineLayout = baseline is null
            ? null
            : await layouts.FindVersionLayoutAsync(baseline.DefinitionVersionId, cancellationToken);
        var candidate = await candidateCompiler.CompileAsync(request, cancellationToken);
        var diagnostics = candidate.Diagnostics;
        if (candidate.TemplateHash is null && diagnostics.Count == 0)
        {
            diagnostics = [new(
                "activity.provider.compilation-failed",
                ActivityDiagnosticSeverity.Error,
                "The post-rewrite activity candidate could not be compiled for comparison.",
                new("ActivityDraft", request.DraftId, request.DefinitionId, Revision: request.Revision),
                Remediation: "Resolve provider compilation errors before applying the upgrade.")];
        }

        var emptyContract = new ActivityContract(request.State.Contract.ContractSchemaVersion, [], [], []);
        return await differ.DiffAsync(new(
            baseline is null
                ? new("ActivityDefinitionBaseline", request.DefinitionId)
                : Identity(baseline),
            new("ActivityDraft", request.DefinitionId, DraftId: request.DraftId, Revision: request.Revision, TemplateHash: candidate.TemplateHash),
            baseline?.Contract ?? emptyContract,
            request.State.Contract,
            baseline?.Provider ?? request.State.Provider,
            request.State.Provider,
            baselineDependencies,
            candidate.Dependencies,
            candidate.ProviderCompatibilityChanges,
            baseline is null ? null : Implementation(baseline, baselineLayout),
            new(
                candidate.ProviderFingerprint,
                candidate.NodeCount,
                candidate.ResumeTargetCount,
                ActivityLayoutHasher.Compute(request.Layout),
                request.Layout.Count,
                candidate.RuntimeRequirements),
            CompareDependencies: candidate.TemplateHash is not null,
            Diagnostics: diagnostics), cancellationToken);
    }

    private async Task<IReadOnlyList<ActivityDependencyItem>> ReadDependenciesAsync(
        ActivityDefinitionVersionPublication owner,
        CancellationToken cancellationToken)
    {
        var edges = await dependencies.ListOutboundAsync(owner.DefinitionVersionId, cancellationToken);
        var ownerReference = Reference(owner);
        var result = new List<ActivityDependencyItem>(edges.Count);
        foreach (var edge in edges.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var dependency = await publications.FindAsync(edge.DependencyVersionId, cancellationToken)
                             ?? throw new InvalidOperationException($"Activity dependency '{edge.DependencyVersionId}' is unavailable.");
            var dependencyReference = Reference(dependency);
            result.Add(new(
                edge.Id,
                ownerReference,
                dependencyReference,
                new(edge.OccurrenceId, edge.NodeOrigin.ToArray()),
                true,
                1,
                [ownerReference, dependencyReference]));
        }
        return result;
    }

    private static ActivityVersionIdentity Identity(ActivityDefinitionVersionPublication version) => new(
        "ActivityVersion",
        version.DefinitionId,
        version.DefinitionVersionId,
        Version: version.Version,
        TemplateHash: version.TemplateHash);

    private static ActivityDefinitionReference Reference(ActivityDefinitionVersionPublication version) => new(
        "ActivityVersion",
        version.DefinitionId,
        version.DefinitionVersionId,
        version.Version,
        TemplateHash: version.TemplateHash,
        TenantId: version.TenantId,
        Lifecycle: version.Lifecycle);

    private static ActivityVersionImplementationFacts Implementation(
        ActivityDefinitionVersionPublication version,
        ActivityDefinitionVersionLayout? layout) => new(
        version.ProviderFingerprint,
        version.ResourceMeasurements.LocalNodeCount,
        version.ResumeTargetCount,
        LayoutHash: layout is null ? null : ActivityLayoutHasher.Compute(layout.Records),
        LayoutCount: layout?.Records.Count,
        RuntimeRequirements: version.RuntimeRequirements.ToArray());

}
