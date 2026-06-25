using Elsa.Activities.Flowchart.Contracts;
using Elsa.Activities.Flowchart.Exceptions;
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
    public async Task Start_MarksFlowchartStateInspectionCheckpointAsMandatory()
    {
        await using var fixture = await FlowchartRuntimeFixture.CreateAsync(["actexec-flowchart", "actexec-a"]);
        var executable = fixture.NewExecutable(
            children: [fixture.NewProbeNode("node-a")],
            connections: [],
            startNodeId: "node-a");

        await fixture.ExecuteAsync(executable);

        var writer = Assert.IsType<InMemoryRuntimeCheckpointWriter>(fixture.Provider.GetRequiredService<IRuntimeCheckpointWriter>());
        var flowchartStateWrites = writer.ListWrites()
            .Where(write => write.Commit.Checkpoint.Name == RuntimeCheckpointNames.ActivityInspectionCaptured &&
                            write.Commit.Checkpoint.CheckpointId.Contains("flowchart-state", StringComparison.Ordinal))
            .ToArray();
        Assert.NotEmpty(flowchartStateWrites);
        Assert.All(flowchartStateWrites, write => Assert.Equal(RuntimeMetadataKeys.CheckpointRequirementMandatory, write.Commit.Checkpoint.Metadata[RuntimeMetadataKeys.CheckpointRequirement]));
        var committer = new RuntimeCheckpointCommitter(
            new SkipFlowchartInspectionCheckpointPolicy(),
            new InMemoryRuntimeCheckpointWriter(),
            new NoopRuntimePostCommitIntentDispatcher());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => committer.CommitAsync(flowchartStateWrites[0].Commit).AsTask());
        Assert.Contains("Mandatory runtime checkpoint", exception.Message, StringComparison.Ordinal);
        Assert.Contains("flowchart-state", exception.Message, StringComparison.Ordinal);
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
        Assert.Contains(flowchartState.ExecutionPaths, path =>
            path.CurrentNodeId == "node-d" &&
            path.ExecutionScopeId == flowchartState.RootExecutionScopeId &&
            path.Status == ExecutionPathStatus.Completed);
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
        Assert.Contains(flowchartState.ExecutionPaths, path =>
            path.CurrentNodeId == "node-f" &&
            path.ExecutionScopeId == flowchartState.RootExecutionScopeId &&
            path.Status == ExecutionPathStatus.Completed);
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
