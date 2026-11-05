using System.Reflection;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Readability;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.StateMachine.M0.Witness;

internal static class CauseWitness
{
    private const string WorkflowId = "m0-cause-workflow";
    private static readonly WorkflowExecutionPartition Partition = new(WorkflowExecutionPartition.DefaultValue);
    internal sealed record ActivityView(string InvocationId, string Node, string? ParentInvocationId, string Status,
        IReadOnlyDictionary<string, string> SchedulingMetadata, ActivityInputSnapshot? Inputs, JsonElement? Result);
    internal sealed record Snapshot(int Pid, string WorkflowStatus, string MachineInvocationId, string? Correlation,
        string? SourceReferenceId, string? Partition, string? Authority, ActivityView[] Activities,
        BookmarkState[] Bookmarks, CauseMachineState? Pending, CauseReport? Report);
    internal sealed record SourceLocator(string OutboxItemId, string PublisherInvocationId, string InputId, string IntentFingerprint);

    public static async Task<bool> RunIntegrationAsync(string directory)
    {
        RequireNew(directory);
        var scopePassed = await ScopeWitness.RunAsync(Path.Join(directory, "scopes"));
        var causePassed = scopePassed && await RunAsync(Path.Join(directory, "causes"));
        var scopes = await ReceiptsAsync(Path.Join(directory, "scopes", "scope-processes.json"));
        ScopeWitness.ProcessReceipt[] causes = scopePassed ? await ReceiptsAsync(Path.Join(directory, "causes", "cause-processes.json")) : [];
        var all = scopes.Concat(causes).ToArray();
        var passed = scopePassed && causePassed && all.Length == 21 && all.Select(item => item.Pid).Distinct().Count() == 21 &&
            all.All(item => item.ObservedExpectedResult);
        await WriteAsync(Path.Join(directory, "integration-processes.json"), new
        {
            capturedAt = DateTimeOffset.UtcNow, assembly = Assembly.GetExecutingAssembly().Location,
            plannedChildGenerations = 21, actualChildGenerations = all.Length, scopePassed,
            causeStatus = !scopePassed ? "not-run;scope prerequisite failed" : causePassed ? "expected-results" : "failed",
            passed, processes = all,
            limits = new[] { "Research integration proof only; Q1-Q3 remain open", "No secret/withholding or production inbox/retention proof", "No complete statechart eligibility, Studio or saga acceptance" }
        });
        return passed;
    }

    public static async Task<bool> RunAsync(string directory)
    {
        RequireNew(directory);
        var receipts = new List<ScopeWitness.ProcessReceipt>();
        foreach (var scenario in new[] { CauseExecutable.ChildDelayed, CauseExecutable.ParentDelayed, CauseExecutable.Distinct })
        {
            for (var generation = 1; generation <= 4; generation++)
            {
                var result = await ChildAsync(directory, scenario, generation, true);
                receipts.Add(result);
                if (!result.ObservedExpectedResult) break;
            }
            if (receipts.Any(item => !item.ObservedExpectedResult)) break;
        }
        if (receipts.Count == 12 && receipts.All(item => item.ObservedExpectedResult))
            for (var generation = 1; generation <= 3; generation++)
            {
                var result = await ChildAsync(directory, CauseExecutable.WrongRelay, generation, generation < 3);
                receipts.Add(result);
                if (!result.ObservedExpectedResult) break;
            }
        await WriteAsync(Path.Join(directory, "cause-processes.json"), new
        {
            capturedAt = DateTimeOffset.UtcNow, assembly = Assembly.GetExecutingAssembly().Location,
            plannedChildGenerations = 15, processes = receipts,
            limits = new[] { "Two-result join is fixture inspection, not a production trigger eligibility rule", "Source/cohort diagnostics never supply execution state", "No complete invocation inbox, arbitrary-composite or statechart proof" }
        });
        return receipts.Count == 15 && receipts.All(item => item.ObservedExpectedResult);
    }

    private static Task<ScopeWitness.ProcessReceipt> ChildAsync(string directory, string scenario, int generation, bool expectedSuccess) =>
        ScopeWitness.ChildAsync(directory, scenario, generation, expectedSuccess, "cause-generation", CauseProbeNames.WrongBindingDiagnostic);

    public static async Task GenerationAsync(string directory, string scenario, int generation)
    {
        if (scenario is not (CauseExecutable.ChildDelayed or CauseExecutable.ParentDelayed or CauseExecutable.Distinct or CauseExecutable.WrongRelay) ||
            generation is < 1 or > 4 || scenario == CauseExecutable.WrongRelay && generation > 3)
            throw new ArgumentException("Unknown cause scenario or generation.");
        Directory.CreateDirectory(directory);
        var database = Path.Join(directory, scenario + ".db");
        if (generation == 1 && File.Exists(database)) throw new InvalidOperationException("Do not reuse an earlier cause database.");
        if (generation > 1 && !File.Exists(database)) throw new InvalidOperationException("Prior cause database is absent.");
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                // Use the real host fleet/observed-gate bridge. No fixture version
                // answer or schema-row mutation substitutes for normal finalization.
                services.AddEfSchemaReadability();
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    RecoveryContinuationSigningKey = "m0-public-fixture-recovery-signing-material",
                    HierarchyCursorSigningKey = "m0-public-fixture-hierarchy-signing-material"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                services.AddPersistenceModes(options => { options.Mode = PersistenceMode.Balanced; options.RecordCompletion = true; });
            })
            .Build(WorkflowExecutionHarness.Identity, WorkflowId,
                Enumerable.Range(1, 500).Select(index => $"{scenario}-g{generation}-activity-{index}"));
        foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        try
        {
            await CheckHostReadinessAsync(harness, directory, scenario, generation);
            if (generation == 1)
            {
                var reference = await harness.PublishAsync(CauseExecutable.Create(scenario), "m0-cause-publication");
                await harness.StartPublishedAsync(reference, WorkflowId, partition: Partition);
                CheckArmed(await SnapshotAsync(harness));
            }
            else
            {
                var prior = await ReadAsync<Snapshot>(Path.Join(directory, $"{scenario}-g{generation - 1}.json"));
                var restored = await SnapshotAsync(harness);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.restored.json"), restored);
                CheckColdIdentity(prior, restored);
                if (generation == 2)
                {
                    CheckArmed(restored);
                    await ResumeAsync(harness, restored, CauseProbeNames.PublishControl, CauseExecutable.Payload("publish-control"));
                    // The normal publish completion has committed its durable source. Observe
                    // the physical source before the explicit normal outbox sweep; do not
                    // manufacture an intent, source, selected cohort or delivery command.
                    var sources = await CapturePendingSourcesAsync(harness, restored, scenario);
                    await WriteAsync(Path.Join(directory, scenario + "-source-locators.json"), sources);
                    await harness.SweepUntilQuietAsync();
                    var after = await SnapshotAsync(harness);
                    await CheckSourcesAsync(harness, directory, scenario, generation, restored, sources, after);
                    if (CauseExecutable.IsDistinct(scenario)) CheckTerminal(after, scenario);
                    else CheckDelayed(after, scenario, restored);
                }
                else
                {
                    var armed = await ReadAsync<Snapshot>(Path.Join(directory, scenario + "-g1.json"));
                    var sources = await ReadAsync<SourceLocator[]>(Path.Join(directory, scenario + "-source-locators.json"));
                    if (generation == 3 && !CauseExecutable.IsDistinct(scenario))
                    {
                        CheckDelayed(restored, scenario, armed);
                        await ResumeAsync(harness, restored, CauseProbeNames.Gate(CauseExecutable.HeldSource(scenario)!), CauseExecutable.Payload("B"));
                        await harness.SweepUntilQuietAsync();
                    }
                    var final = await SnapshotAsync(harness);
                    CheckTerminal(final, scenario);
                    await CheckSourcesAsync(harness, directory, scenario, generation, armed, sources, final);
                }
            }
            var snapshot = await SnapshotAsync(harness);
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.json"), snapshot);
            Console.WriteLine($"N10 {scenario} generation{generation}: assertions passed; workflow={snapshot.WorkflowStatus}");
        }
        catch
        {
            try { await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.failed-state.json"), await SnapshotAsync(harness)); }
            catch (Exception error)
            {
                await TryWriteFailureEvidenceAsync(Path.Join(directory, $"{scenario}-g{generation}.state-read-failure.json"),
                    new { FailureType = error.GetType().FullName, error.Message });
            }
            try
            {
                await using var scope = harness.Services.CreateAsyncScope();
                var incidents = await scope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowId);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.failed-incidents.json"),
                    incidents.Select(item => new { item.IncidentId, item.ActivityExecutionId, item.ExecutableNodeId, item.Status, item.FailureType, item.Message }).ToArray());
            }
            catch (Exception error)
            {
                // Failure diagnostics must not replace the original generation error.
                await TryWriteFailureEvidenceAsync(Path.Join(directory, $"{scenario}-g{generation}.incident-read-failure.json"),
                    new { FailureType = error.GetType().FullName, error.Message });
            }
            throw;
        }
    }

    private static async Task CheckHostReadinessAsync(WorkflowExecutionHarness harness, string directory, string scenario, int generation)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var services = scope.ServiceProvider;
        var dormancy = services.GetRequiredService<ISchemaDormancyCheck>();
        var owners = new
        {
            Dormancy = dormancy.GetType().FullName,
            ObservedSchema = services.GetRequiredService<IObservedSchemaFinalization>().GetType().FullName,
            Fleet = services.GetRequiredService<IEfSchemaFleet>().GetType().FullName,
            Router = services.GetRequiredService<IStimulusRouter>().GetType().FullName
        };
        var path = Path.Join(directory, $"{scenario}-g{generation}.host-readiness.json");
        var before = dormancy.Observe().SingleOrDefault(item => item.Family == "RuntimePostCommitOutbox");
        await WriteAsync(path, new { Status = "Checking", Owners = owners, SchemaBeforeCheck = before });
        try
        {
            await dormancy.EnsureAvailableAsync([new SchemaVersionRequirement("RuntimePostCommitOutbox", "2.0.0")],
                PublishStimulusConstants.PublishStimulusIntentKind);
        }
        catch (Exception error)
        {
            await TryWriteFailureEvidenceAsync(path, new { Status = "Failed", Owners = owners, SchemaBeforeCheck = before, FailureType = error.GetType().FullName, error.Message });
            throw;
        }
        await WriteAsync(path, new { Status = "Available", Owners = owners, Schema = dormancy.Observe().SingleOrDefault(item => item.Family == "RuntimePostCommitOutbox") });
    }

    private static async Task TryWriteFailureEvidenceAsync(string path, object evidence)
    {
        try { await WriteAsync(path, evidence); }
        catch (Exception)
        {
            // The original generation failure remains authoritative even if its
            // diagnostic sink is unavailable; missing evidence cannot qualify a run.
        }
    }

    private static async Task ResumeAsync(WorkflowExecutionHarness harness, Snapshot snapshot, string node, JsonElement payload)
    {
        var bookmark = snapshot.Bookmarks.Single(item => item.ExecutableNodeId == node);
        await using var scope = harness.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(
            WorkflowId, bookmark.StimulusType, bookmark.StimulusHash, payload, idempotencyKey: "cause-control-" + bookmark.BookmarkId,
            requestedBy: "m0-cause-fixture", metadata: null, payloadType: null, providerId: null, bookmarkId: bookmark.BookmarkId, testRunDelivery: null));
        Check(result.Status == BookmarkResumeDispatchStatus.Dispatched, "N10: exact current control wait must dispatch normally.");
    }

    private static async Task<SourceLocator[]> CapturePendingSourcesAsync(WorkflowExecutionHarness harness, Snapshot armed, string scenario)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var sources = await scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(
            new RuntimePostCommitOutboxQuery(DateTimeOffset.MaxValue, 3, WorkflowId, PublishStimulusConstants.PublishStimulusIntentKind));
        Check(sources.Count == (CauseExecutable.IsDistinct(scenario) ? 2 : 1), "N10: exact ordinary publish sources must be observable before the outbox sweep.");
        var activities = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
        return sources.Select(item =>
        {
            var publisher = activities.Single(state => state.InvocationId == item.Intent.ActivityExecutionId);
            Check(publisher.ParentActivityExecutionId == armed.MachineInvocationId && publisher.Status == ActivityExecutionStatus.Completed &&
                  publisher.Execution.ExecutableNodeId is CauseProbeNames.PublisherA or CauseProbeNames.PublisherB,
                "N10: a source must belong to an actual completed ordinary publisher under this machine.");
            var payload = item.Intent.Payload ?? throw new InvalidOperationException("Publish source has no payload.");
            Check(payload.GetProperty("isLocalEvent").GetBoolean() && payload.GetProperty("correlationId").GetString() == armed.MachineInvocationId,
                "N10: actual staged publication must be local and correlated to the machine invocation.");
            return new SourceLocator(item.OutboxItemId, publisher.InvocationId, CauseProbeNames.InputId(payload.GetProperty("payload")),
                RuntimeCheckpointCommitFingerprint.ComputeIntent(item.Intent));
        }).ToArray();
    }

    private static async Task CheckSourcesAsync(WorkflowExecutionHarness harness, string directory, string scenario, int generation,
        Snapshot armed, SourceLocator[] locators, Snapshot observed)
    {
        Check(locators.Length == (CauseExecutable.IsDistinct(scenario) ? 2 : 1) && locators.Select(item => item.OutboxItemId).Distinct().Count() == locators.Length,
            "N10: source locators must identify the actual distinct physical publishes.");
        await using var scope = harness.Services.CreateAsyncScope();
        var lookup = scope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
        var retained = new List<RuntimePostCommitOutboxItem>();
        foreach (var locator in locators)
        {
            // This exact durable read is assertion evidence only. Neither locator files
            // nor this diagnostic source view supply state or authority to execution.
            var source = await lookup.FindDurableAsync(locator.OutboxItemId) ?? throw new InvalidOperationException("N10: the exact committed source is absent.");
            Check(source.Status == RuntimePostCommitOutboxStatus.Delivered && source.Intent.WorkflowExecutionId == WorkflowId &&
                  source.Intent.ActivityExecutionId == locator.PublisherInvocationId && RuntimeCheckpointCommitFingerprint.ComputeIntent(source.Intent) == locator.IntentFingerprint,
                "N10: source identity, publisher and fingerprint must remain unchanged after delivery/restart.");
            var snapshot = source.RecipientSnapshot ?? throw new InvalidOperationException("N10: normal source delivery retained no recipient cohort.");
            Check(snapshot.Version == StimulusDeliverySnapshot.CurrentVersion && snapshot.RetentionAcceptanceVersion == StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion &&
                  snapshot.SourceOutboxItemId == source.OutboxItemId && snapshot.IntentFingerprint == locator.IntentFingerprint && snapshot.Starts.Count == 0,
                "N10: saved source must carry the accepted complete native cohort without starts.");
            string[] expectedSources = CauseExecutable.IsDistinct(scenario) ? [locator.InputId == "A" ? "child" : "parent"] : ["child", "parent"];
            Check(snapshot.Resumes.Count == expectedSources.Length, "N10: actual cohort must contain exactly the intended machine leaves.");
            foreach (var branch in expectedSources)
            {
                var original = armed.Bookmarks.Single(item => item.ExecutableNodeId == CauseProbeNames.Event(branch));
                var recipient = snapshot.Resumes.Single(item => item.ActivityExecutionId == original.ActivityExecutionId);
                Check(recipient.WorkflowExecutionId == WorkflowId && recipient.BookmarkId == original.BookmarkId &&
                      recipient.ExecutableNodeId == original.ExecutableNodeId && recipient.RegistrationCapture is not null,
                    "N10: cohort must preserve the exact originally armed wait and its registration capture.");
                var actual = observed.Activities.Single(item => item.InvocationId == recipient.ActivityExecutionId);
                var eventResult = actual.Result?.Deserialize<EventResult>(ProbeNames.Json);
                Check(actual.Status == "Completed" && eventResult?.Payload is { } payload && CauseProbeNames.InputId(payload) == locator.InputId &&
                      JsonElement.DeepEquals(payload, CauseExecutable.Payload(locator.InputId)),
                    "N10: the ordinary Event result must match its actual source payload.");
            }
            Check(snapshot.Resumes.All(item => item.ActivityExecutionId != armed.Activities.Single(activity => activity.Node == CauseProbeNames.Ambient).InvocationId),
                "N10: workflow-local delivery must still exclude the other machine correlation.");
            retained.Add(source);
        }
        await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.sources.json"), retained);
    }

    private static async Task<Snapshot> SnapshotAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var activities = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
        var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowId);
        var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowId);
        var machine = activities.Single(item => item.Execution.ExecutableNodeId == CauseProbeNames.Machine);
        var host = activities.Single(item => item.Execution.ExecutableNodeId == CauseProbeNames.Host);
        return new(Environment.ProcessId, workflow?.Status.ToString() ?? "Missing", machine.InvocationId,
            machine.VariableFrame?.Values[CauseProbeNames.Correlation].InlineValue?.GetString(), workflow?.PinnedSource?.SourceReferenceId,
            workflow?.Partition?.Value, workflow?.Authority?.SystemIdentity,
            activities.Select(item => new ActivityView(item.InvocationId, item.Execution.ExecutableNodeId, item.ParentActivityExecutionId,
                item.Status.ToString(), item.Provenance.Metadata, item.InputSnapshot, item.Completion?.Result.InlineValue)).ToArray(), bookmarks.ToArray(),
            machine.PrivateState?.Value.InlineValue?.Deserialize<CauseMachineState>(ProbeNames.Json),
            host.Completion?.Result.InlineValue?.Deserialize<CauseReport>(ProbeNames.Json));
    }

    private static void CheckArmed(Snapshot snapshot)
    {
        CheckPublished(snapshot);
        Check(snapshot.WorkflowStatus == "Running" && snapshot.Pending?.Phase == "Armed" && snapshot.Pending.Branches.Length == 0,
            "N10: both ordinary composite triggers must arm before a business event exists.");
        var expected = new[] { CauseProbeNames.Event("child"), CauseProbeNames.Event("parent"), CauseProbeNames.Ambient, CauseProbeNames.PublishControl };
        Check(snapshot.Bookmarks.Select(item => item.ExecutableNodeId).Order().SequenceEqual(expected.Order()), "N10: exact initial Event/control registrations must exist.");
        foreach (var source in new[] { "child", "parent" })
        {
            var boundary = snapshot.Activities.Single(item => item.Node == CauseProbeNames.Trigger(source));
            var body = snapshot.Activities.Single(item => item.Node == CauseProbeNames.Body(source));
            var wait = snapshot.Activities.Single(item => item.Node == CauseProbeNames.Event(source));
            Check(boundary.ParentInvocationId == snapshot.MachineInvocationId && body.ParentInvocationId == boundary.InvocationId &&
                  wait.ParentInvocationId == body.InvocationId && wait.Status == "Suspended", "N10: normal composite ancestry must own the actual Event wait.");
            Check(!boundary.SchedulingMetadata.ContainsKey(ProbeNames.InputId) && !wait.SchedulingMetadata.ContainsKey(ProbeNames.InputId),
                "N10: helper scheduling IDs must not pre-seed business-event identity.");
        }
    }

    private static void CheckDelayed(Snapshot snapshot, string scenario, Snapshot armed)
    {
        CheckPublished(snapshot);
        var held = CauseExecutable.HeldSource(scenario)!;
        Check(snapshot.WorkflowStatus == "Running" && snapshot.Pending is { Phase: "Published", Branches.Length: 1 },
            "N10: one composite must complete while its ordinary sibling still waits.");
        var first = snapshot.Pending!.Branches.Single();
        Check(first.Source != held && first.OriginInputId == "A" && first.FinalInputId == "A", "N10: the non-delayed composite must carry the actual common input.");
        Check(snapshot.Bookmarks.Length == 2 && snapshot.Bookmarks.Any(item => item.ExecutableNodeId == CauseProbeNames.Gate(held)),
            "N10: the delayed composite must retain its independent durable gate.");
        var ambient = armed.Activities.Single(item => item.Node == CauseProbeNames.Ambient);
        Check(snapshot.Activities.Single(item => item.InvocationId == ambient.InvocationId).Status == "Suspended" &&
              snapshot.Bookmarks.Any(item => item.ExecutableNodeId == CauseProbeNames.Ambient), "N10: unrelated same-name wait must remain untouched.");
        var relay = snapshot.Activities.Single(item => item.Node == CauseProbeNames.FirstRelay(held));
        Check(relay.Result is { } result && CauseProbeNames.InputId(result) == "A" && relay.Inputs is not null,
            "N10: delayed composite must have an ordinary committed input/result for A before restart.");
    }

    private static void CheckTerminal(Snapshot snapshot, string scenario)
    {
        CheckPublished(snapshot);
        var report = snapshot.Report ?? throw new InvalidOperationException("N10: terminal typed report is absent.");
        Check(snapshot.WorkflowStatus == "Completed" && snapshot.Bookmarks.Length == 0 && report.MachineInvocationId == snapshot.MachineInvocationId &&
              report.Branches.Length == 2 && report.Branches.Select(item => item.Source).Distinct().Count() == 2,
            "N10: normal completion must retain exactly the two actual trigger occurrences.");
        foreach (var branch in report.Branches)
        {
            var expectedId = CauseExecutable.IsDistinct(scenario) && branch.Source == "parent" ? "B" : "A";
            Check(branch.OriginInputId == expectedId, "N10: originating Event must identify its actual publish.");
            Check(branch.FinalInputId == expectedId && JsonElement.DeepEquals(branch.FinalPayload, CauseExecutable.Payload(expectedId)), CauseProbeNames.WrongBindingDiagnostic);
            CheckBranchIdentity(snapshot, branch);
        }
        Check(report.Branches.Select(item => item.FinalInputId).Distinct(StringComparer.Ordinal).Count() == (CauseExecutable.IsDistinct(scenario) ? 2 : 1),
            "N10: genuinely distinct inputs must remain distinct despite equal content fields.");
        if (!CauseExecutable.IsDistinct(scenario))
            Check(report.Branches[1].Source == CauseExecutable.HeldSource(scenario), "N10: opposite held variants must expose their actual opposite completion orders.");
        Check(report.PublisherInvocationIds.Length == (CauseExecutable.IsDistinct(scenario) ? 2 : 1) && report.AmbientWasSuspendedBeforeCleanup &&
              snapshot.Activities.Single(item => item.InvocationId == report.AmbientWaitInvocationId).Status == "Cancelled",
            "N10: isolate the unrelated wait first, then retire only that owned wait during fixture closeout.");
    }

    private static void CheckBranchIdentity(Snapshot snapshot, CauseBranchResult branch)
    {
        var boundary = snapshot.Activities.Single(item => item.InvocationId == branch.BoundaryInvocationId);
        var body = snapshot.Activities.Single(item => item.InvocationId == branch.BodyInvocationId);
        var wait = snapshot.Activities.Single(item => item.InvocationId == branch.EventInvocationId);
        var first = snapshot.Activities.Single(item => item.InvocationId == branch.InitialRelayInvocationId);
        var final = snapshot.Activities.Single(item => item.InvocationId == branch.FinalRelayInvocationId);
        Check(boundary.Node == CauseProbeNames.Trigger(branch.Source) && boundary.ParentInvocationId == snapshot.MachineInvocationId && body.ParentInvocationId == boundary.InvocationId &&
              wait.ParentInvocationId == body.InvocationId && first.ParentInvocationId == body.InvocationId && final.ParentInvocationId == body.InvocationId &&
              new[] { boundary, body, wait, first, final }.All(item => item.Status == "Completed") && first.Inputs is not null && final.Inputs is not null,
            "N10: typed boundary evidence must belong to the exact completed ordinary composite/relay ancestry.");
    }

    private static void CheckColdIdentity(Snapshot prior, Snapshot restored)
    {
        CheckPublished(restored);
        Check(prior.MachineInvocationId == restored.MachineInvocationId && prior.Correlation == restored.Correlation && prior.WorkflowStatus == restored.WorkflowStatus &&
              prior.Bookmarks.Select(item => item.BookmarkId).Order().SequenceEqual(restored.Bookmarks.Select(item => item.BookmarkId).Order()),
            "N10: cold generation must restore exact invocation/correlation/wait identities.");
        foreach (var previous in prior.Activities)
            Check(restored.Activities.Any(item => item.InvocationId == previous.InvocationId && item.ParentInvocationId == previous.ParentInvocationId && item.Status == previous.Status),
                "N10: cold generation must retain every original activity occurrence and status.");
    }

    private static void CheckPublished(Snapshot snapshot) =>
        Check(snapshot.SourceReferenceId == "m0-cause-publication" && snapshot.Partition == Partition.Value &&
              snapshot.Authority == "activity-behavioural-drive" && snapshot.Correlation == snapshot.MachineInvocationId,
            "N10: actual published context and invocation-derived normal variable binding are required.");
    private static void RequireNew(string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any()) throw new InvalidOperationException("Use a new empty owned integration-evidence directory.");
        Directory.CreateDirectory(directory);
    }
    private static async Task<ScopeWitness.ProcessReceipt[]> ReceiptsAsync(string path)
    {
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(path));
        return document.RootElement.GetProperty("processes").Deserialize<ScopeWitness.ProcessReceipt[]>(ProbeNames.Json)
            ?? throw new InvalidOperationException("Child process inventory is absent.");
    }
    private static async Task<T> ReadAsync<T>(string path) where T : class =>
        JsonSerializer.Deserialize<T>(await File.ReadAllTextAsync(path), ProbeNames.Json) ?? throw new InvalidOperationException("Prior evidence is absent.");
    private static Task WriteAsync(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, ProbeNames.Json));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
