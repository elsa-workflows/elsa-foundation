using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.StateMachine.M0.Witness;

internal static class ScopeWitness
{
    private const string WorkflowId = "m0-scope-workflow";
    private const string Positive = "scope-visits";
    private const string WrongBinding = "scope-wrong-successor-binding";
    private const string NegativeDiagnostic = "N9: successor leaf must bind its own initial value";
    private static readonly WorkflowExecutionPartition Partition = new(WorkflowExecutionPartition.DefaultValue);

    internal sealed record ProcessReceipt(string Scenario, int Generation, int Pid, int ExitCode,
        bool ExpectedSuccess, bool ObservedExpectedResult, bool TimedOut);
    internal sealed record FrameView(string FrameId, string ScopeId, string ActivationId, string? ParentFrameId,
        string Status, long Revision, int Counter);
    internal sealed record ActivityView(string InvocationId, string Node, string? ParentInvocationId, string Status,
        FrameView? Frame, bool HasPrivateState, ActivityInputSnapshot? InputSnapshot, JsonElement? Result);
    internal sealed record Snapshot(int Pid, string WorkflowStatus, string MachineInvocationId,
        ActivityView[] Activities, BookmarkState[] Bookmarks, ScopeReport? Report,
        string? SourceReferenceId, string? Partition, string? Authority);

    public static async Task<bool> RunAsync(string directory)
    {
        if (Directory.Exists(directory) && Directory.EnumerateFileSystemEntries(directory).Any())
            throw new InvalidOperationException("Use a new empty owned scope-evidence directory.");
        Directory.CreateDirectory(directory);
        var receipts = new List<ProcessReceipt>();
        for (var generation = 1; generation <= 4; generation++)
        {
            var receipt = await ChildAsync(directory, Positive, generation, true);
            receipts.Add(receipt);
            if (!receipt.ObservedExpectedResult) break;
        }
        if (receipts.Count == 4 && receipts.All(item => item.ObservedExpectedResult))
        {
            var setup = await ChildAsync(directory, WrongBinding, 1, true);
            receipts.Add(setup);
            if (setup.ObservedExpectedResult)
                receipts.Add(await ChildAsync(directory, WrongBinding, 2, false));
        }
        await WriteAsync(Path.Join(directory, "scope-processes.json"), new
        {
            capturedAt = DateTimeOffset.UtcNow,
            assembly = Assembly.GetExecutingAssembly().Location,
            plannedChildGenerations = 6,
            processes = receipts,
            limits = new[] { "Research fixture, not StateMachine implementation", "Public integer values only; no secret/withholding proof", "Normal bindings and native visit containers; no production compiled topology", "N10 common-event/composite proof is separate and not executed by scope-run" }
        });
        return receipts.Count == 6 && receipts.All(item => item.ObservedExpectedResult);
    }

    internal static async Task<ProcessReceipt> ChildAsync(string directory, string scenario, int generation, bool expectedSuccess,
        string command = "scope-generation", string? negativeDiagnostic = null)
    {
        var start = new ProcessStartInfo("dotnet") { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var arg in new[] { Assembly.GetExecutingAssembly().Location, command, directory, scenario, generation.ToString(System.Globalization.CultureInfo.InvariantCulture) })
            start.ArgumentList.Add(arg);
        var outputPath = Path.Join(directory, $"{scenario}-g{generation}.stdout.log");
        var errorPath = Path.Join(directory, $"{scenario}-g{generation}.stderr.log");
        await using var outputFile = new FileStream(outputPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        await using var errorFile = new FileStream(errorPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
        using var child = Process.Start(start) ?? throw new InvalidOperationException("Scope child could not start.");
        var stdout = child.StandardOutput.BaseStream.CopyToAsync(outputFile);
        var stderr = child.StandardError.BaseStream.CopyToAsync(errorFile);
        var timedOut = false;
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await child.WaitForExitAsync(deadline.Token); }
        catch (OperationCanceledException)
        {
            timedOut = true;
            if (!child.HasExited) child.Kill(entireProcessTree: true);
            using var closeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            await child.WaitForExitAsync(closeout.Token);
            await File.WriteAllTextAsync(Path.Join(directory, $"{scenario}-g{generation}.timeout"), "Owned generation exceeded90seconds; qualification failed.");
        }
        await Task.WhenAll(stdout, stderr);
        await outputFile.FlushAsync();
        await errorFile.FlushAsync();
        using var errorReader = new StreamReader(new FileStream(errorPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite));
        var error = await errorReader.ReadToEndAsync();
        var matches = !timedOut && (expectedSuccess ? child.ExitCode == 0 : child.ExitCode != 0 && error.Contains(negativeDiagnostic ?? NegativeDiagnostic, StringComparison.Ordinal));
        var receipt = new ProcessReceipt(scenario, generation, child.Id, child.ExitCode, expectedSuccess, matches, timedOut);
        await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.process.json"), receipt);
        return receipt;
    }

    public static async Task GenerationAsync(string directory, string scenario, int generation)
    {
        if (scenario is not (Positive or WrongBinding) || generation is < 1 or > 4 || scenario == WrongBinding && generation > 2)
            throw new ArgumentException("Unknown scope scenario or generation.");
        Directory.CreateDirectory(directory);
        var database = Path.Join(directory, scenario + ".db");
        if (generation == 1 && File.Exists(database)) throw new InvalidOperationException("Do not reuse an earlier scope database.");
        if (generation > 1 && !File.Exists(database)) throw new InvalidOperationException("Prior scope generation database is absent.");
        await using var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False",
                    // Public research-only signing material, identical in purpose to the earlier fixture.
                    RecoveryContinuationSigningKey = "m0-public-fixture-recovery-signing-material",
                    HierarchyCursorSigningKey = "m0-public-fixture-hierarchy-signing-material"
                }).AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
                new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
                services.AddPersistenceModes(options => { options.Mode = PersistenceMode.Balanced; options.RecordCompletion = true; });
            })
            .Build(WorkflowExecutionHarness.Identity, WorkflowId,
                Enumerable.Range(1, 500).Select(index => $"{scenario}-g{generation}-activity-{index}"));
        foreach (var initializer in harness.Services.GetServices<IShellInitializer>()) await initializer.InitializeAsync();
        harness.InitializeActivityTypes();
        try
        {
            if (generation == 1)
            {
                var reference = await harness.PublishAsync(Executable(scenario == WrongBinding), "m0-scope-publication");
                await harness.StartPublishedAsync(reference, WorkflowId, partition: Partition);
                var started = await SnapshotAsync(harness);
                CheckWaiting(started, 1, 11, 21, 31);
                CheckObservation(started, 1, 10, 20, 30);
                CheckActiveFrameChain(started);
            }
            else
            {
                var previous = await ReadAsync(directory, scenario, generation - 1);
                var restored = await SnapshotAsync(harness);
                await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.restored.json"), restored);
                CheckSameCommittedFrames(previous, restored);
                if (generation == 2)
                {
                    await ResumeAsync(harness, restored, 1);
                    var second = await SnapshotAsync(harness);
                    CheckWaiting(second, 2, 12, 22, 32);
                    CheckObservation(second, 2, 11, 21, 30, NegativeDiagnostic);
                    CheckParentPreserved(previous, second);
                    CheckActiveFrameChain(second);
                }
                else if (generation == 3)
                {
                    await ResumeAsync(harness, restored, 2);
                    var reentered = await SnapshotAsync(harness);
                    await WriteAsync(Path.Join(directory, $"{scenario}-g3.parent-reentered.json"), reentered);
                    CheckWaiting(reentered, 3, 13, 21, 31);
                    CheckObservation(reentered, 3, 12, 20, 30);
                    CheckParentReentered(restored, reentered);
                    CheckActiveFrameChain(reentered);
                    await ResumeAsync(harness, reentered, 3);
                    CheckTerminal(await SnapshotAsync(harness));
                }
                else CheckTerminal(restored);
            }
            var final = await SnapshotAsync(harness);
            await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.json"), final);
            Console.WriteLine($"N9 {scenario} generation{generation}: assertions passed; workflow={final.WorkflowStatus}");
        }
        catch
        {
            try { await WriteAsync(Path.Join(directory, $"{scenario}-g{generation}.failed-state.json"), await SnapshotAsync(harness)); }
            catch (Exception readFailure) { await File.WriteAllTextAsync(Path.Join(directory, $"{scenario}-g{generation}.state-read-failure.log"), readFailure.ToString()); }
            throw;
        }
    }

    private static async Task ResumeAsync(WorkflowExecutionHarness harness, Snapshot snapshot, int stage)
    {
        var bookmark = snapshot.Bookmarks.Single(item => item.ExecutableNodeId == ScopeProbeNames.Wait(stage));
        await using var scope = harness.Services.CreateAsyncScope();
        var result = await scope.ServiceProvider.GetRequiredService<IBookmarkResumeDispatcher>().DispatchAsync(new BookmarkResumeDispatchRequest(
            WorkflowId, bookmark.StimulusType, bookmark.StimulusHash, JsonSerializer.SerializeToElement(new { finish = true }),
            idempotencyKey: $"scope-finish-{stage}", requestedBy: "m0-scope-fixture", metadata: null,
            payloadType: null, providerId: null, bookmarkId: bookmark.BookmarkId, testRunDelivery: null));
        Check(result.Status == BookmarkResumeDispatchStatus.Dispatched, "N9: exact current wait must dispatch normally.");
        await harness.SweepUntilQuietAsync();
    }

    private static async Task<Snapshot> SnapshotAsync(WorkflowExecutionHarness harness)
    {
        await using var scope = harness.Services.CreateAsyncScope();
        var states = await scope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(WorkflowId);
        var workflow = await scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowId);
        var bookmarks = await scope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(WorkflowId);
        var root = states.Single(item => item.Execution.ExecutableNodeId == ScopeProbeNames.Machine);
        static FrameView? Frame(ActivityExecutionState state) => state.VariableFrame is not { } frame ? null :
            new(frame.FrameId, frame.ScopeId, frame.ActivationId, frame.ParentFrameId, frame.Status.ToString(), frame.Revision,
                frame.Values[ScopeProbeNames.Counter].InlineValue?.GetInt32() ?? throw new InvalidOperationException("Public scope counter is not materialized."));
        return new(Environment.ProcessId, workflow?.Status.ToString() ?? "Missing", root.InvocationId,
            states.Select(item => new ActivityView(item.InvocationId, item.Execution.ExecutableNodeId, item.ParentActivityExecutionId,
                item.Status.ToString(), Frame(item), item.PrivateState is not null, item.InputSnapshot, item.Completion?.Result.InlineValue)).ToArray(),
            bookmarks.ToArray(), root.Completion?.Result.InlineValue?.Deserialize<ScopeReport>(ProbeNames.Json),
            workflow?.PinnedSource?.SourceReferenceId, workflow?.Partition?.Value, workflow?.Authority?.SystemIdentity);
    }

    private static void CheckWaiting(Snapshot snapshot, int stage, int machine, int parent, int leaf)
    {
        CheckPublishedContext(snapshot);
        Check(snapshot.WorkflowStatus == "Running", "N9: the workflow must remain active while its leaf waits.");
        Check(snapshot.Bookmarks.Length == 1 && snapshot.Bookmarks[0].ExecutableNodeId == ScopeProbeNames.Wait(stage), "N9: exactly the expected current leaf wait must remain.");
        Check(Active(snapshot, ScopeProbeNames.Machine).Frame!.Counter == machine && Active(snapshot, ScopeProbeNames.Parent).Frame!.Counter == parent &&
              Active(snapshot, ScopeProbeNames.Leaf).Frame!.Counter == leaf, "N9: normal Set work must commit to the correct three frames.");
    }

    private static void CheckObservation(Snapshot snapshot, int stage, int machine, int parent, int leaf, string? diagnostic = null)
    {
        var observer = snapshot.Activities.Single(item => item.Node == ScopeProbeNames.Observer(stage));
        var result = observer.Result?.Deserialize<ScopeObservation>(ProbeNames.Json);
        Check(result == new ScopeObservation(stage, machine, parent, leaf), diagnostic ?? "N9: ordinary observer inputs must resolve the expected lexical frames.");
        Check(observer.InputSnapshot is not null && observer.Status == "Completed", "N9: observer proof requires a normal committed input snapshot and completion.");
    }

    private static ActivityView Active(Snapshot snapshot, string node) => snapshot.Activities.Single(item => item.Node == node && item.Frame?.Status == "Active");

    private static void CheckActiveFrameChain(Snapshot snapshot)
    {
        var machine = Active(snapshot, ScopeProbeNames.Machine);
        var parent = Active(snapshot, ScopeProbeNames.Parent);
        var leaf = Active(snapshot, ScopeProbeNames.Leaf);
        Check(parent.ParentInvocationId == machine.InvocationId && leaf.ParentInvocationId == parent.InvocationId,
            "N9: actual container invocation ancestry must own the scopes.");
        Check(parent.Frame!.ParentFrameId == machine.Frame!.FrameId && leaf.Frame!.ParentFrameId == parent.Frame.FrameId,
            "N9: canonical lexical frame parentage must match the activity ancestry.");
        Check(new[] { machine, parent, leaf }.All(item => item.Frame!.ActivationId == item.InvocationId && item.Frame.ScopeId == item.Node),
            "N9: scope IDs and activation IDs must be runtime-owned, not copied private values.");
    }

    private static void CheckSameCommittedFrames(Snapshot previous, Snapshot restored)
    {
        CheckPublishedContext(restored);
        Check(previous.SourceReferenceId == restored.SourceReferenceId && previous.Partition == restored.Partition && previous.Authority == restored.Authority,
            "N9: cold restore must retain actual publication, partition and authority facts.");
        Check(previous.MachineInvocationId == restored.MachineInvocationId && previous.WorkflowStatus == restored.WorkflowStatus,
            "N9: a cold generation must restore the same machine invocation and workflow status.");
        foreach (var prior in previous.Activities.Where(item => item.Frame is not null))
        {
            var current = restored.Activities.Single(item => item.InvocationId == prior.InvocationId);
            Check(current.Frame == prior.Frame && current.Status == prior.Status, "N9: cold restore must preserve exact committed frame identity, revision, values and status.");
        }
        Check(previous.Bookmarks.Select(item => item.BookmarkId).Order().SequenceEqual(restored.Bookmarks.Select(item => item.BookmarkId).Order()),
            "N9: cold restore must preserve exact pending wait identities.");
    }

    private static void CheckParentPreserved(Snapshot previous, Snapshot current)
    {
        var parent = Active(previous, ScopeProbeNames.Parent);
        var oldLeaf = Active(previous, ScopeProbeNames.Leaf);
        var next = Active(current, ScopeProbeNames.Leaf);
        Check(Active(current, ScopeProbeNames.Parent).Frame!.FrameId == parent.Frame!.FrameId && next.Frame!.FrameId != oldLeaf.Frame!.FrameId &&
              current.Activities.Single(item => item.InvocationId == oldLeaf.InvocationId).Frame!.Status == "Closed",
            "N9: replacing a child must close only that visit and preserve its parent frame.");
        Check(next.Node == oldLeaf.Node && next.InvocationId != oldLeaf.InvocationId,
            "N9: re-entry must activate the same authored leaf node with a fresh runtime identity.");
    }

    private static void CheckParentReentered(Snapshot previous, Snapshot current)
    {
        var oldParent = Active(previous, ScopeProbeNames.Parent);
        var oldLeaf = Active(previous, ScopeProbeNames.Leaf);
        Check(Active(current, ScopeProbeNames.Parent).Frame!.FrameId != oldParent.Frame!.FrameId &&
              Active(current, ScopeProbeNames.Leaf).Frame!.FrameId != oldLeaf.Frame!.FrameId,
            "N9: parent re-entry must create fresh parent and leaf frames.");
        Check(current.Activities.Single(item => item.InvocationId == oldParent.InvocationId).Frame!.Status == "Closed" &&
              current.Activities.Single(item => item.InvocationId == oldLeaf.InvocationId).Frame!.Status == "Closed",
            "N9: departed parent and child frames must both close.");
        Check(Active(current, ScopeProbeNames.Machine).Frame!.FrameId == Active(previous, ScopeProbeNames.Machine).Frame!.FrameId,
            "N9: machine frame must survive parent re-entry.");
    }

    private static void CheckTerminal(Snapshot snapshot)
    {
        CheckPublishedContext(snapshot);
        Check(snapshot.WorkflowStatus == "Completed" && snapshot.Bookmarks.Length == 0, "N9: normal completion must retire all waits.");
        var frames = snapshot.Activities.Where(item => item.Frame is not null).ToArray();
        Check(frames.Length == 6 && frames.All(item => item.Status == "Completed" && item.Frame!.Status == "Closed" && !item.HasPrivateState),
            "N9: one machine, two parents and three leaves must complete with closed frames and cleared private state.");
        var expected = new[] { new ScopeObservation(1, 10, 20, 30), new ScopeObservation(2, 11, 21, 30), new ScopeObservation(3, 12, 20, 30) };
        Check(snapshot.Report?.Visits.Select(item => item.Observation).SequenceEqual(expected) == true,
            "N9: ordinary typed completion must retain exactly the three public observations.");
        Check(snapshot.Report!.Visits.Select(item => item.LeafInvocationId).Distinct(StringComparer.Ordinal).Count() == 3,
            "N9: each reported leaf visit must have a distinct actual activation.");
        Check(snapshot.Activities.Single(item => item.Node == ScopeProbeNames.Machine).Frame!.Counter == 13,
            "N9: final public machine value must come from the normal committed frame.");
    }

    private static void CheckPublishedContext(Snapshot snapshot) =>
        Check(snapshot.SourceReferenceId == "m0-scope-publication" && snapshot.Partition == Partition.Value && snapshot.Authority == "activity-behavioural-drive",
            "N9: evidence requires the normal published start's durable source, partition and authority.");

    private static WorkflowExecutable Executable(bool wrongSuccessorBinding)
    {
        static RuntimeInputBinding Literal(string key, string alias, object value) => new(key, new ValueTypeDescriptor(alias),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(new ValueTypeDescriptor(alias), JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
        static RuntimeInputBinding Read(string key, string scope) => new(key, new ValueTypeDescriptor("Int32"),
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.VariableRead, variable: new RuntimeVariableReference(ScopeProbeNames.Counter, scope));
        static ExecutableNode Plain(string id, string type, IReadOnlyDictionary<string, RuntimeInputBinding> inputs) =>
            new(id, "authored-" + id, type, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }), inputs, new Dictionary<string, string>());
        static ExecutableNode Set(string id, string scope, int value) => new(id, "authored-" + id, "elsa.intrinsic.set", "1.0.0", "intrinsic",
            JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding> { ["value"] = Literal("value", "Int32", value) },
            new Dictionary<string, string>(), intrinsicKind: WorkflowIntrinsicKind.Set, intrinsicVariable: new RuntimeVariableReference(ScopeProbeNames.Counter, scope));
        static ExecutableNode Container(string id, Type type, string kind, int initial, ExecutableNode[] children) =>
            new(id, "authored-" + id, type.FullName!, "1.0.0", "test", JsonSerializer.SerializeToElement(new { type = "test" }),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), [new ExecutableChildSlot(ScopeProbeNames.Slot, children)],
                new ExecutableActivityStructure(kind, "1.0.0", JsonSerializer.SerializeToElement(new
                {
                    activities = children.Select(child => child.ExecutableNodeId).ToArray(),
                    variables = new[] { new RuntimeVariableDeclaration(ScopeProbeNames.Counter, ScopeProbeNames.Counter, new ValueTypeDescriptor("Int32"),
                        ValueProtectionPolicy.InstanceInline, Literal(ScopeProbeNames.Counter, "Int32", initial)) }
                }, ProbeNames.Json)));
        var targets = new Dictionary<string, WorkflowExecutableResumeTarget>();
        var bodies = Enumerable.Range(1, 3).Select(stage =>
        {
            var observer = Plain(ScopeProbeNames.Observer(stage), typeof(ScopeValueObserver).FullName!, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(ScopeValueObserver.Stage)] = Literal(nameof(ScopeValueObserver.Stage), "Int32", stage),
                [nameof(ScopeValueObserver.Machine)] = Read(nameof(ScopeValueObserver.Machine), ScopeProbeNames.Machine),
                [nameof(ScopeValueObserver.Parent)] = Read(nameof(ScopeValueObserver.Parent), ScopeProbeNames.Parent),
                [nameof(ScopeValueObserver.Leaf)] = Read(nameof(ScopeValueObserver.Leaf), wrongSuccessorBinding && stage == 2 ? ScopeProbeNames.Parent : ScopeProbeNames.Leaf)
            });
            var wait = Plain(ScopeProbeNames.Wait(stage), Event.ActivityType, new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(Event.EventName)] = Literal(nameof(Event.EventName), "String", $"m0-scope-finish-{stage}"),
                [nameof(Event.CanStartWorkflow)] = Literal(nameof(Event.CanStartWorkflow), "Boolean", false)
            });
            var scopedTarget = WorkflowExecutableResumeTarget.ComposeScopedId(wait.ExecutableNodeId, Event.ResumeTargetId);
            targets[scopedTarget] = new(scopedTarget, wait.ExecutableNodeId, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId);
            var work = new[] { observer, Set($"m0-scope-set-machine-{stage}", ScopeProbeNames.Machine, 10 + stage),
                Set($"m0-scope-set-parent-{stage}", ScopeProbeNames.Parent, stage == 2 ? 22 : 21),
                Set($"m0-scope-set-leaf-{stage}", ScopeProbeNames.Leaf, stage == 2 ? 32 : 31), wait };
            return new ExecutableNode(ScopeProbeNames.Body(stage), "authored-" + ScopeProbeNames.Body(stage), typeof(SequenceActivity).FullName!, "1.0.0", "test",
                JsonSerializer.SerializeToElement(new { type = "test" }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, work)], new ExecutableActivityStructure(SequenceActivity.StructureKind,
                    SequenceActivity.StructureSchemaVersion, JsonSerializer.SerializeToElement(new { activities = work.Select(item => item.ExecutableNodeId).ToArray() })));
        }).ToArray();
        var leaf = Container(ScopeProbeNames.Leaf, typeof(ScopeLeafProbe), "m0.scope-leaf", 30, bodies);
        var parent = Container(ScopeProbeNames.Parent, typeof(ScopeParentProbe), "m0.scope-parent", 20, [leaf]);
        var machine = Container(ScopeProbeNames.Machine, typeof(ScopeMachineProbe), "m0.scope-machine", 10, [parent]);
        return new(WorkflowExecutionHarness.Identity, machine, targets, WorkflowExecutionHarness.Timestamp,
            new Dictionary<string, string>(), IncidentStrategyBuiltIns.FaultReference);
    }

    private static Task<Snapshot> ReadAsync(string directory, string scenario, int generation) =>
        ReadCoreAsync(Path.Join(directory, $"{scenario}-g{generation}.json"));
    private static async Task<Snapshot> ReadCoreAsync(string path) =>
        JsonSerializer.Deserialize<Snapshot>(await File.ReadAllTextAsync(path), ProbeNames.Json) ?? throw new InvalidOperationException("Prior scope evidence is absent.");
    private static Task WriteAsync(string path, object value) => File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, ProbeNames.Json));
    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
