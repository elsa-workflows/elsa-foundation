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
        if (plan.Diagnostics.FirstOrDefault(diagnostic => diagnostic.IsError) is { } planError)
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
        var (publishable, publishSkips) = selection.Exclude(plan, refused, inputsById, result.AppliedSourceVersionIds);
        skipped.AddRange(publishSkips);

        var publishableIds = publishable.ToHashSet(StringComparer.Ordinal);
        var workflows = (result.Workflows ?? []).Where(workflow => publishableIds.Contains(workflow.SourceVersionId)).ToArray();
        // A workflow whose publication fails is logged by the publisher and stays imported; the next start tries again.
        var publications = await services.GetRequiredService<ImportedWorkflowPublisher>().PublishAsync(workflows, scope.Value, cancellationToken);

        foreach (var warning in result.Warnings ?? [])
            logger.LogInformation("Elsa 3 import '{SourceId}': {Code} {Message}", _options.SourceId, warning.Code, warning.Message);

        var importedActivities = (result.Publications ?? []).Count;
        LogSummary(scope, importedActivities, workflows.Length, publications.Count(entry => entry.Value == ImportedWorkflowPublication.Published), skipped);
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
                throw new Elsa3ReconciliationFailedException(
                    $"The Elsa 3 import of '{_options.SourceId}' was refused, and the refusal names no single workflow: {exception.Message}",
                    exception);
            }
        }

        return null;
    }

    private static Dictionary<string, (string Code, string Cause)> Locate(ReusableActivityImportValidationException exception, IReadOnlyList<string> selected)
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

    private void LogSummary(PersistenceScope scope, int activities, int workflows, int published, IReadOnlyList<Elsa3SkippedItem> skipped)
    {
        LogSkipped(skipped);
        logger.LogInformation(
            "Elsa 3 import '{SourceId}' in persistence scope '{Scope}': {ImportedActivities} reusable activities, {ImportedWorkflows} workflows imported; {PublishedWorkflows} workflows published; {Skipped} skipped.",
            _options.SourceId, scope.Value, activities, workflows, published, skipped.Count);
    }

    private void LogSkipped(IEnumerable<Elsa3SkippedItem> skipped)
    {
        foreach (var item in skipped)
            logger.LogWarning(
                "Elsa 3 import '{SourceId}' skipped {DefinitionId} version {Version} from '{FilePath}': {Code} {Cause} {BlockedBy}",
                _options.SourceId, item.DefinitionId, item.Version, item.FilePath, item.Code, item.Cause, item.BlockedBy);
    }
}
