using Elsa.Tasks.Core;
using Elsa.Tasks.Core.Attributes;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Exceptions;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Elsa3.Activities.Design.Import.Reconciliation.Services;

/// <summary>
/// Imports the configured Elsa 3 files at engine start through the Elsa 3 collection import, once per persistence
/// scope, and publishes the imported workflows (spec 235). The sequence is <see cref="Elsa3ImportRun"/>'s; this task
/// reads the files, logs the run's result and fails the start where the run reports a refusal.
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
    Elsa3ImportRun run,
    ILogger<Elsa3FolderImportTask> logger) : IStartupTask
{
    private readonly Elsa3JsonReconciliationOptions _options = options.Value;

    public async Task ExecuteAsync(CancellationToken cancellationToken)
    {
        var read = await reader.ReadAsync(_options, cancellationToken);
        await run.RunAsync(
            new(_options.SourceId, read)
            {
                OnSkips = (scope, skipped) => FailIfStrict(skipped, scope),
                OnScopeCompleted = Report
            },
            cancellationToken);
    }

    /// <summary>
    /// Logs one persistence scope's outcome; a refusal the run could not pin on one workflow fails the start (spec 235
    /// research R-5), and so does a skip in strict mode.
    /// </summary>
    private void Report(Elsa3ImportRunResult result)
    {
        if (result.Refusal is { } refusal)
            throw refusal.Stage == Elsa3ImportRunStage.Analysis
                ? new Elsa3ReconciliationFailedException($"The Elsa 3 import of '{_options.SourceId}' was refused: {refusal.Cause} ({refusal.Code})")
                : new Elsa3ReconciliationFailedException(
                    $"The Elsa 3 import of '{_options.SourceId}' was refused, and the refusal names no single workflow: {refusal.Cause}",
                    refusal.Exception!);

        foreach (var warning in result.Warnings)
            logger.LogInformation("Elsa 3 import '{SourceId}': {Code} {Message}", _options.SourceId, warning.Code, warning.Message);

        LogSummary(result.Scope, result.ReusablesInPlace, result.WorkflowsInPlace, result.WorkflowsPublished, result.Skipped);
        FailIfStrict(result.Skipped, result.Scope, logSkipped: false);
    }

    /// <inheritdoc cref="Elsa3ImportRun.Locate"/>
    internal static Dictionary<string, (string Code, string Cause)> Locate(ReusableActivityImportValidationException exception, IReadOnlyList<string> selected) =>
        Elsa3ImportRun.Locate(exception, selected);

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

    /// <param name="reusables">Imported reusable activity versions that have been published, by this start or earlier.</param>
    /// <param name="workflows">Workflow definitions (wrappers included) whose newest imported version has been published, by this start or earlier.</param>
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
