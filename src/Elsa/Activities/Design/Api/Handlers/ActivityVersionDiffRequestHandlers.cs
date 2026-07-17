using System.Globalization;
using Elsa.Activities.Design.Api.Commands;
using Elsa.Activities.Design.Api.Models;
using Elsa.Activities.Design.Api.Requests;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Mediator.Core.Contracts;

namespace Elsa.Activities.Design.Api.Handlers;

public sealed class ActivityVersionDiffService(
    IActivityDefinitionAuthoringStore authoringStore,
    IActivityDefinitionDraftStore draftStore,
    IActivityDefinitionVersionPublicationStore publicationStore,
    IActivityDefinitionLayoutStore layoutStore,
    IActivityDirectDependencyStore dependencyStore,
    IActivityVersionDiffer differ,
    IActivityDraftDiffCandidateCompiler candidateCompiler,
    IActivityAuthoringContext context)
{
    public async Task<ActivityVersionDiffView> CompareVersionsAsync(
        string fromVersionId,
        string toVersionId,
        CancellationToken cancellationToken)
    {
        var from = await GetPublicationAsync(fromVersionId, cancellationToken);
        var to = await GetPublicationAsync(toVersionId, cancellationToken);
        EnsureVisible(from.TenantId);
        EnsureVisible(to.TenantId);
        if (!StringComparer.Ordinal.Equals(from.DefinitionId, to.DefinitionId))
            throw BadRequest("Activity versions from different definitions cannot be compared.");

        var fromDependenciesTask = ReadDependenciesAsync(from, cancellationToken);
        var toDependenciesTask = ReadDependenciesAsync(to, cancellationToken);
        var fromLayoutTask = layoutStore.FindVersionLayoutAsync(from.DefinitionVersionId, cancellationToken);
        var toLayoutTask = layoutStore.FindVersionLayoutAsync(to.DefinitionVersionId, cancellationToken);
        await Task.WhenAll(fromDependenciesTask, toDependenciesTask, fromLayoutTask, toLayoutTask);

        var result = await differ.DiffAsync(new(
            VersionIdentity(from),
            VersionIdentity(to),
            from.Contract,
            to.Contract,
            from.Provider,
            to.Provider,
            fromDependenciesTask.Result,
            toDependenciesTask.Result,
            FromImplementation: Implementation(from, fromLayoutTask.Result),
            ToImplementation: Implementation(to, toLayoutTask.Result)), cancellationToken);
        return result.ToView();
    }

    public async Task<ActivityVersionDiffView> PreviewDraftAsync(
        string draftId,
        long expectedRevision,
        string? baseVersionId,
        CancellationToken cancellationToken)
    {
        var draft = await draftStore.FindAsync(draftId, cancellationToken)
            ?? throw NotFound("activity.draft.not-found", "Activity draft not found", "The requested activity draft was not found.");
        EnsureVisible(draft.TenantId);
        if (draft.Revision != expectedRevision || draft.Status != ActivityDefinitionDraftStatus.Active)
            throw StaleRevision(draft, expectedRevision);

        var authoring = await authoringStore.FindAsync(draft.DefinitionId, cancellationToken)
            ?? throw NotFound("activity.definition.not-found", "Activity definition not found", "The requested activity definition was not found.");
        EnsureVisible(authoring.TenantId);
        var observedBaseVersionId = baseVersionId ?? authoring.HeadVersionId;
        ActivityDefinitionVersionPublication? baseline = null;
        if (observedBaseVersionId is not null)
        {
            baseline = await GetPublicationAsync(observedBaseVersionId, cancellationToken);
            EnsureVisible(baseline.TenantId);
            if (!StringComparer.Ordinal.Equals(baseline.DefinitionId, draft.DefinitionId))
                throw NotFound("activity.version.not-found", "Activity version not found", "The requested base version was not found for this definition.");
        }

        var draftLayout = await layoutStore.FindDraftLayoutAsync(draft.Id, cancellationToken)
            ?? throw OperationFailed("The draft layout is unavailable.");
        ActivityDefinitionVersionLayout? baselineLayout = null;
        IReadOnlyList<ActivityDependencyItem> baselineDependencies = [];
        if (baseline is not null)
        {
            baselineLayout = await layoutStore.FindVersionLayoutAsync(baseline.DefinitionVersionId, cancellationToken);
            baselineDependencies = await ReadDependenciesAsync(baseline, cancellationToken);
        }

        var candidate = await candidateCompiler.CompileAsync(new(
            draft.DefinitionId,
            draft.Id,
            draft.Revision,
            draft.State,
            draftLayout.Records.ToArray(),
            baseline?.DefinitionVersionId), cancellationToken);
        var candidateDiagnostics = candidate.Diagnostics;
        if (candidate.TemplateHash is null && candidateDiagnostics.Count == 0)
        {
            candidateDiagnostics = [new(
                "activity.provider.compilation-failed",
                ActivityDiagnosticSeverity.Error,
                "The draft candidate could not be compiled for comparison.",
                new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision),
                Remediation: "Resolve provider compilation errors before publishing.",
                Metadata: new Dictionary<string, string>())];
        }

        var emptyContract = new ActivityContract(draft.State.Contract.ContractSchemaVersion, [], [], []);
        var result = await differ.DiffAsync(new(
            baseline is null ? EmptyBaseline(draft) : VersionIdentity(baseline),
            new("ActivityDraft", draft.DefinitionId, DraftId: draft.Id, Revision: draft.Revision, TemplateHash: candidate.TemplateHash),
            baseline?.Contract ?? emptyContract,
            draft.State.Contract,
            baseline?.Provider ?? draft.State.Provider,
            draft.State.Provider,
            baselineDependencies,
            candidate.Dependencies,
            candidate.ProviderCompatibilityChanges,
            FromImplementation: baseline is null ? null : Implementation(baseline, baselineLayout),
            ToImplementation: new(
                candidate.ProviderFingerprint,
                candidate.NodeCount,
                candidate.ResumeTargetCount,
                ActivityLayoutHasher.Compute(draftLayout.Records),
                draftLayout.Records.Count,
                candidate.RuntimeRequirements),
            CompareDependencies: candidate.TemplateHash is not null,
            Diagnostics: candidateDiagnostics), cancellationToken);
        return result.ToView();
    }

    private async Task<IReadOnlyList<ActivityDependencyItem>> ReadDependenciesAsync(
        ActivityDefinitionVersionPublication owner,
        CancellationToken cancellationToken)
    {
        var edges = await dependencyStore.ListOutboundAsync(owner.DefinitionVersionId, cancellationToken);
        var result = new List<ActivityDependencyItem>(edges.Count);
        foreach (var edge in edges.OrderBy(x => x.OccurrenceId, StringComparer.Ordinal))
        {
            var dependency = await GetPublicationAsync(edge.DependencyVersionId, cancellationToken);
            EnsureVisible(dependency.TenantId);
            var ownerReference = Reference(owner);
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

    private static ActivityVersionIdentity VersionIdentity(ActivityDefinitionVersionPublication version) => new(
        "ActivityVersion",
        version.DefinitionId,
        version.DefinitionVersionId,
        Version: version.Version,
        TemplateHash: version.TemplateHash);

    private static ActivityVersionIdentity EmptyBaseline(ActivityDefinitionDraft draft) => new(
        "ActivityDefinitionBaseline",
        draft.DefinitionId);

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

    private async Task<ActivityDefinitionVersionPublication> GetPublicationAsync(
        string versionId,
        CancellationToken cancellationToken) =>
        await publicationStore.FindAsync(versionId, cancellationToken)
        ?? throw NotFound("activity.version.not-found", "Activity version not found", "The requested activity version was not found.");

    private void EnsureVisible(string? tenantId)
    {
        if (tenantId is not null && !StringComparer.Ordinal.Equals(tenantId, context.TenantId))
            throw new ActivityAuthoringException(
                403,
                "activity.tenant.reference-denied",
                "Activity reference is forbidden",
                "The requested activity identity is outside the caller's tenant scope.");
    }

    private static ActivityAuthoringException StaleRevision(ActivityDefinitionDraft draft, long expected) => new(
        409,
        "activity.draft.stale-revision",
        "Activity draft revision is stale",
        "The draft changed after the submitted revision was read.",
        [new(
            "activity.draft.stale-revision",
            ActivityDiagnosticSeverity.Error,
            $"Expected revision {expected} but the current revision is {draft.Revision}.",
            new("ActivityDraft", draft.Id, draft.DefinitionId, Revision: draft.Revision),
            Remediation: "Reload the draft and reapply the intended change.",
            Metadata: new Dictionary<string, string>
            {
                ["expectedRevision"] = expected.ToString(CultureInfo.InvariantCulture),
                ["actualRevision"] = draft.Revision.ToString(CultureInfo.InvariantCulture)
            })]);

    private static ActivityAuthoringException NotFound(string code, string title, string detail) => new(404, code, title, detail);

    private static ActivityAuthoringException BadRequest(string detail) =>
        new(400, "activity.request.invalid", "Invalid activity diff request", detail);

    private static ActivityAuthoringException OperationFailed(string detail) =>
        new(500, "activity.operation.failed", "Activity diff operation failed", detail);

}

public sealed class CompareActivityVersionsHandler(ActivityVersionDiffService service)
    : IRequestHandler<CompareActivityVersions, ActivityVersionDiffView>
{
    public Task<ActivityVersionDiffView> Handle(CompareActivityVersions request, CancellationToken cancellationToken) =>
        service.CompareVersionsAsync(request.FromVersionId, request.ToVersionId, cancellationToken);
}

public sealed class PreviewActivityDraftDiffHandler(ActivityVersionDiffService service)
    : IRequestHandler<PreviewActivityDraftDiff, ActivityVersionDiffView>
{
    public Task<ActivityVersionDiffView> Handle(PreviewActivityDraftDiff request, CancellationToken cancellationToken) =>
        service.PreviewDraftAsync(request.DraftId, request.ExpectedRevision, request.BaseVersionId, cancellationToken);
}
