using System.Text.Json;
using Elsa.Activities.Behavioral.Infrastructure;
using Elsa.Activities.ControlFlow;
using Elsa.Activities.Graph.Runtime;
using Elsa.Activities.Graph.Runtime.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Scheduling.Activities;
using Elsa.Activities.Sequence;
using CShells.Lifecycle;
using Elsa.Activities.Testing;
using Elsa.Persistence.EntityFramework;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ParallelActivity = Elsa.Activities.Parallel.Activities.Parallel;

namespace Elsa.Activities.Behavioral;

/// <summary>
/// Spec 237, User Story 2 (SC-002): when a <c>Complete</c> node ends its graph boundary, every other live execution in
/// the body is cancelled durably, in the same commit: a sibling suspended on a bookmark and a sibling waiting on a
/// durable timer leave nothing behind in the stores, while work outside the boundary is untouched.
/// </summary>
public sealed class GraphBoundaryCompleteDurableCancellationTests
{
    private const string InsideEventNodeId = "node-event-inside";
    private const string OutsideEventNodeId = "node-event-outside";
    private const string DelayNodeId = "node-delay";
    private const string InsideEventName = "inside.event";
    private const string OutsideEventName = "outside.event";
    private const string DelayResumeTargetId = "resume-target:durable-timer";

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExitDeletesTheBodysBookmarksTimersAndQueuedWork(bool sqlite)
    {
        using var database = new TemporaryDatabase();
        await using var harness = NewHarness(sqlite ? database : null);

        var run = await harness.RunAsync(NewExecutable(), allowPendingWorkOnTerminalCompletion: true);

        // The boundary ended through the exit, and only through it.
        run.AssertOutcomes(GraphBoundaryCompleteTests.BoundaryNodeId, "Failed");
        run.AssertCompleted("node-exit");
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State(InsideEventNodeId).Status);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State(DelayNodeId).Status);
        // Both siblings really were suspended before the exit: each still records the bookmark it held.
        Assert.Single(run.State(InsideEventNodeId).BookmarkIds);
        Assert.Single(run.State(DelayNodeId).BookmarkIds);
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-body").Status);

        // Nothing of the body survives in the stores; the sibling outside the boundary keeps its bookmark (FR-012).
        var outside = run.State(OutsideEventNodeId);
        Assert.Equal(ActivityExecutionStatus.Suspended, outside.Status);
        var bookmarks = await harness.Services.GetRequiredService<IBookmarkStateStore>()
            .ListAllBookmarkStatesAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        var remaining = Assert.Single(bookmarks);
        Assert.Equal(OutsideEventNodeId, remaining.ExecutableNodeId);
        Assert.Empty(await harness.Services.GetRequiredService<IDurableTimerStore>()
            .ListAsync(WorkflowExecutionHarness.WorkflowExecutionId));
        var queued = await harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListNextWorkItemsAsync([WorkflowExecutionHarness.WorkflowExecutionId]);
        Assert.Empty(queued);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TheCancelledSiblingsBookmarkCannotBeResumed(bool sqlite)
    {
        using var database = new TemporaryDatabase();
        await using var harness = NewHarness(sqlite ? database : null);
        var run = await harness.RunAsync(NewExecutable(), allowPendingWorkOnTerminalCompletion: true);
        var cancelled = run.State(InsideEventNodeId);
        var bookmarkId = Assert.Single(cancelled.BookmarkIds);

        var resumed = await harness.ResumeAsync(
            WorkflowExecutionHarness.Identity,
            bookmarkId: bookmarkId,
            activityExecutionId: cancelled.InvocationId,
            executableNodeId: InsideEventNodeId,
            resumeTargetId: WorkflowExecutableResumeTarget.ComposeScopedId(InsideEventNodeId, Event.ResumeTargetId),
            stimulusType: EventStimulus.StimulusType,
            stimulusHash: EventStimulus.Hash(InsideEventName),
            input: JsonSerializer.SerializeToElement(new EventReceived(InsideEventName)));

        Assert.Equal(ActivityExecutionStatus.Cancelled, resumed.State(InsideEventNodeId).Status);
        run.AssertOutcomes(GraphBoundaryCompleteTests.BoundaryNodeId, "Failed");
    }

    // In-memory stores by default; with a database, the runtime's EF Sqlite stores, as DurableTimerRestartCrashTests
    // composes them.
    private static WorkflowExecutionHarness NewHarness(TemporaryDatabase? database)
    {
        var harness = WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new Workflows.Runtime.Scheduling.WorkflowsRuntimeSchedulingFeature().ConfigureServices(services))
            .WithFeature(services => new GraphActivitiesRuntimeFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesControlFlowFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                if (database is null)
                    return;

                services.AddRuntimeEntityFrameworkCore(new RuntimeEntityFrameworkCoreOptions
                {
                    Provider = "Sqlite",
                    ConnectionString = database.ConnectionString,
                    RecoveryContinuationSigningKey = "graph-complete-cancellation-signing-key-32b",
                    HierarchyCursorSigningKey = "graph-complete-cancellation-hierarchy-key-32b"
                });
                services.AddEfModuleMigrations<RuntimeDbContext>("Sqlite");
            })
            .Build(Enumerable.Range(1, 64).Select(index => $"actexec-{index}"));

        if (database is not null)
        {
            // A plain harness has no CShells Prepare phase, so install the module schema here.
            foreach (var initializer in harness.Services.GetServices<IShellInitializer>())
                initializer.InitializeAsync().GetAwaiter().GetResult();
        }

        return harness;
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"elsa-graph-complete-{Guid.NewGuid():N}.db");

        public string ConnectionString => $"Data Source={_path};Pooling=False";

        public void Dispose()
        {
            foreach (var file in new[] { _path, $"{_path}-shm", $"{_path}-wal" })
            {
                if (File.Exists(file))
                    File.Delete(file);
            }
        }
    }

    // Root Parallel: [graph boundary, an Event outside it]. The boundary body is a Parallel whose branches suspend on
    // an Event bookmark, wait on a Delay durable timer, and exit — in that order, so both siblings are suspended when
    // the exit runs.
    private static WorkflowExecutable NewExecutable()
    {
        var body = Parallel(
            "node-body",
            ("event", EventNode(InsideEventNodeId, InsideEventName)),
            ("delay", Nodes.Leaf(DelayNodeId, typeof(Delay), new Dictionary<string, object?> { ["Duration"] = TimeSpan.FromHours(1) })),
            ("exit", GraphBoundaryCompleteTests.CompleteNode("node-exit", "fail")));
        var boundary = GraphBoundaryCompleteTests.Boundary(body, completeOutcomes: [new GraphActivityCompleteOutcomeDescriptor("fail", "Failed")]);
        var root = Parallel("node-root", ("boundary", boundary), ("outside", EventNode(OutsideEventNodeId, OutsideEventName)));

        return new WorkflowExecutable(
            WorkflowExecutionHarness.Identity,
            root,
            new[]
            {
                ResumeTarget(InsideEventNodeId, Event.ResumeTargetId),
                ResumeTarget(OutsideEventNodeId, Event.ResumeTargetId),
                ResumeTarget(DelayNodeId, DelayResumeTargetId)
            }.ToDictionary(target => target.ResumeTargetId, StringComparer.Ordinal),
            new DateTimeOffset(2026, 6, 12, 12, 0, 0, TimeSpan.Zero),
            new Dictionary<string, string>(),
            IncidentStrategyBuiltIns.FaultReference);
    }

    private static WorkflowExecutableResumeTarget ResumeTarget(string nodeId, string localResumeTargetId)
    {
        var scopedId = WorkflowExecutableResumeTarget.ComposeScopedId(nodeId, localResumeTargetId);
        return new WorkflowExecutableResumeTarget(scopedId, nodeId, "ResumeAsync", new Dictionary<string, string>(), localResumeTargetId);
    }

    private static ExecutableNode EventNode(string nodeId, string eventName) =>
        Nodes.Leaf(nodeId, typeof(Event), new Dictionary<string, object?>
        {
            ["EventName"] = eventName,
            ["CanStartWorkflow"] = false
        });

    private static ExecutableNode Parallel(string nodeId, params (string Name, ExecutableNode Branch)[] branches) =>
        Nodes.Structural(
            nodeId,
            typeof(ParallelActivity),
            ParallelActivity.StructureKind,
            ParallelActivity.StructureSchemaVersion,
            new { branches = branches.Select(branch => new { name = branch.Name, activity = branch.Branch.ExecutableNodeId }).ToArray() },
            branches.Select(branch => new ExecutableChildSlot(ParallelActivity.BranchSlotName(branch.Name), [branch.Branch])).ToArray());
}
