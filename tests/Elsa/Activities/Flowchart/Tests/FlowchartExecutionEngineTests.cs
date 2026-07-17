using System.Text.Json;
using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
using Elsa.Activities.Flowchart.Internal;
using Elsa.Activities.Flowchart.Internal.Policies;
using Elsa.Activities.Flowchart.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Flowchart.Tests;

public sealed class FlowchartExecutionEngineTests
{
    [Fact]
    public async Task ImplicitJoin_RecordsJoinedDiagnostic()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Joined && diagnostic.NodeId == "node-d");
    }

    [Fact]
    public async Task Start_PersistsInitialStateBeforeFirstChildCompletion()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Completed);
    }

    [Fact]
    public async Task Start_CommitsInitialStateAndChildIntentAtomically_AndReplayDoesNotDuplicateChild()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var writer = Assert.IsType<InMemoryRuntimeCheckpointCommitStore>(fixture.Provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.DoesNotContain(writer.ListCommits(), write => write.Commit.Checkpoint.CheckpointId.Contains("flowchart-state", StringComparison.Ordinal));
        var atomicCommit = Assert.Single(writer.ListCommits(), write => SchedulesChild(write.Commit, "node-a")).Commit;
        var activityStateChange = Assert.Single(atomicCommit.StateChanges.ActivityExecutions);
        var serialized = activityStateChange.State.Metadata[FlowchartExecutionEngine.StateMetadataKey];
        var initialState = JsonSerializer.Deserialize<FlowchartExecutionState>(serialized, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                           ?? throw new InvalidOperationException("Flowchart execution state resolved to null.");

        // Start scheduled node-a as an active child on the root path/scope...
        var activeChild = Assert.Single(initialState.ActiveChildren);
        Assert.Equal("node-a", activeChild.NodeId);
        Assert.Equal("path:root", activeChild.ExecutionPathId);
        Assert.Equal("scope:root", activeChild.ExecutionScopeId);
        Assert.Equal("start", activeChild.SchedulingCause);
        // ...the root path is still Active (not yet Completed by any child completion)...
        var rootPath = Assert.Single(initialState.ExecutionPaths, path => path.ExecutionPathId == "path:root");
        Assert.Equal(ExecutionPathStatus.Active, rootPath.Status);
        Assert.Contains(initialState.Scopes, scope => scope.ExecutionScopeId == "scope:root");
        // ...and no terminal Completed diagnostic has been recorded yet.
        Assert.DoesNotContain(initialState.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Completed);

        // Simulate a worker crash after the atomic commit but before child-intent delivery. Replaying the
        // same commit recovers the same single outbox item; it cannot create a second child or lose the first.
        var replayStore = new InMemoryRuntimeCheckpointCommitStore();
        var committer = new RuntimeCheckpointCommitter(new ImmediateRuntimeCheckpointPersistencePolicy(), replayStore);
        var first = await committer.CommitAsync(atomicCommit);
        var replay = await committer.CommitAsync(atomicCommit);
        Assert.Equal(first.PendingPostCommitWorkIds, replay.PendingPostCommitWorkIds);
        Assert.Single(first.PendingPostCommitWorkIds);
        Assert.Single(replayStore.ListCommits());
        Assert.Single(await replayStore.GetDeliverableAsync(new RuntimePostCommitOutboxQuery(DateTimeOffset.MaxValue, 10)));
    }

    [Fact]
    public async Task ChildCompletion_CommitsNextStateAndChildIntentAtomically()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart", "actexec-a", "actexec-b"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a"), fixture.NewProbeNode("node-b")],
            connections: [fixture.NewConnection("node-a", "node-b")],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var writer = Assert.IsType<InMemoryRuntimeCheckpointCommitStore>(fixture.Provider.GetRequiredService<IRuntimeCheckpointCommitStore>());
        Assert.DoesNotContain(writer.ListCommits(), write => write.Commit.Checkpoint.CheckpointId.Contains("flowchart-state", StringComparison.Ordinal));
        var nextChildCommit = Assert.Single(writer.ListCommits(), write => SchedulesChild(write.Commit, "node-b")).Commit;
        var parentChange = Assert.Single(nextChildCommit.StateChanges.ActivityExecutions);
        var serialized = parentChange.State.Metadata[FlowchartExecutionEngine.StateMetadataKey];
        var state = JsonSerializer.Deserialize<FlowchartExecutionState>(serialized, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                    ?? throw new InvalidOperationException("Flowchart execution state resolved to null.");

        var activeChild = Assert.Single(state.ActiveChildren);
        Assert.Equal("node-b", activeChild.NodeId);
        Assert.Contains(state.ExecutionPaths, path => path.CurrentNodeId == "node-b" && path.Status == ExecutionPathStatus.Active);
        Assert.Single(writer.ListCommits().SelectMany(write => write.Commit.PostCommitIntents), intent => SchedulesChild(intent, "node-b"));
    }

    [Fact]
    public async Task Start_ProjectsChildSchedulingProvenanceForInspection()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var projections = await fixture.Provider.GetRequiredService<IActivityExecutionInspectionStore>().ListSummariesAsync("wfexec-1");
        var childProjection = projections.Single(projection => projection.ActivityExecutionId == "actexec-a");
        Assert.Equal(ActivityExecutionStatus.Completed, childProjection.Status);
        Assert.Equal("actexec-flowchart", childProjection.Provenance.ParentActivityExecutionId);
        Assert.Equal("actexec-flowchart", childProjection.Provenance.SchedulingActivityExecutionId);
        Assert.Equal("path:root", childProjection.Provenance.ExecutionPathId);
        Assert.Equal("scope:root", childProjection.Provenance.ExecutionScopeId);
        Assert.Equal("start", childProjection.Provenance.SchedulingCause);
        Assert.Equal("node-a", childProjection.Provenance.Metadata["flowchart.targetNodeId"]);
    }

    [Fact]
    public async Task ExplicitJoinPolicy_UsesEngineJoinEvaluation()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-d"] = new(FlowchartPolicyKinds.ImplicitActivationJoin)
            });

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-d"));
    }

    [Fact]
    public async Task PolicyContinuation_RespectsImplicitJoinBeforeSchedulingTarget()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-b"] = new(FlowchartPolicyKinds.DirectContinuation)
            });

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var flowchartState = await fixture.GetFlowchartStateAsync();
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-d"));
        Assert.Contains(flowchartState.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Waiting && diagnostic.NodeId == "node-d");
    }

    [Fact]
    public async Task PolicyScheduleNode_WithMissingConnectionId_RecordsPolicyFailure()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a"],
            services => services.AddSingleton<IFlowchartPolicy, MissingConnectionPolicy>());
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-c")
            ],
            connections: [fixture.NewConnection("node-a", "node-c")],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(MissingConnectionPolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic =>
            diagnostic.Kind == FlowchartDiagnosticKind.PolicyFailure &&
            diagnostic.NodeId == "node-a" &&
            diagnostic.Message.Contains("unknown connection id 'missing-connection'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PolicyScheduleNode_WithMissingTargetScope_RecordsPolicyFailure()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a"],
            services => services.AddSingleton<IFlowchartPolicy, MissingTargetScopePolicy>());
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-c")
            ],
            connections: [],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(MissingTargetScopePolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic =>
            diagnostic.Kind == FlowchartDiagnosticKind.PolicyFailure &&
            diagnostic.NodeId == "node-a" &&
            diagnostic.Message.Contains("unknown execution scope 'scope-missing'", StringComparison.Ordinal));
    }

    [Fact]
    public async Task PolicyScheduleNode_UsesTargetExecutionScopeIdForScheduling()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a", "actexec-c"],
            services => services.AddSingleton<IFlowchartPolicy, TargetScopePolicy>());
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-c")
            ],
            connections: [],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(TargetScopePolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.Single(states.Where(state => state.Execution.ExecutableNodeId == "node-c"));
    }

    [Fact]
    public async Task MergePolicy_DoesNotWaitForOtherActiveInboundBranches()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d1",
            "actexec-d2"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-c", "node-d")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-d"] = new(FlowchartPolicyKinds.Merge)
            });

        await fixture.ExecuteAsync(executable);

        var states = await fixture.Provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        Assert.Equal(2, states.Count(state => state.Execution.ExecutableNodeId == "node-d"));
    }

    [Fact]
    public async Task FirstWinsPolicy_CancelsLosingSiblingPaths()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(FlowchartPolicyKinds.FirstWins)
            });

        await fixture.ExecuteAsync(executable);

        var flowchartState = await fixture.GetFlowchartStateAsync();
        Assert.Contains(flowchartState.Scopes, scope => scope.Kind == ExecutionScopeKind.Race && scope.Status == ExecutionScopeStatus.Completed);
        // #382 retains Canceled paths in the persisted blob: race losers can receive late child
        // completions after CompleteRaceScope strips their ActiveChildren entries, and the by-id
        // "ignored completion for canceled path" lookup needs the record.
        Assert.Contains(flowchartState.ExecutionPaths, path => path.CurrentNodeId == "node-c" && path.Status == ExecutionPathStatus.Canceled);
        Assert.Contains(flowchartState.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.Canceled && diagnostic.NodeId == "node-c");
    }

    [Fact]
    public async Task FirstWinsWinnerPolicyContinuation_UsesParentScope()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(FlowchartPolicyKinds.FirstWins),
                ["node-b"] = new(FlowchartPolicyKinds.DirectContinuation)
            });

        await fixture.ExecuteAsync(executable);

        var flowchartState = await fixture.GetFlowchartStateAsync();
        // #382: the completed continuation path is pruned from the persisted blob; the durable evidence
        // that node-d ran in the parent (root) scope is its inspection projection's provenance.
        var projections = await fixture.Provider.GetRequiredService<IActivityExecutionInspectionStore>().ListSummariesAsync("wfexec-1");
        var winnerContinuation = projections.Single(projection => projection.ExecutableNodeId == "node-d");
        Assert.Equal(flowchartState.RootExecutionScopeId, winnerContinuation.Provenance.ExecutionScopeId);
    }

    [Fact]
    public async Task NestedFirstWinsPolicy_ParentsInnerRaceToContinuationScope()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync([
            "actexec-flowchart",
            "actexec-a",
            "actexec-b",
            "actexec-c",
            "actexec-d",
            "actexec-e",
            "actexec-f"
        ]);
        var executable = fixture.NewExecutable(
            children:
            [
                fixture.NewProbeNode("node-a"),
                fixture.NewProbeNode("node-b"),
                fixture.NewProbeNode("node-c"),
                fixture.NewProbeNode("node-d"),
                fixture.NewProbeNode("node-e"),
                fixture.NewProbeNode("node-f")
            ],
            connections:
            [
                fixture.NewConnection("node-a", "node-b"),
                fixture.NewConnection("node-a", "node-c"),
                fixture.NewConnection("node-b", "node-d"),
                fixture.NewConnection("node-b", "node-e"),
                fixture.NewConnection("node-d", "node-f")
            ],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(FlowchartPolicyKinds.FirstWins),
                ["node-b"] = new(FlowchartPolicyKinds.FirstWins)
            });

        await fixture.ExecuteAsync(executable);

        var flowchartState = await fixture.GetFlowchartStateAsync();
        var raceScopes = flowchartState.Scopes.Where(scope => scope.Kind == ExecutionScopeKind.Race).ToArray();
        Assert.Equal(2, raceScopes.Length);
        Assert.All(raceScopes, scope => Assert.Equal(flowchartState.RootExecutionScopeId, scope.ParentExecutionScopeId));
        // #382: the completed continuation path is pruned from the persisted blob; the durable evidence
        // that node-f ran in the root scope is its inspection projection's provenance.
        var projections = await fixture.Provider.GetRequiredService<IActivityExecutionInspectionStore>().ListSummariesAsync("wfexec-1");
        var innerContinuation = projections.Single(projection => projection.ExecutableNodeId == "node-f");
        Assert.Equal(flowchartState.RootExecutionScopeId, innerContinuation.Provenance.ExecutionScopeId);
    }

    [Fact]
    public async Task WaitPolicy_RemainsWaitingInsteadOfBeingOverwrittenToCompleted()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a"],
            services => services.AddSingleton<IFlowchartPolicy, WaitPolicy>());
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(WaitPolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.ExecutionPaths, path => path.CurrentNodeId == "node-a" && path.Status == ExecutionPathStatus.Waiting);
    }

    [Fact]
    public void BuiltInForkPolicies_ReturnExpectedScheduleCommands()
    {
        var context = NewPolicyContext(["Done"]);

        Assert.Equal(["node-b"], new DecisionFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b", "node-c"], new ParallelForkFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new InclusiveForkFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new MergeFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
        Assert.Equal(["node-b"], new FirstWinsFlowchartPolicy().Execute(context).Commands.Select(command => command.NodeId));
    }

    [Fact]
    public async Task InvalidPolicyCommand_FaultsFlowchartWithPolicyFailure()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(
            ["actexec-flowchart", "actexec-a"],
            services => services.AddSingleton<IFlowchartPolicy, InvalidPolicy>());
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a",
            nodeMetadata: new Dictionary<string, FlowchartNodeMetadata>
            {
                ["node-a"] = new(InvalidPolicy.Kind)
            });

        await fixture.ExecuteAsync(executable);

        var state = await fixture.GetFlowchartStateAsync();
        Assert.Contains(state.Diagnostics, diagnostic => diagnostic.Kind == FlowchartDiagnosticKind.PolicyFailure && diagnostic.NodeId == "node-a");
    }

    private static IFlowchartPolicyContext NewPolicyContext(IReadOnlyCollection<string> outcomes) =>
        new TestPolicyContext(
            "node-a",
            outcomes,
            [
                new FlowchartConnection(new FlowchartEndpoint("node-a"), new FlowchartEndpoint("node-b")),
                new FlowchartConnection(new FlowchartEndpoint("node-a", "Other"), new FlowchartEndpoint("node-c"))
            ]);

    private static bool SchedulesChild(RuntimeCheckpointCommit commit, string executableNodeId) =>
        commit.PostCommitIntents.Any(intent => SchedulesChild(intent, executableNodeId));

    private static bool SchedulesChild(RuntimePostCommitIntent intent, string executableNodeId)
    {
        if (!StringComparer.Ordinal.Equals(intent.Kind, RuntimePostCommitIntentKinds.EnqueueSchedulerWork) || intent.Payload is null)
            return false;

        var workItem = intent.Payload.Value.Deserialize<RuntimeSchedulerWorkItem>();
        if (workItem?.CommandKind != WorkflowExecutionCommandKind.ScheduleActivity || workItem.Payload is null)
            return false;

        var payload = workItem.Payload.Value.Deserialize<RuntimeScheduleActivityCommandPayload>();
        return StringComparer.Ordinal.Equals(payload?.ExecutableNodeId, executableNodeId);
    }

    private sealed record TestPolicyContext(
        string? CurrentNodeId,
        IReadOnlyCollection<string> OutcomeNames,
        IReadOnlyCollection<FlowchartConnection> Connections) : IFlowchartPolicyContext
    {
        public FlowchartPolicyTrigger Trigger => FlowchartPolicyTrigger.ChildCompleted;
        public FlowchartExecutionState State { get; } = new("scope:root");
    }

    private sealed class InvalidPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/invalid";
        public string PolicyKind => Kind;
        public string DisplayName => "Invalid";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode)]);
    }

    private sealed class MissingConnectionPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/missing-connection";
        public string PolicyKind => Kind;
        public string DisplayName => "Missing Connection";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "node-c", connectionId: "missing-connection")]);
    }

    private sealed class MissingTargetScopePolicy : IFlowchartPolicy
    {
        public const string Kind = "test/missing-target-scope";
        public string PolicyKind => Kind;
        public string DisplayName => "Missing Target Scope";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "node-c", targetExecutionScopeId: "scope-missing")]);
    }

    private sealed class TargetScopePolicy : IFlowchartPolicy
    {
        public const string Kind = "test/target-scope";
        public string PolicyKind => Kind;
        public string DisplayName => "Target Scope";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.ScheduleNode, nodeId: "node-c", executionScopeId: "scope-missing", targetExecutionScopeId: "scope:root")]);
    }

    private sealed class WaitPolicy : IFlowchartPolicy
    {
        public const string Kind = "test/wait";
        public string PolicyKind => Kind;
        public string DisplayName => "Wait";
        public FlowchartPolicyDecision Execute(IFlowchartPolicyContext context) => new([new FlowchartPolicyCommand(FlowchartPolicyCommandKind.WaitExecutionPath)]);
    }

    private sealed class SkipFlowchartInspectionCheckpointPolicy : IRuntimeCheckpointPersistencePolicy
    {
        public ValueTask<RuntimeCheckpointPersistenceDecision> DecideAsync(RuntimeCheckpoint checkpoint, CancellationToken cancellationToken = default) =>
            new(StringComparer.Ordinal.Equals(checkpoint.Name, RuntimeCheckpointNames.ActivityInspectionCaptured)
                ? new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Skip)
                : new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
    }
}
