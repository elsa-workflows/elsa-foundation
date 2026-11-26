using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>What one run imports, and the hooks its caller observes it through.</summary>
/// <param name="SourceId">Names the collection (<c>elsa3-reconcile:{SourceId}</c>) and the publish phase's operation key.</param>
/// <param name="Inputs">The Elsa 3 definitions read, in input order.</param>
internal sealed record Elsa3ImportRunRequest(string SourceId, IReadOnlyList<Elsa3InputItem> Inputs)
{
    /// <summary>
    /// Called inside the persistence scope with the skips so far, at each point the sequence may stop before it
    /// commits: after input selection, after the analysis, and after each apply round that left items out. An exception
    /// it throws ends the run in that scope, before anything more is committed.
    /// </summary>
    public Action<PersistenceScope, IReadOnlyList<Elsa3SkippedItem>>? OnSkips { get; init; }

    /// <summary>Called inside the persistence scope with its result, before the next scope runs.</summary>
    public Action<Elsa3ImportRunResult>? OnScopeCompleted { get; init; }
}

/// <summary>Where a run-level refusal happened.</summary>
internal enum Elsa3ImportRunStage
{
    /// <summary>The analysis reported an error that names no plan item.</summary>
    Analysis,

    /// <summary>The apply refused the selection without naming one selected item.</summary>
    Apply
}

/// <summary>
/// A refusal the run cannot pin on one definition: a plan error that names no item, or an apply refusal over the whole
/// selection (a credential-literal refusal, an identity collision, a persistence failure, or a validation refusal that
/// names no selected item). The run returns it instead of throwing (spec 1237 FR-013).
/// </summary>
/// <param name="Code">The refusal's code: the plan error's code, the first error code of a validation refusal, or
/// <c>null</c> when the exception carries none.</param>
/// <param name="Cause">The refusal's text: the plan error's message and guidance, or the exception's message followed
/// by a validation refusal's error diagnostics.</param>
/// <param name="Diagnostics">The plan error, or a validation refusal's diagnostics; empty for other exceptions.</param>
/// <param name="UnappliedSourceVersionIds">The selected source version ids the refusal kept from being applied.</param>
/// <param name="Exception">The apply's exception; <c>null</c> for <see cref="Elsa3ImportRunStage.Analysis"/>.</param>
internal sealed record Elsa3ImportRunRefusal(
    Elsa3ImportRunStage Stage,
    string? Code,
    string Cause,
    IReadOnlyList<Elsa3MigrationDiagnostic> Diagnostics,
    IReadOnlyList<string> UnappliedSourceVersionIds,
    Exception? Exception);

/// <summary>
/// One imported workflow or wrapper version the run could publish. Only the newest of a workflow definition is
/// published; an older one is imported, has no <see cref="Status"/>, and names the version it was superseded by.
/// </summary>
/// <param name="Status">The publication's outcome; <c>null</c> for a superseded version.</param>
/// <param name="SupersededBySourceVersionId">For a superseded version: the source version id published instead.</param>
/// <param name="PublishFailure">For a failed publication: the first error the publication stopped at (FR-014).</param>
internal sealed record Elsa3WorkflowPublication(
    ReusableActivityImportedWorkflowVersion Workflow,
    WorkflowPublicationStatus? Status,
    string? SupersededBySourceVersionId,
    Exception? PublishFailure);

/// <summary>What one run did in one persistence scope.</summary>
/// <param name="PlanItems">The analysis' items with their diagnostics; empty when nothing was analysed.</param>
/// <param name="Skipped">The items left out, in the order they were left out, with their cause and code.</param>
/// <param name="ReusablePublications">The publish phase's states of the applied reusable versions.</param>
/// <param name="WorkflowPublications">The applied workflow and wrapper versions whose reusables were published.</param>
/// <param name="Warnings">The conversion warnings of the applied items.</param>
/// <param name="Refusal">A refusal the run could not pin on one definition; nothing more ran in this scope after it.</param>
internal sealed record Elsa3ImportRunResult(
    PersistenceScope Scope,
    IReadOnlyList<ReusableActivityImportItem> PlanItems,
    IReadOnlyList<Elsa3SkippedItem> Skipped,
    IReadOnlyList<ReusableActivityPublicationState> ReusablePublications,
    IReadOnlyList<Elsa3WorkflowPublication> WorkflowPublications,
    IReadOnlyList<Elsa3MigrationDiagnostic> Warnings,
    Elsa3ImportRunRefusal? Refusal)
{
    /// <summary>Imported reusable activity versions that have been published, by this run or earlier.</summary>
    public int ReusablesInPlace => ReusablePublications.Count(state =>
        state.Status is ReusableActivityPublicationStatus.Published or ReusableActivityPublicationStatus.AlreadyPublished);

    /// <summary>Workflow definitions (wrappers included) whose newest imported version has been published, by this run or earlier.</summary>
    public int WorkflowsInPlace => WorkflowPublications.Count(publication =>
        publication.Status is WorkflowPublicationStatus.Published or WorkflowPublicationStatus.AlreadyPublished);

    /// <summary>Of those, the ones this run published.</summary>
    public int WorkflowsPublished => WorkflowPublications.Count(publication => publication.Status == WorkflowPublicationStatus.Published);
}

/// <summary>
/// The Elsa 3 import sequence the startup task and the migration tooling share (spec 235, spec 1237 research R-2): input
/// selection, then per persistence scope and under <see cref="Elsa3StartupPublishingGrant"/> the collection analysis,
/// the plan selection, the apply (again after leaving out refused items and their dependents) and the publication of
/// the newest imported version of each workflow.
/// </summary>
internal sealed class Elsa3ImportRun(Elsa3ImportSelection selection, IPersistenceScopeRunner scopeRunner)
{
    public async Task<IReadOnlyList<Elsa3ImportRunResult>> RunAsync(Elsa3ImportRunRequest request, CancellationToken cancellationToken)
    {
        var (inputs, inputSkips) = selection.SelectInput(request.Inputs);
        var startId = Guid.NewGuid().ToString("N");
        var results = new List<Elsa3ImportRunResult>();

        await scopeRunner.RunAsync(
            async (scope, operationScope, token) =>
            {
                var result = await ImportAsync(request, scope, operationScope.ServiceProvider, inputs, inputSkips, startId, token);
                results.Add(result);
                request.OnScopeCompleted?.Invoke(result);
            },
            cancellationToken);

        return results;
    }

    private async Task<Elsa3ImportRunResult> ImportAsync(
        Elsa3ImportRunRequest request,
        PersistenceScope scope,
        IServiceProvider services,
        IReadOnlyList<Elsa3InputItem> inputs,
        IReadOnlyList<Elsa3SkippedItem> inputSkips,
        string startId,
        CancellationToken cancellationToken)
    {
        var skipped = new List<Elsa3SkippedItem>(inputSkips);
        request.OnSkips?.Invoke(scope, skipped);
        if (inputs.Count == 0)
            return Result(scope, [], skipped);

        services.GetRequiredService<Elsa3StartupPublishingGrant>().Grant(scope.Value);
        var importer = services.GetRequiredService<IReusableActivityCollectionImporter>();
        var inputsById = inputs.ToDictionary(input => input.Definition.Id, StringComparer.Ordinal);
        var collection = new ReusableActivityImportCollection(
            $"elsa3-reconcile:{request.SourceId}",
            inputs.Select(input => input.Definition).ToArray());

        var plan = await importer.AnalyzeAsync(collection, cancellationToken);
        // The plan repeats its items' errors, which the selection skips per item; an error that names no item cannot be
        // attributed, so it refuses the run.
        var itemIds = plan.Items.Select(item => item.SourceVersionId).ToHashSet(StringComparer.Ordinal);
        if (plan.Diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.IsError && !itemIds.Contains(diagnostic.Metadata.GetValueOrDefault("SourceVersionId") ?? string.Empty)) is { } planError)
            return Result(scope, plan.Items, skipped, refusal: new(
                Elsa3ImportRunStage.Analysis,
                planError.Code,
                Elsa3ImportSelection.Cause(planError.Message, planError.Guidance),
                [planError],
                inputs.Select(input => input.Definition.Id).ToArray(),
                Exception: null));

        var (selected, analysisSkips) = selection.SelectPlan(plan, inputsById);
        skipped.AddRange(analysisSkips);
        request.OnSkips?.Invoke(scope, skipped);

        var (result, refusal) = await ApplyAsync(request, importer, plan, collection, selected, inputsById, skipped, scope, startId, cancellationToken);
        if (refusal is not null)
            return Result(scope, plan.Items, skipped, refusal: refusal);
        if (result is null)
            return Result(scope, plan.Items, skipped);

        // A reusable the publisher did not publish blocks every workflow that uses it, its wrapper included.
        var refused = (result.Publications ?? [])
            .Where(state => state.Status is not (ReusableActivityPublicationStatus.Published or ReusableActivityPublicationStatus.AlreadyPublished))
            .ToDictionary(
                state => state.SourceVersionId,
                state => (state.ErrorCode ?? state.Status.ToString(), $"The reusable activity was not published ({state.Status}{(state.BlockedBy is null ? "" : $", blocked by '{state.BlockedBy}'")})."),
                StringComparer.Ordinal);
        var (publishable, publishSkips) = selection.Exclude(plan, refused, inputsById, result.AppliedSourceVersionIds, missingTargetState: "not published");
        skipped.AddRange(publishSkips);

        var publishableIds = publishable.ToHashSet(StringComparer.Ordinal);
        var workflows = (result.Workflows ?? []).Where(workflow => publishableIds.Contains(workflow.SourceVersionId)).ToArray();
        var outcomes = await services.GetRequiredService<ImportedWorkflowPublisher>().PublishWithOutcomesAsync(workflows, scope.Value, cancellationToken);
        // A workflow whose publication fails stays imported, and the next start tries again (FR-012); it is reported as
        // a skip, so strict mode fails on it (FR-015).
        foreach (var sourceVersionId in outcomes.Where(entry => entry.Value.Status == WorkflowPublicationStatus.Failed).Select(entry => entry.Key))
        {
            var item = plan.Items.First(candidate => candidate.SourceVersionId == sourceVersionId);
            skipped.Add(new(Elsa3ReconciliationDiagnosticCodes.PublishFailed, inputsById[sourceVersionId].FilePath,
                item.SourceDefinitionId, item.SourceVersion, "The imported workflow could not be published; the warning before this one says why.",
                SourceVersionId: sourceVersionId));
        }

        return Result(scope, plan.Items, skipped, result.Publications ?? [], Publications(workflows, outcomes), result.Warnings ?? []);
    }

    /// <summary>
    /// Applies the selection; when the apply refuses items it can locate, leaves them and their dependents out and
    /// applies again (spec 235 research R-5). Each round removes at least one item, so it ends. A refusal it cannot
    /// locate is returned.
    /// </summary>
    private async Task<(ReusableActivityImportApplyResult? Result, Elsa3ImportRunRefusal? Refusal)> ApplyAsync(
        Elsa3ImportRunRequest request,
        IReusableActivityCollectionImporter importer,
        ReusableActivityImportPlan plan,
        ReusableActivityImportCollection collection,
        IReadOnlyList<string> selected,
        IReadOnlyDictionary<string, Elsa3InputItem> inputsById,
        List<Elsa3SkippedItem> skipped,
        PersistenceScope scope,
        string startId,
        CancellationToken cancellationToken)
    {
        while (selected.Count > 0)
        {
            try
            {
                return (await importer.ApplyAsync(
                    new(plan.PlanId, collection, selected, TenantId: scope.Value, OperationKey: $"elsa3-reconcile:{request.SourceId}:{startId}"),
                    cancellationToken), null);
            }
            catch (ReusableActivityImportValidationException exception) when (Locate(exception, selected) is { Count: > 0 } located)
            {
                var (remaining, refusals) = selection.Exclude(plan, located, inputsById, selected);
                skipped.AddRange(refusals);
                request.OnSkips?.Invoke(scope, skipped);
                selected = remaining;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A credential-literal refusal, an identity collision thrown by the commit, or a refusal that names no
                // item: the import does not attribute it to one item (spec 235 research R-5).
                var validation = exception as ReusableActivityImportValidationException;
                var errors = validation?.Diagnostics.Where(diagnostic => diagnostic.IsError).ToArray() ?? [];
                var details = validation is null
                    ? string.Empty
                    : " " + string.Join(" ", errors.Select(diagnostic => $"{diagnostic.Code}: {Elsa3ImportSelection.Cause(diagnostic.Message, diagnostic.Guidance)}"));
                return (null, new(
                    Elsa3ImportRunStage.Apply,
                    errors.FirstOrDefault()?.Code,
                    $"{exception.Message}{details}",
                    validation?.Diagnostics ?? [],
                    selected,
                    exception));
            }
        }

        return (null, null);
    }

    /// <summary>
    /// The selected item each error diagnostic names, by its <c>SourceVersionId</c> metadata or else its source-version
    /// path segment; empty when any error names no selected item, because then the refusal cannot be attributed.
    /// </summary>
    internal static Dictionary<string, (string Code, string Cause)> Locate(ReusableActivityImportValidationException exception, IReadOnlyList<string> selected)
    {
        var selectedIds = selected.ToHashSet(StringComparer.Ordinal);
        var located = new Dictionary<string, (string, string)>(StringComparer.Ordinal);
        foreach (var diagnostic in exception.Diagnostics.Where(diagnostic => diagnostic.IsError))
        {
            var sourceVersionId = diagnostic.Metadata.GetValueOrDefault("SourceVersionId")
                                  ?? diagnostic.PathSegments.FirstOrDefault(segment => segment.Kind == Elsa3MigrationPathSegmentKind.SourceVersion)?.Identity;
            if (sourceVersionId is null || !selectedIds.Contains(sourceVersionId))
                return [];
            located.TryAdd(sourceVersionId, (diagnostic.Code, Elsa3ImportSelection.Cause(diagnostic.Message, diagnostic.Guidance)));
        }

        return located;
    }

    /// <summary>
    /// The publisher's outcome per newest version, and for each older version of the same workflow definition the
    /// version published instead.
    /// </summary>
    private static IReadOnlyList<Elsa3WorkflowPublication> Publications(
        IReadOnlyList<ReusableActivityImportedWorkflowVersion> workflows,
        IReadOnlyDictionary<string, WorkflowPublicationOutcome> outcomes)
    {
        var newestByDefinition = workflows
            .Where(workflow => outcomes.ContainsKey(workflow.SourceVersionId))
            .ToDictionary(workflow => workflow.WorkflowDefinitionId, workflow => workflow.SourceVersionId, StringComparer.Ordinal);
        return workflows
            .Select(workflow => outcomes.TryGetValue(workflow.SourceVersionId, out var outcome)
                ? new Elsa3WorkflowPublication(workflow, outcome.Status, null, outcome.Failure)
                : new Elsa3WorkflowPublication(workflow, null, newestByDefinition[workflow.WorkflowDefinitionId], null))
            .ToArray();
    }

    private static Elsa3ImportRunResult Result(
        PersistenceScope scope,
        IReadOnlyList<ReusableActivityImportItem> planItems,
        IReadOnlyList<Elsa3SkippedItem> skipped,
        IReadOnlyList<ReusableActivityPublicationState>? reusables = null,
        IReadOnlyList<Elsa3WorkflowPublication>? workflows = null,
        IReadOnlyList<Elsa3MigrationDiagnostic>? warnings = null,
        Elsa3ImportRunRefusal? refusal = null) =>
        new(scope, planItems, skipped.ToArray(), reusables ?? [], workflows ?? [], warnings ?? [], refusal);
}
