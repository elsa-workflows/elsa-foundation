using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Exceptions;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>
/// Imports the configured Elsa 3 files at engine start through the Elsa 3 collection import, once per persistence
/// scope, and publishes the imported workflows (spec 235).
/// </summary>
/// <remarks>
/// Order 4 runs after activity reconciliation (1), which puts the host's built-in activities in the catalog that
/// conversion reads, after workflow reconciliation (2) and the Git export (3), and before interrupted publications are
/// completed (5). Like the reconciliation tasks it runs on every node and takes no lock: the commit is idempotent and a
/// publication another node made first is recognised.
/// </remarks>
[Order(4)]
internal sealed class Elsa3FolderImportTask(
    IOptions<Elsa3JsonReconciliationOptions> options,
    Elsa3JsonFileReader reader,
    Elsa3ImportSelection selection,
    IPersistenceScopeRunner scopeRunner,
    ILogger<Elsa3FolderImportTask> logger) : IStartupTask
{
    private readonly Elsa3JsonReconciliationOptions _options = options.Value;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(_options, cancellationToken);
        var (inputs, inputSkips) = selection.SelectInput(read);
        var startId = Guid.NewGuid().ToString("N");

        await scopeRunner.RunAsync(
            (scope, operationScope, token) => new ValueTask(ImportAsync(scope, operationScope.ServiceProvider, inputs, inputSkips, startId, token)),
            cancellationToken);
    }

    private async Task ImportAsync(
        PersistenceScope scope,
        IServiceProvider services,
        IReadOnlyList<Elsa3InputItem> inputs,
        IReadOnlyList<Elsa3SkippedItem> inputSkips,
        string startId,
        CancellationToken cancellationToken)
    {
        var skipped = new List<Elsa3SkippedItem>(inputSkips);
        FailIfStrict(skipped, scope);
        if (inputs.Count == 0)
        {
            LogSummary(scope, 0, 0, 0, skipped);
            return;
        }

        services.GetRequiredService<Elsa3StartupPublishingGrant>().Grant(scope.Value);
        var importer = services.GetRequiredService<IReusableActivityCollectionImporter>();
        var inputsById = inputs.ToDictionary(input => input.Definition.Id, StringComparer.Ordinal);
        var collection = new ReusableActivityImportCollection(
            $"elsa3-reconcile:{_options.SourceId}",
            inputs.Select(input => input.Definition).ToArray());

        var plan = await importer.AnalyzeAsync(collection, cancellationToken);
        // The plan repeats its items' errors, which the selection skips per item; an error that names no item cannot be
        // attributed, so it fails the start.
        var itemIds = plan.Items.Select(item => item.SourceVersionId).ToHashSet(StringComparer.Ordinal);
        if (plan.Diagnostics.FirstOrDefault(diagnostic =>
                diagnostic.IsError && !itemIds.Contains(diagnostic.Metadata.GetValueOrDefault("SourceVersionId") ?? string.Empty)) is { } planError)
            throw new Elsa3ReconciliationFailedException(
                $"The Elsa 3 import of '{_options.SourceId}' was refused: {Elsa3ImportSelection.Cause(planError.Message, planError.Guidance)} ({planError.Code})");

        var (selected, analysisSkips) = selection.SelectPlan(plan, inputsById);
        skipped.AddRange(analysisSkips);
        FailIfStrict(skipped, scope);

        var result = await ApplyAsync(importer, plan, collection, selected, inputsById, skipped, scope, startId, cancellationToken);
        if (result is null)
        {
            LogSummary(scope, 0, 0, 0, skipped);
            return;
        }

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
        var publications = await services.GetRequiredService<ImportedWorkflowPublisher>().PublishAsync(workflows, scope.Value, cancellationToken);
        // A workflow whose publication fails stays imported, and the next start tries again (FR-012); it is reported as
        // a skip, so strict mode fails on it (FR-015).
        foreach (var sourceVersionId in publications.Where(entry => entry.Value == WorkflowPublicationStatus.Failed).Select(entry => entry.Key))
        {
            var item = plan.Items.First(candidate => candidate.SourceVersionId == sourceVersionId);
            skipped.Add(new(Elsa3ReconciliationDiagnosticCodes.PublishFailed, inputsById[sourceVersionId].FilePath,
                item.SourceDefinitionId, item.SourceVersion, "The imported workflow could not be published; the warning before this one says why."));
        }

        foreach (var warning in result.Warnings ?? [])
            logger.LogInformation("Elsa 3 import '{SourceId}': {Code} {Message}", _options.SourceId, warning.Code, warning.Message);

        var reusables = (result.Publications ?? []).Count(state => state.Status is ReusableActivityPublicationStatus.Published or ReusableActivityPublicationStatus.AlreadyPublished);
        LogSummary(scope, reusables, publications.Count, publications.Count(entry => entry.Value == WorkflowPublicationStatus.Published), skipped);
        FailIfStrict(skipped, scope, logSkipped: false);
    }

    /// <summary>
    /// Applies the selection; when the apply refuses items it can locate, leaves them and their dependents out and
    /// applies again (research R-5). Each round removes at least one item, so it ends.
    /// </summary>
    private async Task<ReusableActivityImportApplyResult?> ApplyAsync(
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
                return await importer.ApplyAsync(
                    new(plan.PlanId, collection, selected, TenantId: scope.Value, OperationKey: $"elsa3-reconcile:{_options.SourceId}:{startId}"),
                    cancellationToken);
            }
            catch (ReusableActivityImportValidationException exception) when (Locate(exception, selected) is { Count: > 0 } located)
            {
                var (remaining, refusals) = selection.Exclude(plan, located, inputsById, selected);
                skipped.AddRange(refusals);
                FailIfStrict(skipped, scope);
                selected = remaining;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                // A credential-literal refusal, an identity collision thrown by the commit, or a refusal that names no
                // item: the import does not attribute it to one item, so it fails the start (research R-5).
                var details = exception is ReusableActivityImportValidationException validation
                    ? " " + string.Join(" ", validation.Diagnostics.Where(diagnostic => diagnostic.IsError)
                        .Select(diagnostic => $"{diagnostic.Code}: {Elsa3ImportSelection.Cause(diagnostic.Message, diagnostic.Guidance)}"))
                    : string.Empty;
                throw new Elsa3ReconciliationFailedException(
                    $"The Elsa 3 import of '{_options.SourceId}' was refused, and the refusal names no single workflow: {exception.Message}{details}",
                    exception);
            }
        }

        return null;
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

    private void FailIfStrict(IReadOnlyList<Elsa3SkippedItem> skipped, PersistenceScope scope, bool logSkipped = true)
    {
        if (!_options.FailOnUnconvertible || skipped.Count == 0)
            return;
        if (logSkipped)
            LogSkipped(skipped);
        throw new Elsa3ReconciliationFailedException(
            $"The Elsa 3 import of '{_options.SourceId}' skipped {skipped.Count} item(s) in persistence scope '{scope.Value}', and FailOnUnconvertible is set: "
            + string.Join("; ", skipped.Select(item => $"{item.Code} {item.DefinitionId} v{item.Version} ({item.FilePath}): {item.Cause}")));
    }

    /// <param name="reusables">Reusable activities that are published, by this start or earlier.</param>
    /// <param name="workflows">Workflow definitions (wrappers included) whose newest imported version is publishable.</param>
    /// <param name="published">Of those, the ones this start published.</param>
    private void LogSummary(PersistenceScope scope, int reusables, int workflows, int published, IReadOnlyList<Elsa3SkippedItem> skipped)
    {
        LogSkipped(skipped);
        logger.LogInformation(
            "Elsa 3 import '{SourceId}' in persistence scope '{Scope}': {Reusables} reusable activities and {Workflows} workflows in place; {Published} workflows published by this start; {Skipped} skipped.",
            _options.SourceId, scope.Value, reusables, workflows, published, skipped.Count);
    }

    private void LogSkipped(IEnumerable<Elsa3SkippedItem> skipped)
    {
        foreach (var item in skipped)
            logger.LogWarning(
                "Elsa 3 import '{SourceId}' skipped {DefinitionId} version {Version} from '{FilePath}': {Code} {Cause} {BlockedBy}",
                _options.SourceId, item.DefinitionId, item.Version, item.FilePath, item.Code, item.Cause, item.BlockedBy);
    }
}
