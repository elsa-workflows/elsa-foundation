using Elsa.Activities.Design.Core.Models;
using Elsa.Workflows.Publishing.Core.Models;
using Elsa.Workflows.Publishing.Handlers;
using Elsa.Workflows.Publishing.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa3.Activities.Design.Import.Contracts;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Services;
using Elsa3.Activities.Design.Import.Reconciliation.Models;
using Elsa3.Activities.Design.Import.Reconciliation.Services;
using Elsa3.Migration.Tooling.Report;
using Elsa3.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa3.Migration.Tooling;

/// <summary>What one run returned to the CLI (contracts/cli.md).</summary>
/// <param name="ExitCode">0 or 1 with a report, 2 when refused.</param>
/// <param name="OutputPath">The output folder as it was given.</param>
/// <param name="Refusal">The refusal, on exit 2.</param>
internal sealed record MigrationRunResult(int ExitCode, string OutputPath, MigrationRefusal? Refusal);

/// <summary>
/// The migration pipeline the CLI calls in process (spec 1237 T008, T018): validate the request and read the export before
/// anything is composed, select the versions, compose the migration host's shell, run the shared Elsa 3 import and
/// publish, export what was published, build the redacted report and swap the output into <c>--out</c>.
/// </summary>
internal static class Elsa3MigrationRun
{
    /// <summary>The exit code of a refused run.</summary>
    public const int RefusedExitCode = 2;

    /// <summary>The source id the run imports under; it names the collection and the operation keys.</summary>
    public const string SourceId = "elsa3-migration";

    public static async Task<MigrationRunResult> RunAsync(MigrationRequest request, HostWiring wiring, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(wiring);

        if (request.Validate() is { } invalid)
            return Refused(request, invalid);

        // The export is read before anything is composed or created, so an input refusal leaves --out as it was.
        var (export, inputRefusal) = ExportInputReader.Read(request.ExportPath, Path.GetFullPath(request.OutputPath));
        if (inputRefusal is not null)
            return Refused(request, inputRefusal);

        var selection = VersionSelection.Select(export!.Items, request.AllVersions);
        var (composition, compositionRefusal) = await MigrationComposition.ComposeAsync(request.Command, wiring, cancellationToken);
        if (compositionRefusal is not null)
            return Refused(request, compositionRefusal);

        await using (composition!)
        {
            OutputSwap swap;
            try
            {
                swap = OutputSwap.Begin(request.OutputPath);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or NotSupportedException or ArgumentException)
            {
                return Refused(request, new(MigrationRefusal.Arguments, "The --out folder could not be created or written."));
            }

            using (swap)
            {
                var report = await MigrateAsync(request, export, selection, composition!, swap.StagingFolder, cancellationToken);
                var json = ReportJson.Serialize(report);
                try
                {
                    await File.WriteAllBytesAsync(Path.Combine(swap.StagingFolder, OutputSwap.ReportJsonName), json, cancellationToken);
                    await File.WriteAllBytesAsync(Path.Combine(swap.StagingFolder, OutputSwap.ReportHtmlName), ReportHtml.Render(json), cancellationToken);
                    swap.Commit();
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    // The swap puts the previous output back, or keeps it in its staging folder when it cannot.
                    return Refused(request, new(MigrationRefusal.Arguments,
                        "The output could not be written into the --out folder; its previous content was put back or kept in the staging folder."));
                }

                return new MigrationRunResult(report.ExitCode, request.OutputPath, null);
            }
        }
    }

    /// <summary>Runs the import and the export in the composed shell, and builds the report.</summary>
    private static async Task<MigrationReport> MigrateAsync(
        MigrationRequest request,
        ExportInput export,
        VersionSelectionResult selection,
        MigrationComposition composition,
        string stagingFolder,
        CancellationToken cancellationToken)
    {
        await using var scope = composition.Shell.BeginScope();
        var services = scope.ServiceProvider;
        var inputs = selection.Selected.Select(item => new Elsa3InputItem(item.File, item.Definition)).ToArray();
        var results = await services.GetRequiredService<Elsa3ImportRun>().RunAsync(new Elsa3ImportRunRequest(SourceId, inputs), cancellationToken);

        var collected = Collect(selection, results);
        var exported = await MigratedExporter.ExportAsync(services, collected.Candidates, Path.Combine(stagingFolder, OutputSwap.MigratedName), cancellationToken);

        var definitions = collected.Definitions
            .Select(definition => definition with
            {
                ExportedTo = exported.ExportedTo.GetValueOrDefault(definition.SourceVersionId),
                WrapperExportedTo = exported.WrapperExportedTo.GetValueOrDefault(definition.SourceVersionId)
            })
            .ToArray();
        var converted = selection.Selected.Where(item => !collected.ConversionRefused.Contains(item.Definition.Id)).Select(item => item.Definition);
        var facts = composition.Facts;

        return ReportBuilder.Build(new ReportInput(
            request.CommandName,
            request.ToolVersion,
            request.AllVersions,
            definitions,
            selection.NotConverted,
            MissingTargetDetector.Detect(converted, export.Items.Select(item => item.Definition)),
            collected.RunFindings,
            export.InputFindings,
            facts.Features,
            facts.SettingKeys,
            facts.PersistenceOverridden));
    }

    /// <summary>The run's findings per selected definition version, its run-level findings and what can be exported.</summary>
    internal static CollectedRun Collect(VersionSelectionResult selection, IReadOnlyList<Elsa3ImportRunResult> results)
    {
        var planItems = results.SelectMany(result => result.PlanItems)
            .GroupBy(item => item.SourceVersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        // Keyed by file too: two inputs of the same version (a duplicate-input skip) each get their own skip.
        // Each persistence scope reports the same selection again, so a skip repeated across scopes is one skip; the
        // duplicate-input skips of one scope are taken from the first scope only, one per input.
        var duplicateInputs = results.Take(1).SelectMany(result => result.Skipped)
            .Where(skip => skip.Code == Elsa3ReconciliationDiagnosticCodes.DuplicateInput);
        var otherSkips = results.SelectMany(result => result.Skipped)
            .Where(skip => skip.Code != Elsa3ReconciliationDiagnosticCodes.DuplicateInput)
            .DistinctBy(skip => (skip.Code, skip.SourceVersionId, skip.FilePath, skip.BlockedBy));
        var skips = duplicateInputs.Concat(otherSkips)
            .Where(skip => skip.SourceVersionId is not null)
            .ToLookup(skip => (skip.SourceVersionId!, skip.FilePath));
        var reusables = results.SelectMany(result => result.ReusablePublications)
            .GroupBy(state => state.SourceVersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        // A publication state names the dependency that blocked it by its imported activity version id; the report names
        // definitions by their Elsa 3 source version id.
        var sourceOfActivityVersion = reusables.Values
            .GroupBy(state => state.ActivityDefinitionVersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First().SourceVersionId, StringComparer.Ordinal);
        var workflows = results.SelectMany(result => result.WorkflowPublications)
            .GroupBy(publication => publication.Workflow.SourceVersionId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        // Warnings of applied items, and the conversion diagnostics of items the apply refused (their plan items carry none).
        var versionDiagnostics = results.SelectMany(result => result.Warnings.Concat(result.ApplyDiagnostics)).ToArray();
        var refusals = results.Select(result => result.Refusal).OfType<Elsa3ImportRunRefusal>().ToArray();
        var refusedIds = refusals.SelectMany(refusal => refusal.UnappliedSourceVersionIds).ToHashSet(StringComparer.Ordinal);
        var noPublished = selection.NoPublishedVersion.ToHashSet(StringComparer.Ordinal);

        var definitions = new List<CollectedDefinition>();
        var occurrences = new Dictionary<(string, string), int>();
        var conversionRefused = new HashSet<string>(StringComparer.Ordinal);
        var candidateWorkflows = new List<ExportWorkflow>();
        var candidateReusables = new List<ExportReusable>();
        foreach (var item in selection.Selected)
        {
            var definition = item.Definition;
            var sourceVersionId = definition.Id;
            planItems.TryGetValue(sourceVersionId, out var planItem);
            var findings = new List<CollectedFinding>();

            if (noPublished.Contains(sourceVersionId))
                findings.Add(VersionSelection.NoPublishedVersionFinding());

            var diagnostics = (planItem?.Diagnostics ?? [])
                .Concat(versionDiagnostics.Where(diagnostic => NamesVersion(diagnostic, sourceVersionId)))
                .DistinctBy(diagnostic => (diagnostic.Code, diagnostic.Path, diagnostic.Message, diagnostic.Severity))
                .ToArray();
            findings.AddRange(diagnostics.Select(Finding));
            if (diagnostics.Any(diagnostic => diagnostic.Severity == Elsa3MigrationDiagnosticSeverity.Error && Stage(diagnostic.Code) == FindingStage.Conversion))
                conversionRefused.Add(sourceVersionId);

            var blockedBy = new List<string>();
            var publishFailure = workflows.TryGetValue(sourceVersionId, out var failed) ? failed.PublishFailure : null;
            var failureFindings = publishFailure is null ? [] : PublishFindings(publishFailure).ToArray();
            reusables.TryGetValue(sourceVersionId, out var reusableState);
            var notPublished = planItem is { IsReusable: true } && reusableState is not null
                               && reusableState.Status is not (ReusableActivityPublicationStatus.Published or ReusableActivityPublicationStatus.AlreadyPublished);
            // A reusable whose publication a dependency blocked names that dependency (FR-017) and reports one finding that
            // points at it; publishing did not stop at it. Its own diagnostic's guidance speaks to the upload API.
            var blockingDependency = notPublished && reusableState!.BlockedBy is { } blockedActivityVersion
                ? sourceOfActivityVersion.GetValueOrDefault(blockedActivityVersion, blockedActivityVersion)
                : null;
            var reusablePublishFindings = !notPublished ? []
                : blockingDependency is not null ? [DependencyNotPublishedFinding()]
                : PublishFindings(reusableState!.Diagnostics ?? []).ToArray();
            if (blockingDependency is not null)
                blockedBy.Add(blockingDependency);

            // The same version twice in one file: each input gets one of its duplicate-input skips, in order.
            var key = (sourceVersionId, item.File);
            var occurrence = occurrences.GetValueOrDefault(key);
            occurrences[key] = occurrence + 1;
            var itemSkips = skips[key].Where(skip => skip.Code != Elsa3ReconciliationDiagnosticCodes.DuplicateInput)
                .Concat(skips[key].Where(skip => skip.Code == Elsa3ReconciliationDiagnosticCodes.DuplicateInput).Skip(occurrence).Take(1));
            foreach (var skip in itemSkips)
            {
                if (skip.BlockedBy is not null)
                    blockedBy.Add(skip.BlockedBy);
                // A skip for an error the conversion reported is that error, already a finding of its own.
                if (diagnostics.Any(diagnostic => diagnostic.Code == skip.Code && diagnostic.Severity == Elsa3MigrationDiagnosticSeverity.Error))
                    continue;

                // A publication refused with error diagnostics of its own is reported by them; its skip would repeat them.
                var isWorkflowPublishSkip = skip.Code == Elsa3ReconciliationDiagnosticCodes.PublishFailed;
                var publishSkip = isWorkflowPublishSkip || notPublished;
                var ownPublishFindings = isWorkflowPublishSkip ? failureFindings : reusablePublishFindings;
                if (publishSkip && ownPublishFindings.Any(finding => finding.Severity == FindingSeverity.Error))
                    continue;

                // Otherwise the skip reports the publication, which stopped there unless a dependency blocked it. The skip
                // of a reusable the publisher blocked carries no dependency; its publication state names it.
                var dependency = skip.BlockedBy ?? blockingDependency;
                var skipFinding = SkipFinding(skip, diagnostics, dependency);
                if (publishSkip)
                    skipFinding = skipFinding with { Stage = FindingStage.Publish, PublishStoppedHere = dependency is null };
                findings.Add(skipFinding);
            }

            var published = false;
            if (planItem is { IsReusable: true } && reusableState is not null)
            {
                published = !notPublished;
                if (published)
                    candidateReusables.Add(new ExportReusable(sourceVersionId, reusableState.ActivityDefinitionVersionId));
                else
                    findings.AddRange(reusablePublishFindings);
            }

            if (workflows.TryGetValue(sourceVersionId, out var workflow))
            {
                if (workflow.SupersededBySourceVersionId is not null)
                {
                    findings.Add(new(Elsa3MigrationToolingCodes.SupersededVersion, FindingSeverity.Info, FindingStage.Publish, null, null,
                        "A newer version of this definition was published instead; this version was imported but not published.", null));
                }
                else if (workflow.Status is WorkflowPublicationStatus.Published or WorkflowPublicationStatus.AlreadyPublished)
                {
                    candidateWorkflows.Add(new ExportWorkflow(sourceVersionId, workflow.Workflow.WorkflowDefinitionId, workflow.Workflow.WorkflowVersionId));
                    published |= planItem is not { IsReusable: true };
                }
                else
                {
                    findings.AddRange(failureFindings);
                }
            }

            if (refusedIds.Contains(sourceVersionId))
                findings.Add(new(Elsa3MigrationToolingCodes.RunRefused, FindingSeverity.Error, FindingStage.Import, null, null,
                    "The import was refused for the whole run; see the run findings.", null));

            definitions.Add(new CollectedDefinition(
                definition.DefinitionId,
                definition.Name,
                definition.Version,
                sourceVersionId,
                planItem?.IsReusable ?? false,
                published,
                ExportedTo: null,
                WrapperExportedTo: null,
                blockedBy.Distinct(StringComparer.Ordinal).ToArray(),
                findings));
        }

        var runFindings = refusals.SelectMany(RefusalFindings)
            .DistinctBy(finding => (finding.Code, finding.Path, finding.Message))
            .ToArray();
        return new CollectedRun(definitions, runFindings, conversionRefused, new ExportCandidates(candidateWorkflows, candidateReusables));
    }

    /// <summary>
    /// A run-level refusal's findings: its own diagnostics when it carries them (their messages are the converter's, or are
    /// replaced by the code table's fixed text), otherwise <c>ELS3-MIGRATION-IMPORT-REFUSED</c> with its fixed text. The
    /// refusal's cause and exception message are never copied (FR-019).
    /// </summary>
    private static IEnumerable<CollectedFinding> RefusalFindings(Elsa3ImportRunRefusal refusal)
    {
        var errors = refusal.Diagnostics.Where(diagnostic => diagnostic.Severity == Elsa3MigrationDiagnosticSeverity.Error).ToArray();
        if (refusal.Code is null || errors.Length == 0)
            return [new(Elsa3MigrationToolingCodes.ImportRefused, FindingSeverity.Error, FindingStage.Import, null, null, string.Empty, null)];
        // Every run finding is an import-stage finding (data-model), whoever owns its code.
        return errors.Select(error => Finding(error) with { Stage = FindingStage.Import });
    }

    /// <summary>
    /// A skip as an Error finding (FR-016). Its message is the matching diagnostic's own, which the converter redacts, or
    /// a fixed sentence; the skip's cause is never copied, because it can carry an Elsa 4 message (FR-019). A skip code
    /// that is a publication status rather than a code becomes a real code.
    /// </summary>
    private static CollectedFinding SkipFinding(Elsa3SkippedItem skip, IReadOnlyList<Elsa3MigrationDiagnostic> diagnostics, string? dependency)
    {
        // A reusable publication with no error code is skipped under its status name ('Refused', 'NotPublishedDependency').
        var isStatusName = skip.Code.All(char.IsLetter);
        // A refusal without a code is an Elsa 4 publication failure, which the code table's fixed text reports.
        var code = !isStatusName
            ? skip.Code
            : dependency is not null ? ReusableActivityImportDiagnosticCodes.DependencyNotPublished : ReusableActivityImportPublishPhase.PublicationFailed;
        var matching = diagnostics.FirstOrDefault(diagnostic => diagnostic.Code == code);
        return new CollectedFinding(
            code,
            FindingSeverity.Error,
            Stage(code),
            matching?.Path,
            matching is null ? null : NodeId(matching),
            matching?.Message ?? $"The definition was left out of the import ({code}).",
            matching?.Guidance,
            matching is null ? null : ComponentName(matching));
    }

    /// <summary>A reusable not published because a reusable it uses was not; its <c>blockedBy</c> names that one.</summary>
    private static CollectedFinding DependencyNotPublishedFinding() =>
        new(ReusableActivityImportDiagnosticCodes.DependencyNotPublished, FindingSeverity.Error, FindingStage.Publish, null, null,
            "This reusable activity was not published because a reusable activity it uses was not published.",
            "Fix the reusable activity its blockedBy names; this one publishes then.");

    /// <summary>A reusable's publication diagnostics: publish stops at the first error (FR-014).</summary>
    private static IEnumerable<CollectedFinding> PublishFindings(IReadOnlyList<ActivityDiagnostic> diagnostics)
    {
        var stopped = false;
        foreach (var diagnostic in diagnostics)
        {
            var severity = diagnostic.Severity switch
            {
                ActivityDiagnosticSeverity.Error => FindingSeverity.Error,
                ActivityDiagnosticSeverity.Warning => FindingSeverity.Warning,
                _ => FindingSeverity.Info
            };
            var stopsHere = severity == FindingSeverity.Error && !stopped;
            stopped |= stopsHere;
            yield return new(diagnostic.Code, severity, FindingStage.Publish, diagnostic.Location?.JsonPointer, null, diagnostic.Message,
                diagnostic.Remediation, PublishStoppedHere: stopsHere);
        }
    }

    /// <summary>
    /// A workflow publication's failure: its diagnostics when it carries them, otherwise its code; nothing for a failure
    /// without a code (a compilation refusal), which its <c>ELS3-RECONCILE-PUBLISH-FAILED</c> skip reports.
    /// </summary>
    private static IEnumerable<CollectedFinding> PublishFindings(Exception failure)
    {
        switch (failure)
        {
            case ExpressionPublicationValidationException validation when validation.Diagnostics.Count > 0:
                var stopped = false;
                foreach (var diagnostic in validation.Diagnostics)
                {
                    var severity = diagnostic.Severity.Equals("Error", StringComparison.OrdinalIgnoreCase) ? FindingSeverity.Error
                        : diagnostic.Severity.Equals("Warning", StringComparison.OrdinalIgnoreCase) ? FindingSeverity.Warning
                        : FindingSeverity.Info;
                    var stopsHere = severity == FindingSeverity.Error && !stopped;
                    stopped |= stopsHere;
                    yield return new(diagnostic.Code, severity, FindingStage.Publish, diagnostic.AuthoredPath, null, diagnostic.Message, null, PublishStoppedHere: stopsHere);
                }

                if (!stopped)
                    yield return PublishFailed(validation.Code);
                break;
            case ExpressionPublicationValidationException validation:
                yield return PublishFailed(validation.Code);
                break;
            case PublicationActivationException activation:
                yield return PublishFailed(activation.Code);
                break;
            case PublicationPreflightConflictException conflict:
                yield return PublishFailed(conflict.Code);
                break;
        }
    }

    /// <summary>An Elsa 4 publication code; the report gives it the code table's fixed text, never the Elsa 4 message.</summary>
    private static CollectedFinding PublishFailed(string code) =>
        new(code, FindingSeverity.Error, FindingStage.Publish, null, null, string.Empty, null, PublishStoppedHere: true);

    private static CollectedFinding Finding(Elsa3MigrationDiagnostic diagnostic) =>
        new(diagnostic.Code,
            diagnostic.Severity switch
            {
                Elsa3MigrationDiagnosticSeverity.Error => FindingSeverity.Error,
                Elsa3MigrationDiagnosticSeverity.Warning => FindingSeverity.Warning,
                _ => FindingSeverity.Info
            },
            Stage(diagnostic.Code),
            diagnostic.Path,
            NodeId(diagnostic),
            diagnostic.Message,
            diagnostic.Guidance,
            ComponentName(diagnostic));

    /// <summary>The import's own codes come from the import stage; every other diagnostic of a plan item from the converter.</summary>
    private static FindingStage Stage(string code) => ImportCodes.Contains(code) ? FindingStage.Import : FindingStage.Conversion;

    /// <summary>The codes the import, the reconciliation and this tooling own, read from their constant classes.</summary>
    private static readonly HashSet<string> ImportCodes = new[]
        {
            typeof(ReusableActivityImportDiagnosticCodes),
            typeof(Elsa3ReconciliationDiagnosticCodes),
            typeof(Elsa3MigrationToolingCodes)
        }
        .SelectMany(type => type.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
        .Where(field => field is { IsLiteral: true } && field.FieldType == typeof(string))
        .Select(field => (string)field.GetRawConstantValue()!)
        .ToHashSet(StringComparer.Ordinal);

    private static string? NodeId(Elsa3MigrationDiagnostic diagnostic) => diagnostic.Metadata.GetValueOrDefault("ActivityNodeId");

    private static string? ComponentName(Elsa3MigrationDiagnostic diagnostic) =>
        diagnostic.Metadata.GetValueOrDefault(Elsa3MigrationDiagnosticMetadataKeys.ComponentName);

    private static bool NamesVersion(Elsa3MigrationDiagnostic diagnostic, string sourceVersionId) =>
        diagnostic.Metadata.GetValueOrDefault("SourceVersionId") == sourceVersionId
        || diagnostic.PathSegments.Any(segment => segment.Kind == Elsa3MigrationPathSegmentKind.SourceVersion && segment.Identity == sourceVersionId);

    private static MigrationRunResult Refused(MigrationRequest request, MigrationRefusal refusal) =>
        new(RefusedExitCode, request.OutputPath, refusal);
}

/// <summary>What <see cref="Elsa3MigrationRun.Collect"/> gathered from the import.</summary>
/// <param name="ConversionRefused">The source version ids the converter refused.</param>
internal sealed record CollectedRun(
    IReadOnlyList<CollectedDefinition> Definitions,
    IReadOnlyList<CollectedFinding> RunFindings,
    IReadOnlySet<string> ConversionRefused,
    ExportCandidates Candidates);
