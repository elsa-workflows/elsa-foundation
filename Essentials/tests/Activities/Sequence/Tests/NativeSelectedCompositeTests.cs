using System.Text.Json;
using System.Text.Json.Nodes;
using Elsa.Activities.Testing;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Expressions.Core.Contracts;
using Elsa.Expressions.Core.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Values;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Activities.Sequence.Tests;

public sealed class NativeSelectedCompositeTests
{
    [Theory]
    [InlineData(PersistenceMode.Durable, false, false)]
    [InlineData(PersistenceMode.Balanced, false, false)]
    [InlineData(PersistenceMode.Durable, true, false)]
    [InlineData(PersistenceMode.Balanced, true, false)]
    [InlineData(PersistenceMode.Durable, false, true)]
    [InlineData(PersistenceMode.Balanced, false, true)]
    public async Task Trusted_selected_sequence_runs_only_real_owned_work(PersistenceMode mode, bool nested, bool empty)
    {
        var owner = new Owner();
        await using var harness = Harness(owner, mode);
        var children = empty ? [] : new[] { WorkflowExecutionHarness.NewProbeNode("owned-a"), nested
            ? Sequence("inner", [WorkflowExecutionHarness.NewProbeNode("owned-b")]) : WorkflowExecutionHarness.NewProbeNode("owned-b") };
        await Setup(harness, owner, Sequence("selected", children));
        var before = JsonSerializer.Serialize(owner.Executable);
        var dispatched = await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        Assert.True(dispatched.CommandDispatch.Status == WorkflowExecutionCommandDispatchStatus.Accepted, dispatched.CommandDispatch.Reason);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        var activities = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId);
        Assert.Equal(empty ? ["selected"] : nested ? ["inner", "owned-a", "owned-b", "selected"] : ["owned-a", "owned-b", "selected"],
            activities.Select(x => x.Execution.ExecutableNodeId).Order().ToArray());
        var selected = Assert.Single(activities, x => x.Execution.ExecutableNodeId == "selected");
        Assert.Null(selected.ParentActivityExecutionId);
        Assert.Equal(selected.InvocationId, state.TestContext!.RootActivityExecutionId);
        Assert.Equal(activities.Select(x => x.Execution.ExecutableNodeId).Order(), state.TestContext.OwnedNodeIds.Order());
        Assert.All(activities, x => Assert.Equal(ActivityExecutionStatus.Completed, x.Status));
        foreach (var child in activities.Where(x => x != selected))
            Assert.Equal(nested && child.Execution.ExecutableNodeId == "owned-b"
                ? Assert.Single(activities, x => x.Execution.ExecutableNodeId == "inner").InvocationId : selected.InvocationId, child.ParentActivityExecutionId);
        Assert.Equal(before, JsonSerializer.Serialize(await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(owner.Executable!.Identity.ArtifactId)));
        Assert.Contains("selected", owner.QualifiedNodes);
        if (!empty) Assert.Contains("owned-b", owner.QualifiedNodes);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Every_owned_node_requires_explicit_qualification_before_any_execution(bool unsupportedChild)
    {
        var owner = new Owner { QualifyTree = unsupportedChild };
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [unsupportedChild ? WorkflowExecutionHarness.NewFaultingNode("owned") : WorkflowExecutionHarness.NewProbeNode("owned")]));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () => await harness.Services.GetRequiredService<IWorkflowStartDispatcher>()
            .DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_child_cannot_inherit_context_from_a_duplicate_or_missing_selected_parent(bool missing)
    {
        var owner = new Owner();
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("owned")]));
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var store = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        await store.SaveAsync(owner.RunningHeader!);
        var activityStore = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var rows = await activityStore.ListAllAsync(harness.ExecutionId);
        var child = Assert.Single(rows, x => x.Execution.ExecutableNodeId == "owned");
        var frames = new RuntimeContainerScopeService(activityStore, store);
        Assert.NotEmpty((await frames.BuildVisibleFramesAsync(harness.ExecutionId, child)).Frames);
        if (!missing)
        {
            var selected = Assert.Single(rows, x => x.Execution.ExecutableNodeId == "selected");
            await activityStore.SaveAsync(selected with { Execution = selected.Execution with { ActivityExecutionId = "duplicate-root" } });
        }
        var forged = child with { ParentActivityExecutionId = missing ? "missing-root" : "duplicate-root" };
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await frames.BuildVisibleFramesAsync(harness.ExecutionId, forged));
    }

    [Theory]
    [InlineData("target")]
    [InlineData("missing-parent")]
    [InlineData("duplicate-root")]
    [InlineData("scheduler")]
    [InlineData("scope")]
    [InlineData("metadata-scope")]
    [InlineData("root-id")]
    [InlineData("terminal-parent")]
    public async Task Invalid_owned_child_schedules_cannot_change_header_invocations_or_queued_work(string defect)
    {
        var owner = new Owner();
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("owned")]));
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var workflows = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var activities = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        await workflows.SaveAsync(owner.RunningHeader!);
        var parent = owner.ActiveRoot!;
        if (defect == "terminal-parent") parent = parent with { Status = ActivityExecutionStatus.Completed };
        if (defect == "duplicate-root") parent = parent with { Execution = parent.Execution with { ActivityExecutionId = "duplicate-root" } };
        await activities.SaveAsync(parent);
        var parentId = defect == "missing-parent" ? "missing" : parent.InvocationId;
        var schedulerId = defect == "scheduler" ? "missing" : parent.InvocationId;
        var scope = defect == "scope" ? "foreign-scope" : null;
        var payload = new RuntimeScheduleActivityCommandPayload(owner.Executable!.Identity, defect == "target" ? "before" : "owned",
            defect == "root-id" ? owner.RunningHeader!.TestContext!.RootActivityExecutionId! : "new-child",
            RuntimeScheduleActivityCommandPayload.ActivityCompletionReason, schedulerId, parentId,
            ActivitySchedulingProvenance.From(harness.ExecutionId, parentId, schedulerId, null, null, null, scope,
                RuntimeScheduleActivityCommandPayload.ActivityCompletionReason,
                defect == "metadata-scope" ? new Dictionary<string, string> { [RuntimeMetadataKeys.FlowchartExecutionScopeId] = "foreign-scope" } : null));
        var work = new RuntimeSchedulerWorkItem("forged-child", harness.ExecutionId, "forged-command", WorkflowExecutionCommandKind.ScheduleActivity,
            "forged-envelope", "forged-key", owner.Clock.Now, owner.Clock.Now, payload: JsonSerializer.SerializeToElement(payload), executionScopeId: scope);
        var queue = harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var beforeHeader = JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId));
        var beforeActivities = JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId));
        var beforeQueue = JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>()
            .OfType<WorkflowScheduleActivitySchedulerWorkHandler>()).HandleAsync(work));
        Assert.Equal(beforeHeader, JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId)));
        Assert.Equal(beforeActivities, JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId)));
        Assert.Equal(beforeQueue, JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId))));
    }

    [Fact]
    public async Task A_fixture_cannot_replace_an_owned_child_result()
    {
        var owner = new Owner { Fixtures = [new("owned", "scope", ValueEnvelope.Inline(new("Object"),
            JsonSerializer.SerializeToElement("fake"), ValueProtectionPolicy.InstanceInline))] };
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("owned")]));
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(async () => await harness.Services.GetRequiredService<IWorkflowStartDispatcher>()
            .DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun));
        Assert.Null(await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId));
        Assert.Empty(await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Owned_child_consumes_an_outside_fixture_through_its_actual_parent_chain(bool expression)
    {
        var probe = WorkflowExecutionHarness.NewProbeNode("owned");
        var declaration = probe.InputBindings[nameof(ProbeActivity.Outcomes)];
        var result = ValueEnvelope.Inline(declaration.TargetType, JsonSerializer.SerializeToElement(new[] { ActivityOutcomes.Done }), ValueProtectionPolicy.InstanceInline);
        var owner = new Owner { Fixtures = [new("before", "outside", result)] };
        owner.Before = new("before", "before", "fixture.producer", "1", "fixture.producer", JsonSerializer.SerializeToElement(new { }),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(), activityContract: new("fixture.producer", "1", "fixture.producer",
                JsonSerializer.SerializeToElement(new { }), [], new(declaration.TargetType, true, ActivityValuePolicy.Default, []), [ActivityOutcomes.Done], new("fixture.producer", "probe")));
        var binding = new RuntimeInputBinding(declaration.InputKey, declaration.TargetType, declaration.EffectivePolicy,
            expression ? RuntimeInputBindingSource.Expression : RuntimeInputBindingSource.ActivityResult,
            activityResult: expression ? null : new("before", "$result", "outside"),
            expression: expression ? new("fixture-expression", "p", parameters: new Dictionary<string, ExpressionParameterBinding>
                { ["p"] = new ActivityResultExpressionParameterBinding("before", "$result", false) }) : null);
        var child = new ExecutableNode(probe.ExecutableNodeId, probe.AuthoredActivityId, probe.ActivityType, probe.ActivityTypeVersion,
            probe.DescriptorType, probe.DescriptorPayload, new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding }, probe.Metadata,
            activityContract: probe.ActivityContract);
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [Sequence("inner", [child])]));
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var state = (await harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(harness.ExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        var rows = await harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(harness.ExecutionId);
        Assert.Equal(new[] { "inner", "owned", "selected" }, rows.Select(x => x.Execution.ExecutableNodeId).Order());
        var actual = Assert.Single(rows, x => x.Execution.ExecutableNodeId == "owned");
        Assert.Equal(Assert.Single(rows, x => x.Execution.ExecutableNodeId == "inner").InvocationId, actual.ParentActivityExecutionId);
        Assert.Equal(ActivityOutcomes.Done, actual.InputSnapshot!.Values[nameof(ProbeActivity.Outcomes)].InlineValue!.Value[0].GetString());
        Assert.Equal("before", Assert.Single(state.TestContext!.Results).ProducerNodeId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Exact_settled_child_schedule_replay_reconciles_after_parent_or_workflow_completion(bool terminalWorkflow)
    {
        var owner = new Owner();
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [Sequence("inner", [WorkflowExecutionHarness.NewProbeNode("owned")])]));
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var workflows = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var activities = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        var queue = harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        Assert.Equal(WorkflowExecutionStatus.Completed, (await workflows.FindAsync(harness.ExecutionId))!.Status);
        if (!terminalWorkflow) await workflows.SaveAsync(owner.RunningHeader!);
        var beforeHeader = JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId));
        var beforeActivities = JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId));
        var beforeQueue = JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        var handler = Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>().OfType<WorkflowScheduleActivitySchedulerWorkHandler>());
        Assert.NotEmpty(owner.ChildSchedules);
        foreach (var work in owner.ChildSchedules) await handler.HandleAsync(work);
        Assert.Equal(beforeHeader, JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId)));
        Assert.Equal(beforeActivities, JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId)));
        Assert.Equal(beforeQueue, JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId))));
    }

    [Theory]
    [InlineData("reason")]
    [InlineData("scope")]
    [InlineData("parent")]
    [InlineData("receipt")]
    public async Task A_settled_child_cannot_reconcile_changed_schedule_content_or_a_missing_receipt(string defect)
    {
        var owner = new Owner();
        await using var harness = Harness(owner, PersistenceMode.Balanced);
        await Setup(harness, owner, Sequence("selected", [WorkflowExecutionHarness.NewProbeNode("owned")]));
        await harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(Request(harness, owner), WorkflowExecutableReferenceScope.TestRun);
        var original = Assert.Single(owner.ChildSchedules);
        var json = JsonNode.Parse(original.Payload!.Value.GetRawText())!;
        if (defect == "reason") json["Reason"] = "changed";
        if (defect == "parent") json["ParentActivityExecutionId"] = "foreign";
        if (defect == "scope") json["SchedulingProvenance"]!["Metadata"]![RuntimeMetadataKeys.FlowchartExecutionScopeId] = "foreign";
        var activities = harness.Services.GetRequiredService<IActivityExecutionStateStore>();
        if (defect == "receipt")
        {
            var child = Assert.Single(await activities.ListAllAsync(harness.ExecutionId), x => x.Execution.ExecutableNodeId == "owned");
            await activities.SaveAsync(child with { Metadata = new Dictionary<string, string>() });
        }
        var work = new RuntimeSchedulerWorkItem(original.WorkItemId, original.WorkflowExecutionId, original.CommandId, original.CommandKind,
            original.EnvelopeId, original.IdempotencyKey, original.EnqueuedAt, original.RecordedAt, original.Sequence, JsonSerializer.SerializeToElement(json),
            original.CommandMetadata, original.EnvelopeMetadata, original.ExecutionScopeId, original.Attempt);
        var workflows = harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var queue = harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var beforeHeader = JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId));
        var beforeActivities = JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId));
        var beforeQueue = JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId)));
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await Assert.Single(harness.Services.GetServices<IWorkflowSchedulerWorkHandler>()
            .OfType<WorkflowScheduleActivitySchedulerWorkHandler>()).HandleAsync(work));
        Assert.Equal(beforeHeader, JsonSerializer.Serialize(await workflows.FindAsync(harness.ExecutionId)));
        Assert.Equal(beforeActivities, JsonSerializer.Serialize(await activities.ListAllAsync(harness.ExecutionId)));
        Assert.Equal(beforeQueue, JsonSerializer.Serialize(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(harness.ExecutionId))));
    }

    private static WorkflowExecutionHarness Harness(Owner owner, PersistenceMode mode) => WorkflowExecutionHarness.Create()
        .WithPersistenceMode(mode).WithFeature(s => new ActivitiesSequenceFeature().ConfigureServices(s)).WithProbeLeaf()
        .ConfigureServices(s =>
        {
            s.AddSingleton<TimeProvider>(owner.Clock);
            s.AddSingleton<IPortableExpressionEvaluator, FixtureEvaluator>();
            s.AddSingleton<IWorkflowTestEntryContextResolver>(owner);
            s.AddSingleton<IRuntimeTestContextSeedSource>(owner);
            s.AddSingleton<IRuntimeTestPersistenceBoundary>(owner);
            s.AddSingleton<IRuntimeTestExecutionRoutingBoundary>(owner);
            s.AddSingleton<IRuntimeSelectedStartTargetProfile>(owner);
            s.AddSingleton<IRuntimeCheckpointCommitEnricher>(owner);
        }).Build(Enumerable.Range(1, 30).Select(x => "activity-" + x));

    private static ExecutableNode Sequence(string id, ExecutableNode[] children) => new(id, id, typeof(SequenceActivity).FullName!, "1", "test.sequence",
        JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
        childSlots: [new(SequenceActivity.ActivitiesSlotName, children)], structure: new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
            JsonSerializer.SerializeToElement(new { activities = children.Select(x => x.ExecutableNodeId), variables = Array.Empty<RuntimeVariableDeclaration>() }, new JsonSerializerOptions(JsonSerializerDefaults.Web))));

    private static async Task Setup(WorkflowExecutionHarness harness, Owner owner, ExecutableNode selected)
    {
        var executable = WorkflowExecutionHarness.NewExecutable(Sequence("outer", [owner.Before ?? WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after")]));
        var publication = await harness.PublishAsync(executable, "ordinary-source");
        owner.Executable = (await harness.Services.GetRequiredService<IWorkflowExecutableStore>().FindAsync(executable.Identity.ArtifactId))!;
        owner.Source = publication with { SourceReferenceId = "test-source", Scope = WorkflowExecutableReferenceScope.TestRun,
            ActivationId = null, SlotId = null, ExpiresAt = owner.Clock.Now.AddMinutes(30) };
        await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(owner.Source);
        var scope = new WorkflowTestScope("test-scope", owner.Clock.Now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
        owner.Entry = new("context", "fingerprint", owner.Executable.Identity, "selected", scope, new("tester", "tester"));
        await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, owner.Clock.Now);
    }

    private static WorkflowExecutionStartDispatchRequest Request(WorkflowExecutionHarness harness, Owner owner) => new(
        owner.Executable!.Identity.ArtifactId, "tester", harness.ExecutionId, null, null, null, null, null, null, WorkflowRunKind.TestRun,
        new WorkflowExecutableSourceSelection(owner.Source!.SourceReferenceId), WorkflowExecutableProvenanceRequirement.RequireLiveReference,
        null, null, null, owner.Entry!.Scope.Partition, owner.Entry.Authority, WorkflowExecutableStartAuthority.LiveReference, 0, owner.Entry.Scope) { TestEntry = owner.Entry };

    private sealed class FixtureEvaluator : IPortableExpressionEvaluator, IPortableExpressionCapabilities
    {
        public bool CanEvaluate(ExpressionDefinition definition) => definition.Language == "fixture-expression" && definition.Source == "p";
        public ValueTask<JsonElement> EvaluateAsync(ExpressionEvaluationRequest request) => ValueTask.FromResult(request.ParameterValues["p"].Clone());
    }

    private sealed class Clock : TimeProvider
    {
        internal DateTimeOffset Now { get; } = new(2026, 11, 14, 12, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Owner : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource, IRuntimeTestPersistenceBoundary,
        IRuntimeTestExecutionRoutingBoundary, IRuntimeSelectedStartTargetProfile, IRuntimeCheckpointCommitEnricher
    {
        internal Clock Clock { get; } = new();
        internal WorkflowExecutable? Executable;
        internal ExecutableNode? Before;
        internal WorkflowExecutableSourceReference? Source;
        internal WorkflowTestEntry? Entry;
        internal WorkflowExecutionState? RunningHeader;
        internal ActivityExecutionState? ActiveRoot;
        internal bool QualifyTree = true;
        internal RuntimeTestResultFixture[] Fixtures = [];
        internal List<string> QualifiedNodes { get; } = [];
        internal List<RuntimeSchedulerWorkItem> ChildSchedules { get; } = [];
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) => ValueTask.FromResult(Entry);
        public ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable, string workflowExecutionId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeTestContextSeed?>(new(entry.ContextId, entry.ContextFingerprint, [], Fixtures));
        public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context) => true;
        public ValueTask<bool> CanRouteTestEntryAsync(WorkflowTestEntry entry, IWorkflowExecutionActorProvider actors, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanExecuteAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode target,
            IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default) => ValueTask.FromResult(true);
        public ValueTask<bool> CanExecuteOwnedNodeAsync(WorkflowTestEntry entry, WorkflowExecutable executable, ExecutableNode selectedRoot, ExecutableNode ownedNode,
            IRuntimeActivityExecutionPipeline pipeline, CancellationToken cancellationToken = default)
        {
            QualifiedNodes.Add(ownedNode.ExecutableNodeId);
            return ValueTask.FromResult(QualifyTree && ownedNode.InputBindings.Values.All(x => x.Source == RuntimeInputBindingSource.Literal && x.Literal is { Presence: ValuePresence.Present } ||
                    Fixtures.Count(f => f.ProducerNodeId == "before") == 1 &&
                    Fixtures.Any(f => f.ProducerNodeId == "before" && f.ProducerScopeId == "outside" && f.Result.Type == x.TargetType) &&
                    (x.ActivityResult is { ProducerExecutableNodeId: "before", ProjectionKey: "$result", ProducerScopeId: "outside" } ||
                        x.Expression is { Language: "fixture-expression", Expression: "p" } expression && expression.Parameters.Count == 1 &&
                        expression.Parameters.TryGetValue("p", out var parameter) && parameter is ActivityResultExpressionParameterBinding { ProducerNodeId: "before", ProjectionKey: "$result" })) &&
                ownedNode.Metadata.Count == 0 && ownedNode.OutputCaptures.Count == 0 &&
                (ownedNode.ActivityType == typeof(SequenceActivity).FullName || ownedNode.ActivityType == ProbeActivity.ProbeActivityType || ownedNode.ActivityType == typeof(ProbeActivity).FullName));
        }
        public ValueTask<RuntimeCheckpointCommit> EnrichAsync(RuntimeCheckpointCommit commit, CancellationToken cancellationToken = default)
        {
            if (commit.Checkpoint.Name == RuntimeCheckpointNames.WorkflowStarted) RunningHeader = commit.StateChanges.WorkflowExecution!.State;
            foreach (var intent in commit.PostCommitIntents.Where(x => x.Kind == RuntimePostCommitIntentKinds.EnqueueSchedulerWork))
            {
                var work = intent.MaterializedSchedulerWorkItem ?? intent.Payload?.Deserialize<RuntimeSchedulerWorkItem>();
                if (work?.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity &&
                    work.Payload?.Deserialize<RuntimeScheduleActivityCommandPayload>()?.ParentActivityExecutionId is not null)
                    ChildSchedules.Add(work);
            }
            foreach (var change in commit.StateChanges.ActivityExecutions)
                if (change.State.Execution.ExecutableNodeId == "selected" && change.State.Status is ActivityExecutionStatus.Running or ActivityExecutionStatus.Waiting)
                    ActiveRoot = change.State;
            return ValueTask.FromResult(commit);
        }
    }
}
