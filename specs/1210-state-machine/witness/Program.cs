using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Testing;
using Elsa.Activities.Sequence;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Contracts.Alterations;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Models.Alterations;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Elsa.Workflows.Runtime.Services.Alterations;
using Elsa.Workflows.Runtime.Services.Alterations.Handlers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;

namespace Elsa.StateMachine.M0.Witness;

internal static partial class Program
{
    private const string WorkflowId = "m0-witness-workflow";
    private const string PayloadCanary = "public-payload-must-not-appear-in-terminal-output";
    private static readonly WorkflowExecutionPartition Partition = new(WorkflowExecutionPartition.DefaultValue);

    public static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Use run, scope-run or a generation command with a new owned output directory.");
            if (args[0] == "scope-run") return await ScopeWitness.RunAsync(Path.GetFullPath(args[1])) ? 0 : 1;
            if (args[0] == "integration-run") return await CauseWitness.RunIntegrationAsync(Path.GetFullPath(args[1])) ? 0 : 1;
            if (args[0] == "cause-run") return await CauseWitness.RunAsync(Path.GetFullPath(args[1])) ? 0 : 1;
            if (args[0] == "scope-generation") await ScopeWitness.GenerationAsync(Path.GetFullPath(args[1]), args[2], int.Parse(args[3]));
            else if (args[0] == "cause-generation") await CauseWitness.GenerationAsync(Path.GetFullPath(args[1]), args[2], int.Parse(args[3]));
            else if (args[0] == "run") return await RunAsync(Path.GetFullPath(args[1])) ? 0 : 1;
            else if (args[0] == "generation") await GenerationAsync(Path.GetFullPath(args[1]), args[2], int.Parse(args[3]), args.ElementAtOrDefault(4));
            else throw new ArgumentException("Unknown command.");
            return 0;
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine(exception);
            return 1;
        }
    }

    private static async Task<bool> RunAsync(string directory)
    {
        // Refuse to reuse an old database/evidence set accidentally.
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("The owned output directory must be empty.");
        Directory.CreateDirectory(directory);
        var processes = new List<ProcessReceipt>();
        foreach (var scenario in new[] { "normal", "action-retry", "guard-retry", "retry-without-reschedule-marker", "complete", "cancel", "cancel-selected", "nested", "admission-blocked", "admission-race" })
            for (var generation = 1; generation <= 4; generation++)
                processes.Add(await ChildAsync(directory, scenario, generation, null, expectedSuccess: true));

        // Separate databases isolate negative controls from the positive evidence.
        processes.Add(await ChildAsync(directory, "negative-state", 1, "omit-state", expectedSuccess: false));
        processes.Add(await ChildAsync(directory, "negative-provenance", 1, "corrupt-provenance", expectedSuccess: false));
        processes.Add(await ChildAsync(directory, "admission-capacity-control", 1, "omit-in-flight-capacity", expectedSuccess: false));
        foreach (var (scenario, mutation) in new[] { ("negative-terminal", "omit-terminal-export"), ("negative-cancel", "omit-cancel-projection") })
        {
            processes.Add(await ChildAsync(directory, scenario, 1, mutation, expectedSuccess: true));
            processes.Add(await ChildAsync(directory, scenario, 2, mutation, expectedSuccess: false));
        }
        await WriteAsync(Path.Join(directory, "processes.json"), new
        {
            capturedAt = DateTimeOffset.UtcNow,
            assembly = Assembly.GetExecutingAssembly().Location,
            persistence = "Balanced / native EF runtime aggregate / SQLite",
            processes,
            limits = new[] { "Research fixture, not a StateMachine implementation", "Sequential ordinary Event carriers; no complete machine inbox/API guarantee", "Guard helper adapter, not all inline expression recovery", "No Studio, provider matrix or saga qualification" }
        });
        var passed = processes.All(x => x.ObservedExpectedResult);
        Console.WriteLine($"Focused native witness finished; all expected results={passed}. Evidence: {directory}");
        return passed;
    }

    private sealed record ProcessReceipt(string Scenario, int Generation, int Pid, string? Mutation, bool ExpectedSuccess, int ExitCode, bool ObservedExpectedResult);

    private static async Task<ProcessReceipt> ChildAsync(string directory, string scenario, int generation, string? mutation, bool expectedSuccess)
    {
        var assembly = Assembly.GetExecutingAssembly().Location;
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var argument in new[] { assembly, "generation", directory, scenario, generation.ToString() }) start.ArgumentList.Add(argument);
        if (mutation is not null) start.ArgumentList.Add(mutation);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not launch the owned child.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var output = await stdout;
        var error = await stderr;
        await File.WriteAllTextAsync(Path.Join(directory, $"{scenario}-g{generation}.stdout.log"), output);
        await File.WriteAllTextAsync(Path.Join(directory, $"{scenario}-g{generation}.stderr.log"), error);
        var expectedDiagnostic = mutation switch
        {
            "omit-state" => "No committed native parent state is available",
            "corrupt-provenance" => "Provenance negative control",
            "omit-terminal-export" => "N6: terminal archive must retain the original receipt IDs and order",
            "omit-cancel-projection" => "N6: queued cancellation must have an explicit not-processed reason",
            "omit-in-flight-capacity" => "N7: selected work must occupy one pending-capacity slot",
            _ => null
        };
        var matched = (process.ExitCode == 0) == expectedSuccess && (expectedSuccess || expectedDiagnostic is not null && error.Contains(expectedDiagnostic, StringComparison.Ordinal));
        var result = new ProcessReceipt(scenario, generation, process.Id, mutation, expectedSuccess, process.ExitCode, matched);
        await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.process.json"), result);
        return result;
    }

    private static async Task GenerationAsync(string directory, string scenario, int generation, string? mutation)
    {
        Directory.CreateDirectory(directory);
        var database = Path.Join(directory, scenario + ".db");
        var controls = new ProbeControls
        {
            FailGuardForInput = generation == 1 && (scenario is "guard-retry" or "retry-without-reschedule-marker" or "cancel-selected" || scenario.StartsWith("admission-", StringComparison.Ordinal)) ? "A" : null,
            OmitPrivateState = mutation == "omit-state",
            CorruptProvenance = mutation == "corrupt-provenance",
            DisableTerminalReceiptExport = mutation == "omit-terminal-export",
            DisableCancellationProjection = mutation == "omit-cancel-projection",
            IgnoreInFlightCapacity = mutation == "omit-in-flight-capacity"
        };
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(controls);
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    // Public fixture-only signing material; never used by a product host.
                    RecoveryContinuationSigningKey = "m0-public-fixture-recovery-signing-material",
                    HierarchyCursorSigningKey = "m0-public-fixture-hierarchy-signing-material"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                services.AddPersistenceModes(options => { options.Mode = PersistenceMode.Balanced; options.RecordCompletion = true; });
            })
            .Build(WorkflowExecutionHarness.Identity, WorkflowId, Enumerable.Range(1, 500).Select(i => $"{scenario}-g{generation}-activity-{i}"));
        foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        if (scenario.StartsWith("admission-", StringComparison.Ordinal))
        {
            await AdmissionGenerationAsync(harness, directory, scenario, generation);
            return;
        }
        var transport = new List<object>();
        object? recovery = null;
        try
        {
            if (generation == 1)
            {
                var reference = await harness.PublishAsync(Executable(scenario == "retry-without-reschedule-marker", scenario == "nested"), "m0-publication");
                // The harness publishes in its default persistence access scope. Start in that same
                // durable partition, and use it explicitly for subsequent operator/cancellation calls.
                await harness.StartPublishedAsync(reference, WorkflowId, partition: Partition);
                var start = await SnapshotAsync(harness);
                Check(start.RequiredState.Phase == "Action" && start.RequiredState.ExitCompletions == 1, "Ordinary Exit must finish once before the waiting Action.");
                await WriteAsync(Path.Join(directory, scenario + "-original-action.json"), await WaitAsync(harness, ProbeNames.Action));
                transport.Add(await AdmitAsync(harness, "A", parentFirst: true));
                transport.Add(await AdmitAsync(harness, "B"));
                transport.Add(await AdmitAsync(harness, "A", parentFirst: true));
                transport.Add(await AdmitAsync(harness, "A", parentFirst: false));
                transport.Add(await AdmitAsync(harness, "C"));
                var pending = await SnapshotAsync(harness);
                Check(pending.RequiredState.Pending.SequenceEqual(["A", "B"]) && pending.RequiredState.Processed.Count == 0, "N1: pending inputs must be durably ordered without evaluation during Action.");
                Check(pending.RequiredState.Decisions.Select(x => x.Status).SequenceEqual(["Accepted", "Accepted", "Duplicate", "Conflict", "Full"]), "N1: committed semantic decisions must distinguish admission, duplicate, conflict and full.");
                if (scenario == "action-retry") transport.Add(await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Action), new ActionSignal(Fault: true)));
                if (scenario is "guard-retry" or "retry-without-reschedule-marker" or "cancel-selected" || mutation == "corrupt-provenance")
                    transport.Add(await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Action), new ActionSignal()));
                if (scenario is "guard-retry" or "retry-without-reschedule-marker" or "cancel-selected" or "action-retry")
                    Check((await SnapshotAsync(harness)).RequiredState.AwaitingRecovery, "A real child fault must preserve the parent awaiting normal recovery.");
                if (mutation == "corrupt-provenance")
                    Check((await SnapshotAsync(harness)).RequiredState.Processed.SequenceEqual(["A", "B"]), "Provenance negative control: corrupted cause must fail the positive drain assertion.");
            }
            else if (generation == 2)
            {
                var restored = await SnapshotAsync(harness);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.restored.json"), restored);
                await CheckOriginalRootAsync(directory, scenario, restored);
                Check(restored.RequiredState.ExitCompletions == 1 && restored.RequiredState.Receipts.Count == 2, "N2: fresh process must recover native state and receipts.");
                if (scenario is "action-retry" or "guard-retry" or "retry-without-reschedule-marker")
                {
                    Check(restored.RequiredState.AwaitingRecovery && restored.RequiredState.FailedChild is not null, "Recovery must target the retained actual failed child.");
                    recovery = await RetryAsync(harness, restored.RequiredState.FailedChild!);
                    if (scenario == "action-retry")
                    {
                        var old = JsonSerializer.Deserialize<BookmarkState>(await File.ReadAllTextAsync(Path.Join(directory, scenario + "-original-action.json")), ProbeNames.Json)!;
                        var successor = await WaitAsync(harness, ProbeNames.Action);
                        Check(old.BookmarkId != successor.BookmarkId && old.ActivityExecutionId != successor.ActivityExecutionId, "N5: normal Action retry must create a distinct physical wait.");
                        transport.Add(await ResumeAsync(harness, old, new ActionSignal()));
                        Check((await WaitAsync(harness, ProbeNames.Action)).BookmarkId == successor.BookmarkId && (await SnapshotAsync(harness)).RequiredState.Processed.Count == 0,
                            "N5: saved retired Action bookmark must not advance the replacement Action.");
                        transport.Add(await ResumeAsync(harness, successor, new ActionSignal()));
                    }
                }
                else if (scenario is "cancel" or "cancel-selected" or "negative-cancel") await harness.CancelAsync(WorkflowId, Partition);
                else transport.Add(await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Action), new ActionSignal(Finish: scenario is "complete" or "negative-terminal")));
                var after = await SnapshotAsync(harness);
                if (scenario is "normal" or "action-retry" or "guard-retry" or "retry-without-reschedule-marker" or "nested")
                {
                    Check(after.RequiredState.Processed.SequenceEqual(["A", "B"]) && after.RequiredState.ExitCompletions == 1, "N2/N3: drain in admission order without repeating completed Exit.");
                    Check(after.RequiredState.Callbacks.GroupBy(x => x.InputId).All(group => group.Count() == 2 && group.Select(x => x.Source).ToHashSet().SetEquals(["child", "parent"])), "N4: both native guard callbacks must retain their scheduled logical input.");
                    Check(after.RequiredState.Selections.Count == 2 && after.RequiredState.Selections.All(x => x.Source == "child"), "N4: child precedence must be independent of callback order.");
                    Check(after.RequiredState.Callbacks.Where(x => x.InputId == "A").Select(x => x.Source).SequenceEqual(["parent", "child"]) &&
                          after.RequiredState.Callbacks.Where(x => x.InputId == "B").Select(x => x.Source).SequenceEqual(["child", "parent"]),
                        "N4: actual native callbacks must exercise both source orders.");
                    if (scenario == "guard-retry")
                    {
                        var previous = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Join(directory, scenario + "-g1.json")), ProbeNames.Json)!;
                        var successful = previous.RequiredState.Callbacks.Single();
                        Check(after.RequiredState.Callbacks.Count(x => x.InvocationId == successful.InvocationId && x.OperationKey == successful.OperationKey) == 1,
                            "N3: preserve the successful guard callback across sibling retry.");
                    }
                }
                if (scenario is "cancel" or "cancel-selected" or "complete" or "negative-terminal" or "negative-cancel")
                    CheckTerminal(after, scenario, ["A", "B"]);
            }
            else
            {
                var restored = await SnapshotAsync(harness);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.restored.json"), restored);
                await CheckOriginalRootAsync(directory, scenario, restored);
                if (generation == 3 && (scenario is "normal" or "action-retry" or "guard-retry" or "retry-without-reschedule-marker" or "nested"))
                {
                    Check(restored.RequiredState.Processed.SequenceEqual(["A", "B"]), "Cold verification must retain handled receipts.");
                    transport.Add(await AdmitAsync(harness, "stop"));
                }
                else CheckTerminal(restored, scenario, scenario is "normal" or "action-retry" or "guard-retry" or "retry-without-reschedule-marker" or "nested" ? ["A", "B", "stop"] : ["A", "B"]);
            }
            var final = await SnapshotAsync(harness);
            if (scenario == "nested") await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.nesting.json"), await CheckNestedAsync(harness, final));
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.json"), final);
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.delivery.json"), new { transport, recovery });
            Console.WriteLine($"{scenario} generation {generation}: assertions passed; phase={final.State?.Phase ?? "Terminal"}; workflow={final.WorkflowStatus}");
        }
        catch
        {
            // Persist the actual partial outcome before propagating a failed assertion; never fill a pass receipt.
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.delivery.json"), new { transport, recovery });
            try { await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.failed-state.json"), await SnapshotAsync(harness)); }
            catch (Exception readFailure) { await File.WriteAllTextAsync(Path.Join(directory, $"{scenario}-g{generation}.state-read-failure.log"), readFailure.ToString()); }
            await using var scope = harness.Services.CreateAsyncScope();
            var activities = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
            var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowId);
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.native-failure-readback.json"), new { workflow, activities });
            throw;
        }
    }

    private static async Task<object> AdmitAsync(WorkflowExecutionHarness harness, string id, bool parentFirst = false) =>
        await ResumeAsync(harness, await WaitAsync(harness, ProbeNames.Inbox), new ProbeInput(id, JsonSerializer.SerializeToElement(new { parentFirst, payloadCanary = PayloadCanary })));

    private static void CheckTerminal(Snapshot snapshot, string scenario, string[] expectedIds)
    {
        var terminal = snapshot.Terminal ?? throw new InvalidOperationException("N6: no terminal receipt view is available.");
        Check(terminal.FormatVersion == 1 && terminal.RootInvocationId == snapshot.RootInvocationId,
            "N6: terminal evidence must remain linked to the exact original root invocation.");
        Check(terminal.Receipts.Select(x => x.Id).SequenceEqual(expectedIds) && terminal.Receipts.Select(x => x.Order).SequenceEqual(Enumerable.Range(1, expectedIds.Length).Select(x => (long)x)),
            "N6: terminal archive must retain the original receipt IDs and order.");
        Check(terminal.Receipts.Length <= NativeProbeBase.ReceiptBudget, "N6: terminal evidence must stay bounded.");
        if (snapshot.RootStatus == "Completed")
        {
            Check(!snapshot.HasPrivateState && snapshot.State is null && snapshot.TerminalSource == "CompletionResult", "N6: ordinary completion must clear private state and retain only the declared result.");
            Check(!snapshot.CompletionResult!.Value.GetRawText().Contains(PayloadCanary, StringComparison.Ordinal), "N6: business payload must not leak into terminal output.");
        }
        else Check(snapshot.RootStatus == "Cancelled" && snapshot.TerminalSource == "CancelledRootState", "N6: cancellation facts must come from the terminal root state.");
        if (scenario is "cancel" or "negative-cancel")
            Check(terminal.Receipts.All(x => x.Status == "NotProcessed:Cancelled"), "N6: queued cancellation must have an explicit not-processed reason.");
        else if (scenario == "cancel-selected")
            Check(terminal.Receipts.Single(x => x.Id == "A").Status == "Interrupted:Cancelled" && terminal.Receipts.Single(x => x.Id == "B").Status == "NotProcessed:Cancelled",
                "N6: selected interruption must remain distinct from unselected cancellation.");
        else if (scenario is "complete" or "negative-terminal")
            Check(terminal.Receipts.All(x => x.Status == "NotProcessed:Completed"), "N6: completion must settle queued receipts explicitly.");
        else Check(terminal.Receipts.All(x => x.Status == "Handled"), "N6: handled receipts must remain handled after completion.");
    }

    private static async Task CheckOriginalRootAsync(string directory, string scenario, Snapshot restored)
    {
        var original = JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(Path.Join(directory, scenario + "-g1.json")), ProbeNames.Json)!;
        Check(restored.RootInvocationId == original.RootInvocationId, "Cold read must retain the original machine invocation identity.");
    }

    private sealed record DeliveryEvidence(string BookmarkId, string ActivityExecutionId, string TransportStatus, string? Reason);

    private static async Task<DeliveryEvidence> ResumeAsync(WorkflowExecutionHarness harness, BookmarkState bookmark, object payload)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(
            WorkflowId, bookmark.StimulusType, bookmark.StimulusHash, JsonSerializer.SerializeToElement(payload, ProbeNames.Json),
            idempotencyKey: Guid.NewGuid().ToString("N"), requestedBy: "m0-public-fixture", metadata: null, payloadType: null, providerId: null,
            bookmarkId: bookmark.BookmarkId, testRunDelivery: null));
        await harness.SweepUntilQuietAsync();
        return new(bookmark.BookmarkId, bookmark.ActivityExecutionId, result.Status.ToString(), result.Reason);
    }

    private static async Task<BookmarkState> WaitAsync(WorkflowExecutionHarness harness, string node)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        return (await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowId)).Single(x => x.ExecutableNodeId == node);
    }

    private sealed record ActivitySnapshot(string ActivityExecutionId, string Node, string Invocation, string Status, string OperationKey);
    private sealed record Snapshot(int Pid, string WorkflowStatus, string RootInvocationId, string RootStatus, bool HasPrivateState,
        ProbeState? State, ProbeTerminalEvidence? Terminal, string? TerminalSource, JsonElement? CompletionResult,
        ActivitySnapshot[] Activities, BookmarkState[] Bookmarks)
    {
        [JsonIgnore] public ProbeState RequiredState => State ?? throw new InvalidOperationException("This assertion requires an active native parent state.");
    }

    private static async Task<Snapshot> SnapshotAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var activities = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
        var root = activities.Single(x => x.Execution.ExecutableNodeId == ProbeNames.Root);
        var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowId);
        var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowId);
        var controls = harness.Services.GetRequiredService<ProbeControls>();
        var terminal = NativeProbeBase.ReadTerminalEvidence(root, !controls.DisableCancellationProjection);
        return new(Environment.ProcessId, workflow?.Status.ToString() ?? "Missing", root.InvocationId, root.Status.ToString(), root.PrivateState is not null,
            root.PrivateState is not null ? NativeProbeBase.Read(root) : terminal is null ? NativeProbeBase.Read(root) : null, terminal,
            root.Status == ActivityExecutionStatus.Completed ? "CompletionResult" : root.Status == ActivityExecutionStatus.Cancelled ? "CancelledRootState" : null,
            root.Completion?.Result.InlineValue,
            activities.Select(x => new ActivitySnapshot(x.InvocationId, x.Execution.ExecutableNodeId, x.InvocationId, x.Status.ToString(), x.OperationKey)).ToArray(), bookmarks.ToArray());
    }

    private static async Task<object> RetryAsync(WorkflowExecutionHarness harness, string activityId)
    {
        WorkflowAlterationPlanAdmissionResult admission;
        await using (var scope = harness.Services.CreateAsyncScope())
            admission = await scope.ServiceProvider.GetRequiredService<WorkflowAlterationPlanService>().SubmitAsync(new WorkflowAlterationSubmission(
                new WorkflowAlterationAuthorityScope(Partition.Value, "activity-behavioural-drive", "activity-behavioural-drive"),
                WorkflowAlterationTargetSelector.ForExecutionIds([WorkflowId]),
                [new WorkflowAlterationEnvelope(RetryFaultedActivityAlterationHandler.Kind, RetryFaultedActivityAlterationHandler.SchemaVersion,
                    JsonSerializer.SerializeToElement(new { sourceActivityExecutionId = activityId, reason = "M0 fixture participant restored" }))]),
                new WorkflowAlterationOperatorProvenance("m0-fixture-operator", null), "retry-" + activityId);
        var jobId = WorkflowAlterationIdentity.CreateJobId(admission.Plan.PlanId, WorkflowId);
        WorkflowAlterationJobState? job = null;
        for (var sweep = 0; sweep < 8; sweep++)
        {
            await using var scope = harness.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<WorkflowAlterationOrchestrationSweep>().ExecuteAsync(new WorkflowAlterationOrchestrationOptions { WorkerId = "m0-fixture-worker" }, "m0-fixture-worker");
            job = await scope.ServiceProvider.GetRequiredService<IWorkflowAlterationStore>().FindJobAsync(jobId);
            if (job?.Status is WorkflowAlterationJobStatus.Succeeded or WorkflowAlterationJobStatus.Failed or WorkflowAlterationJobStatus.Cancelled) break;
        }
        await harness.SweepUntilQuietAsync();
        Check(job?.Status == WorkflowAlterationJobStatus.Succeeded, $"Normal operator retry must succeed; actual status {job?.Status}.");
        return new { jobId, sourceActivityExecutionId = activityId, status = job!.Status.ToString() };
    }

    private static WorkflowExecutable Executable(bool withoutRescheduleOptIn, bool nested = false)
    {
        static RuntimeInputBinding Literal(string name, string alias, object value)
        {
            var type = new ValueTypeDescriptor(alias);
            return new(name, type, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
                literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        }
        static ExecutableNode Node(string id, Type type, Dictionary<string, RuntimeInputBinding>? inputs = null) =>
            new(id, "authored-" + id, type.FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }), inputs ?? [], new Dictionary<string, string>());
        var children = new[]
        {
            Node(ProbeNames.Exit, typeof(ExitProbe)), Node(ProbeNames.Action, typeof(ActionProbe)),
            new ExecutableNode(ProbeNames.Inbox, "authored-" + ProbeNames.Inbox, Event.ActivityType, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                new Dictionary<string, RuntimeInputBinding> { [nameof(Event.EventName)] = Literal(nameof(Event.EventName), "String", "m0-business"), [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), "Boolean", false) }, new Dictionary<string, string>()),
            Node(ProbeNames.ChildGuard, typeof(GuardProbe), new() { [nameof(GuardProbe.Source)] = Literal(nameof(GuardProbe.Source), "String", "child") }),
            Node(ProbeNames.ParentGuard, typeof(GuardProbe), new() { [nameof(GuardProbe.Source)] = Literal(nameof(GuardProbe.Source), "String", "parent") })
        };
        var root = new ExecutableNode(ProbeNames.Root, "authored-" + ProbeNames.Root, (withoutRescheduleOptIn ? typeof(NativeProbeWithoutRescheduleOptIn) : typeof(NativeProbe)).FullName!, "1.0.0", "test",
            JsonSerializer.SerializeToElement(new { type = "test" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
            [new ExecutableChildSlot(ProbeNames.Slot, children)], new ExecutableActivityStructure("m0.native-probe", "1.0.0", JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId) })));
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>();
        foreach (var (node, local) in new[] { (ProbeNames.Inbox, Event.ResumeTargetId), (ProbeNames.Action, ActionProbe.ResumeId) })
        {
            var id = WorkflowExecutableResumeTarget.ComposeScopedId(node, local);
            targets[id] = new(id, node, "ResumeAsync", new Dictionary<string, string>(), local);
        }
        if (nested)
        {
            var consumer = Node("m0-result-consumer", typeof(ReceiptConsumer), new()
            {
                [nameof(ReceiptConsumer.Evidence)] = new(nameof(ReceiptConsumer.Evidence), new ValueTypeDescriptor(typeof(ProbeTerminalEvidence).FullName!),
                    ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.ActivityResult,
                    activityResult: new RuntimeActivityResultReference(ProbeNames.Root, "$result", "scope:root"))
            });
            var nestedChildren = new[] { root, consumer };
            root = new("m0-outer-sequence", "authored-m0-outer-sequence", typeof(SequenceActivity).FullName!, "1.0.0", "test",
                JsonSerializer.SerializeToElement(new { type = "test" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, nestedChildren)],
                new ExecutableActivityStructure(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new { activities = nestedChildren.Select(x => x.ExecutableNodeId).ToArray() })));
        }
        return new(WorkflowExecutionHarness.Identity, root, targets, WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static Task WriteAsync(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, ProbeNames.Json));
}
