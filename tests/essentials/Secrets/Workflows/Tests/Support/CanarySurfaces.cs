using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Elsa.Canary.Fixtures;
using Elsa.Workflows.ExecutionEvidence.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Secrets.Workflows.Tests.Support;

/// <summary>The eight surfaces of FR-013 the canary scans (spec 188, A14).</summary>
public enum CanarySurface
{
    StoredDefinitions,
    GitExport,
    RuntimeState,
    ExecutionEvidence,
    InspectorResponses,
    DiagnosticSnapshots,
    RuntimeTelemetry,
    RuntimeLogs
}

/// <summary>
/// One canary run and what its surfaces must show for it, so each surface's precondition can prove the surface holds
/// the run before the scan counts.
/// </summary>
/// <param name="Scenario">The A15 scenario, named in every failure.</param>
/// <param name="NodeId">The scenario's canary node, an id no other scenario uses.</param>
/// <param name="DefinitionId">The definition the run's version was published from; null for an imported artifact, which has none.</param>
/// <param name="SubjectInput">The canary input the scenario binds or plants its value on.</param>
/// <param name="ReferenceName">The secret reference the subject input is bound to, or null when it is not secret-bound.</param>
/// <param name="RuntimeMarker">Text the runtime database must hold for the run: the reference name of a withheld secret, or another marker.</param>
/// <param name="SubjectWithheld">
/// Whether the subject input reaches the inspection surfaces as the withheld reference; false where an injection replaced
/// the withheld envelope with a planted value.
/// </param>
/// <param name="CompletesInOneSegment">
/// Whether the run completes without suspending or faulting. Under coalesced persistence such a run's value records are
/// folded to the segment's durable boundary, so none of the subject input is kept; a run that suspends or faults keeps
/// them at that boundary.
/// </param>
public sealed record CanaryRun(
    string Scenario,
    string NodeId,
    string WorkflowExecutionId,
    string ArtifactId,
    string? SourceReferenceId,
    string? DefinitionId,
    string SubjectInput,
    string? ReferenceName,
    string RuntimeMarker,
    bool SubjectWithheld = true,
    bool CompletesInOneSegment = false)
{
    /// <summary>The reference the inspection surfaces must show for the subject input, if they show one.</summary>
    public string? ShownReference => SubjectWithheld ? ReferenceName : null;
}

/// <summary>One piece of a surface's content, named by where it was read from.</summary>
public sealed record CanaryContent(string Location, byte[] Bytes)
{
    public static CanaryContent Text(string location, string text) => new(location, Encoding.UTF8.GetBytes(text));
}

/// <summary>
/// How a scan reads its surfaces: the capture mode the diagnostics level gives a non-sensitive input, which runtime
/// tables an injection planted into, and who reads the inspector.
/// </summary>
public sealed record CanaryReadOptions(
    RuntimePayloadCaptureMode CompanionCapture,
    IReadOnlySet<string>? ExcludedRuntimeTables = null,
    CanaryCaller InspectorCaller = CanaryCaller.Operator);

/// <summary>The inspection granularity the instance view reports for each checkpoint cadence (ADR 0032 R3).</summary>
public static class CanaryInspectionGranularity
{
    public const string ActivityLevel = "activity-level";
    public const string BoundaryLevel = "boundary-level";
}

/// <summary>One surface as the canary read it: its content, and every precondition of the read that did not hold.</summary>
public sealed record CanarySurfaceRead(CanarySurface Surface, IReadOnlyList<CanaryContent> Contents, IReadOnlyList<string> FailedPreconditions);

/// <summary>
/// Reads the canary's surfaces (spec 188, T081, research R10). Each read checks that surface's precondition for every
/// run, which proves the surface holds the run, and reports every precondition that does not hold beside the content
/// it read, so a surface that does not hold the run is reported as such rather than passing its scan vacuously, and a
/// leak the content shows is reported even when a precondition failed too.
/// </summary>
public sealed class CanarySurfaces(SecretsCanaryWorkflowHost host)
{
    private const string RuntimeExecutablesTable = "elsa_runtime_workflow_executable";
    private const string RuntimeActivityStatesTable = "elsa_runtime_activity_execution_state";
    private const string RuntimeWorkflowStatesTable = "elsa_runtime_workflow_execution_state";
    private const string RuntimeInspectionsTable = "elsa_runtime_activity_execution_inspection";

    /// <summary>The surface's content, with every precondition that did not hold for a run in <paramref name="runs"/>.</summary>
    public async Task<CanarySurfaceRead> ReadAsync(CanarySurface surface, IReadOnlyCollection<CanaryRun> runs, CanaryReadOptions options)
    {
        var failures = new List<string>();
        var contents = surface switch
        {
            CanarySurface.StoredDefinitions => await ReadStoredDefinitionsAsync(runs, failures),
            CanarySurface.GitExport => await ReadGitExportAsync(runs, failures),
            CanarySurface.RuntimeState => await ReadRuntimeStateAsync(runs, options.ExcludedRuntimeTables, failures),
            CanarySurface.ExecutionEvidence => ReadEvidence(runs, failures),
            CanarySurface.InspectorResponses => await ReadInspectorAsync(runs, options.InspectorCaller, failures),
            CanarySurface.DiagnosticSnapshots => await ReadDiagnosticSnapshotsAsync(runs, options.CompanionCapture, failures),
            CanarySurface.RuntimeTelemetry => ReadTelemetry(runs, failures),
            CanarySurface.RuntimeLogs => await ReadLogsAsync(runs, failures),
            _ => throw new ArgumentOutOfRangeException(nameof(surface), surface, null)
        };
        if (contents.Count == 0)
            failures.Add($"{surface}: the surface has no content to scan.");
        return new CanarySurfaceRead(surface, contents, failures);
    }

    /// <summary>The design databases: every file under the design directory, and every cell of every table in them.</summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadStoredDefinitionsAsync(IReadOnlyCollection<CanaryRun> runs, List<string> failures)
    {
        var cells = await SecretsCanaryWorkflowHost.ReadDatabaseCellsAsync(host.DesignDirectory);
        foreach (var run in runs)
        {
            var definitionId = run.DefinitionId ?? throw new InvalidOperationException($"{run.Scenario}: an imported run has no stored definition to read.");
            Require(failures, Holds(cells, definitionId), $"{run.Scenario} precondition (stored definitions): the design database holds no row of definition '{definitionId}'.");
            if (run.ReferenceName is { } reference)
                Require(failures, Holds(cells, reference), $"{run.Scenario} precondition (stored definitions): no stored state of definition '{definitionId}' holds reference '{reference}'.");
        }

        return [.. Files(host.DesignDirectory, failures), .. cells];
    }

    /// <summary>The export tree's files and the export branch's history with every patch, once an export pass has run.</summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadGitExportAsync(IReadOnlyCollection<CanaryRun> runs, List<string> failures)
    {
        await host.ExportToGitAsync();
        var history = await host.ReadGitHistoryAsync();
        var files = host.GitTreeFiles();
        foreach (var run in runs)
        {
            var definitionId = run.DefinitionId ?? throw new InvalidOperationException($"{run.Scenario}: an imported run has no definition to export.");
            var versions = Path.Join(host.GitClonePath, SecretsCanaryWorkflowHost.GitWorkflowsPath, definitionId, "versions");
            Require(failures, files.Any(file => file.StartsWith(versions, StringComparison.Ordinal)), $"{run.Scenario} precondition (git export): the export tree has no version file of definition '{definitionId}'.");
            Require(failures, history.Contains(definitionId, StringComparison.Ordinal), $"{run.Scenario} precondition (git export): the export branch has no commit of definition '{definitionId}'.");
        }

        return [.. files.Select(file => new CanaryContent(file, CanaryScanner.ReadShared(file))), CanaryContent.Text("git log -p --all", history)];
    }

    /// <summary>
    /// The runtime database: every file under the runtime directory, and every cell of every table in it. When an
    /// injection planted a value in some tables (S5, S6, S8, S8b and S8c), every other table's cells are read, and so
    /// are the raw files of every database the host writes that holds none of those tables: the design databases, which
    /// no injection plants into, and any other runtime database. The raw bytes of the database that holds the planted
    /// tables (its file, <c>-wal</c> and <c>-shm</c>, freelist pages included) are not scanned in those scenarios: a
    /// planted table's residue there cannot be told apart from a leak, so a value that reaches that database only as
    /// raw bytes outside a live cell is not found by them.
    /// </summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadRuntimeStateAsync(IReadOnlyCollection<CanaryRun> runs, IReadOnlySet<string>? excludedTables, List<string> failures)
    {
        var cells = await SecretsCanaryWorkflowHost.ReadDatabaseCellsAsync(host.RuntimeDirectory, excludedTables);
        foreach (var run in runs)
        {
            Require(failures, Holds(cells, run.WorkflowExecutionId), $"{run.Scenario} precondition (runtime state): the runtime database holds no row of run '{run.WorkflowExecutionId}'.");
            Require(failures, Holds(cells, run.RuntimeMarker), $"{run.Scenario} precondition (runtime state): no runtime row holds '{run.RuntimeMarker}', the marker the run's state carries.");
        }

        if (excludedTables is null)
            return [.. Files(host.RuntimeDirectory, failures), .. cells];
        return [.. await UnplantedDatabaseFilesAsync(excludedTables, failures), .. cells];
    }

    /// <summary>
    /// The raw files of every database the host writes that holds none of <paramref name="excludedTables"/>, which must
    /// each exist in a runtime database, so the exclusion names something the injection planted.
    /// </summary>
    private async Task<IReadOnlyList<CanaryContent>> UnplantedDatabaseFilesAsync(IReadOnlySet<string> excludedTables, List<string> failures)
    {
        var plantedDatabases = new List<string>();
        var tables = new HashSet<string>(StringComparer.Ordinal);
        foreach (var database in Directory.EnumerateFiles(host.RuntimeDirectory, "*.db"))
        {
            var names = await SecretsCanaryWorkflowHost.ReadTableNamesOfAsync(database);
            tables.UnionWith(names);
            if (names.Any(excludedTables.Contains))
                plantedDatabases.Add(Path.GetFileName(database));
        }

        foreach (var table in excludedTables.Where(table => !tables.Contains(table)))
            failures.Add($"runtime state: the excluded table '{table}' does not exist, so the exclusion names nothing the injection planted.");

        var files = Files(host.DesignDirectory, failures)
            .Concat(Files(host.RuntimeDirectory, failures).Where(file => !plantedDatabases.Any(planted => Path.GetFileName(file.Location).StartsWith(planted, StringComparison.Ordinal))))
            .ToArray();
        Require(failures, files.Length > 0, "runtime state: no raw database file outside the planted database to scan.");
        return files;
    }

    /// <summary>The evidence records of each run, as the evidence store holds them.</summary>
    private IReadOnlyList<CanaryContent> ReadEvidence(IReadOnlyCollection<CanaryRun> runs, List<string> failures)
    {
        var contents = new List<CanaryContent>();
        foreach (var run in runs)
        {
            var records = host.EvidenceRecords(run.WorkflowExecutionId);
            Require(failures, records.Count > 0, $"{run.Scenario} precondition (execution evidence): no evidence record of run '{run.WorkflowExecutionId}'.");
            Require(
                failures,
                records.Any(record => record.Kind == ExecutionEvidenceKinds.Activity && record.ActivityType?.StartsWith("Elsa.Canary.Fixtures.", StringComparison.Ordinal) == true),
                $"{run.Scenario} precondition (execution evidence): no activity fact of a canary activity in run '{run.WorkflowExecutionId}'.");
            contents.Add(CanaryContent.Text($"evidence of {run.WorkflowExecutionId}", SecretsCanaryWorkflowHost.Serialize(records)));
        }

        return contents;
    }

    /// <summary>
    /// The run inspector's and the executable inspector's responses for each run: the instance, each activity execution,
    /// its descendants and each of its value evidence payloads, the incidents, the executable and its input sources.
    /// </summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadInspectorAsync(IReadOnlyCollection<CanaryRun> runs, CanaryCaller caller, List<string> failures)
    {
        var contents = new List<CanaryContent>();
        foreach (var run in runs)
        {
            var instancePath = $"runtime/workflows/instances/{run.WorkflowExecutionId}";
            var instance = await ReadRequiredAsync(run, instancePath, caller, contents, failures);
            Require(failures, instance.Contains(run.WorkflowExecutionId, StringComparison.Ordinal), $"{run.Scenario} precondition (inspector): the instance response does not name run '{run.WorkflowExecutionId}'.");
            var keepsValueRecords = KeepsValueRecords(run, instance, failures);

            var (activities, _) = await host.ReadRunAsync(run.WorkflowExecutionId);
            Require(failures, activities.Count > 0, $"{run.Scenario} precondition (inspector): run '{run.WorkflowExecutionId}' has no activity execution to read.");
            var subjectShown = false;
            var subjectRecorded = false;
            foreach (var activity in activities)
            {
                var activityPath = $"{instancePath}/activity-executions/{activity.InvocationId}";
                var view = JsonNode.Parse(await ReadRequiredAsync(run, activityPath, caller, contents, failures) is { Length: > 0 } body ? body : "{}")!;
                // A descendants page exists for an activity execution the hierarchy records as a scope; a leaf answers 404.
                await ReadAnsweredAsync($"{activityPath}/descendants", caller, contents);
                foreach (var snapshot in view["valueSnapshots"]?.AsArray() ?? [])
                {
                    var isSubject = (string?)snapshot?["inputKey"] == run.SubjectInput;
                    subjectRecorded |= isSubject;
                    if (isSubject && run.ShownReference is { } reference)
                        subjectShown |= (string?)snapshot!["secretReferenceName"] == reference;
                    await ReadAnsweredAsync($"{activityPath}/value-evidence/{(string?)snapshot?["evidenceId"]}/payload", caller, contents);
                }
            }

            if (run.ShownReference is not null && keepsValueRecords)
                Require(failures, subjectShown, $"{run.Scenario} precondition (inspector): no activity execution view shows reference '{run.ShownReference}' for input '{run.SubjectInput}'.");
            // A run folded under coalescing keeps no value record of the subject input, so the run inspector's
            // sensitive-value rule has nothing to hide there. That absence is asserted: a value record of the subject
            // input there is something the canary would have to bite on, so it turns the canary red.
            if (!keepsValueRecords)
                Require(failures, !subjectRecorded, $"{run.Scenario} (inspector, folded): an activity execution view shows a value record of input '{run.SubjectInput}', which coalesced persistence does not keep for a run that completes in one segment.");

            await ReadRequiredAsync(run, $"{instancePath}/incidents", caller, contents, failures);
            var executable = await ReadRequiredAsync(run, $"runtime/workflows/executables/{run.ArtifactId}", caller, contents, failures);
            Require(failures, executable.Contains(run.ArtifactId, StringComparison.Ordinal), $"{run.Scenario} precondition (inspector): the executable response does not name artifact '{run.ArtifactId}'.");
            if (run.SourceReferenceId is not null)
            {
                var sources = await ReadRequiredAsync(run, $"runtime/workflows/executables/{run.ArtifactId}/source-references/{Uri.EscapeDataString(run.SourceReferenceId)}/input-sources", caller, contents, failures);
                if (run.ReferenceName is { } reference)
                    Require(failures, sources.Contains(reference, StringComparison.Ordinal), $"{run.Scenario} precondition (inspector): the input sources do not show reference '{reference}'.");
            }
        }

        return contents;
    }

    /// <summary>
    /// The diagnostic snapshots of each run: the inspection projections of its activity executions, with every value
    /// record's captured payload, read through the inspection store.
    /// </summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadDiagnosticSnapshotsAsync(IReadOnlyCollection<CanaryRun> runs, RuntimePayloadCaptureMode companionCapture, List<string> failures)
    {
        var contents = new List<CanaryContent>();
        foreach (var run in runs)
        {
            var projections = await host.ReadInspectionsAsync(run.WorkflowExecutionId);
            contents.AddRange(projections.Select(projection => CanaryContent.Text($"inspection {projection.ActivityExecutionId}", SecretsCanaryWorkflowHost.Serialize(projection))));
            using var response = await host.Send(HttpMethod.Get, $"runtime/workflows/instances/{run.WorkflowExecutionId}", CanaryCaller.Operator);
            var instance = response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync() : string.Empty;
            var snapshots = projections.SelectMany(projection => projection.ValueSnapshots).ToArray();
            var subject = snapshots.Where(snapshot => snapshot.InputKey == run.SubjectInput).ToArray();
            if (!KeepsValueRecords(run, instance, failures))
            {
                // Under coalesced checkpoints the runtime folds inspection evidence to the segment's durable boundary
                // (ADR 0032 R3), which the instance view reports, so a run that completes in one segment leaves no value
                // record of the subject input, and the capture rules that protect one (P6, P7) have nothing to act on.
                // That absence is asserted rather than assumed: a value record of the subject input there turns the
                // canary red. Whatever the store does hold is scanned.
                Require(
                    failures,
                    subject.Length == 0,
                    $"{run.Scenario} (diagnostic snapshots, folded): the inspection store holds {subject.Length} value records of input '{run.SubjectInput}' in run '{run.WorkflowExecutionId}', which coalesced persistence does not keep for a run that completes in one segment.");
                continue;
            }

            // The factory ran: a non-sensitive input was captured at the configured level.
            Require(
                failures,
                snapshots.Any(snapshot => snapshot.InputKey == nameof(CanaryActivity.Companion) && snapshot.Payload is not null && snapshot.CaptureMode == companionCapture),
                $"{run.Scenario} precondition (diagnostic snapshots): the companion input's projection of run '{run.WorkflowExecutionId}' holds no snapshot captured at {companionCapture}.");
            Require(failures, subject.Length > 0, $"{run.Scenario} precondition (diagnostic snapshots): input '{run.SubjectInput}' has no projection in run '{run.WorkflowExecutionId}'.");
            if (run.ShownReference is { } reference)
                Require(
                    failures,
                    subject.Any(snapshot => snapshot.Metadata.GetValueOrDefault(RuntimeMetadataKeys.SecretReferenceName) == reference),
                    $"{run.Scenario} precondition (diagnostic snapshots): the projection of input '{run.SubjectInput}' does not show reference '{reference}'.");
        }

        return contents;
    }

    /// <summary>Every span the runtime emitted while the host ran, once each run has at least one.</summary>
    private IReadOnlyList<CanaryContent> ReadTelemetry(IReadOnlyCollection<CanaryRun> runs, List<string> failures)
    {
        var spans = host.Spans.Spans;
        foreach (var run in runs)
            Require(failures, spans.Any(span => span.WorkflowExecutionId == run.WorkflowExecutionId), $"{run.Scenario} precondition (runtime telemetry): no span of run '{run.WorkflowExecutionId}'.");
        return spans.Select((span, index) => CanaryContent.Text($"span {index} {span.Name}", span.Render())).ToArray();
    }

    /// <summary>
    /// Every log line the host wrote, once the runtime's own log output names each run: a line of a runtime category
    /// that names the run, one of its activity executions or the scenario's node. The runtime names those in its
    /// post-commit delivery lines; a line the host's request logging writes for an inspector read names the run too, but
    /// proves nothing about the runtime's output, so it does not count.
    /// </summary>
    private async Task<IReadOnlyList<CanaryContent>> ReadLogsAsync(IReadOnlyCollection<CanaryRun> runs, List<string> failures)
    {
        var lines = host.Logs.Lines;
        var runtimeLines = lines.Where(line => RuntimeCategories.Any(category => line.Contains($" {category}", StringComparison.Ordinal))).ToArray();
        foreach (var run in runs)
        {
            var (activities, _) = await host.ReadRunAsync(run.WorkflowExecutionId);
            string[] ids = [run.WorkflowExecutionId, run.NodeId, .. activities.Select(activity => activity.InvocationId)];
            Require(
                failures,
                runtimeLines.Any(line => ids.Any(id => line.Contains(id, StringComparison.Ordinal))),
                $"{run.Scenario} precondition (runtime logs): no runtime log line names run '{run.WorkflowExecutionId}', one of its activity executions or its node.");
        }

        return lines.Select((line, index) => CanaryContent.Text($"log line {index}", line)).ToArray();
    }

    private static readonly string[] RuntimeCategories = ["Elsa.Workflows.Runtime.", "Elsa.Activities.Runtime."];

    private async Task<string> ReadRequiredAsync(CanaryRun run, string path, CanaryCaller caller, List<CanaryContent> contents, List<string> failures)
    {
        using var response = await host.Send(HttpMethod.Get, path, caller);
        var body = await response.Content.ReadAsStringAsync();
        Require(failures, response.StatusCode == HttpStatusCode.OK, $"{run.Scenario} precondition (inspector): GET {path} as {caller} returned {(int)response.StatusCode}: {body}");
        contents.Add(CanaryContent.Text($"GET {path}", body));
        return response.StatusCode == HttpStatusCode.OK ? body : string.Empty;
    }

    /// <summary>A read whose answer, whatever it is (a page, a payload resolved, unavailable or denied, or not found), is scanned.</summary>
    private async Task ReadAnsweredAsync(string path, CanaryCaller caller, List<CanaryContent> contents)
    {
        using var response = await host.Send(HttpMethod.Get, path, caller);
        contents.Add(CanaryContent.Text($"GET {path} ({(int)response.StatusCode})", await response.Content.ReadAsStringAsync()));
    }

    /// <summary>
    /// Whether the run's value records are kept: always under activity-level inspection, and under boundary-level
    /// inspection unless the run completes in one segment. The granularity the instance view reports must be the one the
    /// host's checkpoint cadence implies.
    /// </summary>
    private bool KeepsValueRecords(CanaryRun run, string instanceView, List<string> failures)
    {
        var granularity = instanceView.Length == 0 ? null : (string?)JsonNode.Parse(instanceView)?["inspectionGranularity"];
        var expected = host.Mode == CanaryStartMode.Fused ? CanaryInspectionGranularity.BoundaryLevel : CanaryInspectionGranularity.ActivityLevel;
        Require(failures, granularity == expected, $"{run.Scenario} precondition (inspection granularity): the instance view reports '{granularity}', not the '{expected}' a {host.Mode} host implies.");
        return expected == CanaryInspectionGranularity.ActivityLevel || !run.CompletesInOneSegment;
    }

    private static void Require(List<string> failures, bool holds, string precondition)
    {
        if (!holds)
            failures.Add(precondition);
    }

    /// <summary>Every file under <paramref name="directory"/>; a directory with none fails the read's precondition.</summary>
    private static IReadOnlyList<CanaryContent> Files(string directory, List<string> failures)
    {
        if (!Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories).Any())
        {
            failures.Add($"precondition: no database file under '{directory}'.");
            return [];
        }

        return CanaryScanner.ReadDirectory(directory).Select(file => new CanaryContent(file.Path, file.Content)).ToArray();
    }

    /// <summary>True when some cell holds <paramref name="text"/> in any form the scanner searches for.</summary>
    private static bool Holds(IEnumerable<CanaryContent> cells, string text) =>
        cells.Any(cell => CanaryScanner.Find(cell.Bytes, text, cell.Location).Count > 0);

    /// <summary>The runtime table the executable artifacts are stored in: an imported artifact plants its literal there.</summary>
    public static IReadOnlySet<string> ExecutableArtifactTables { get; } = new HashSet<string>(StringComparer.Ordinal) { RuntimeExecutablesTable };

    /// <summary>The runtime table S8 plants into: the replanted input snapshot is committed with the activity execution state.</summary>
    public static IReadOnlySet<string> ReplantedSnapshotTables { get; } = new HashSet<string>(StringComparer.Ordinal) { RuntimeActivityStatesTable };

    /// <summary>
    /// The runtime tables S8b plants into: the replanted input snapshot, and the inspection projection the
    /// capture-everything policy captured it in.
    /// </summary>
    public static IReadOnlySet<string> CapturedSnapshotTables { get; } = new HashSet<string>(StringComparer.Ordinal) { RuntimeActivityStatesTable, RuntimeInspectionsTable };

    /// <summary>
    /// The runtime tables S8c plants into: the artifact, which carries the sensitive variable's initial value, and the
    /// workflow execution state, whose root variable frame holds it.
    /// </summary>
    public static IReadOnlySet<string> SensitiveVariableTables { get; } = new HashSet<string>(StringComparer.Ordinal) { RuntimeExecutablesTable, RuntimeWorkflowStatesTable };

    /// <summary>The runtime table the drainer records a handler fault in: S9's planted exception message lands there.</summary>
    public static IReadOnlySet<string> PoisonTables { get; } = new HashSet<string>(StringComparer.Ordinal) { "elsa_runtime_scheduler_poison" };
}
