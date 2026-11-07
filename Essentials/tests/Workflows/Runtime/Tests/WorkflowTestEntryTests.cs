using System.Text.Json;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Sequence;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.Executions;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.WorkHandlers;
using Elsa.Workflows.Runtime.Services.Bookmarks;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Scheduler;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class WorkflowTestEntryTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_ambiguous_physical_boundary_refuses_before_any_queue_or_activity(int count)
    {
        await using var fixture = await Fixture.CreateAsync(configure: services =>
        {
            services.RemoveAll<IRuntimeTestPersistenceBoundary>();
            for (var index = 0; index < count; index++) services.AddScoped<IRuntimeTestPersistenceBoundary, UnqualifiedBoundary>();
        });
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync());
        Assert.Equal("test-entry-provider-unavailable", error.ReasonCode);
        Assert.Empty(await fixture.ActivitiesAsync());
        Assert.Empty(await fixture.Harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAllAsync(new RuntimeSchedulerWorkQuery(fixture.Harness.ExecutionId)));
    }

    [Fact]
    public async Task Actual_memory_boundary_rejects_alternate_same_type_queue_and_timer_instances()
    {
        await using var fixture = await Fixture.CreateAsync();
        var services = fixture.Harness.Services;
        T Physical<T>() where T : class => services.GetService<CoalescingInner<T>>()?.Value ?? services.GetRequiredService<T>();
        var context = new RuntimeTestPersistenceBoundaryContext(fixture.Entry, Physical<IWorkflowExecutionStateStore>(),
            Physical<IWorkflowSchedulerWorkQueue>(), Physical<IRuntimeCheckpointCommitStore>(), Physical<IRuntimePostCommitOutboxStore>(),
            services.GetRequiredService<IBackgroundActivityWorkStore>(), services.GetRequiredService<IDurableTimerStore>());
        var boundary = services.GetRequiredService<IRuntimeTestPersistenceBoundary>();
        Assert.True(boundary.CanPersistTestEntry(context));
        Assert.False(boundary.CanPersistTestEntry(context with { Scheduler = new InMemoryWorkflowSchedulerWorkQueue() }));
        Assert.False(boundary.CanPersistTestEntry(context with { Timers = new InMemoryDurableTimerStore() }));
    }

    [Fact]
    public async Task Same_actor_type_with_an_unqualified_command_executor_cannot_receive_test_start()
    {
        var executor = new UnqualifiedExecutor();
        await using var fixture = await Fixture.CreateAsync(configure: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(new InProcessWorkflowExecutionActorProvider(executor))));
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync());
        Assert.Equal("test-entry-provider-unavailable", error.ReasonCode);
        Assert.Equal(0, executor.Calls);
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    private sealed class UnqualifiedBoundary : IRuntimeTestPersistenceBoundary
    {
        public bool CanPersistTestEntry(RuntimeTestPersistenceBoundaryContext context) => true;
    }

    private sealed class UnqualifiedExecutor : IWorkflowExecutionCommandExecutor
    {
        public int Calls { get; private set; }
        public ValueTask<WorkflowExecutionCommandProcessResult> ProcessAsync(WorkflowExecutionCommandEnvelope envelope, CancellationToken cancellationToken = default)
        {
            Calls++;
            return ValueTask.FromResult(WorkflowExecutionCommandProcessResult.NoDrain);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task Startup_accepts_optional_single_provider_and_rejects_duplicate_composition(int providers)
    {
        var services = new ServiceCollection();
        services.AddWorkflowRuntime();
        for (var index = 0; index < providers; index++) services.AddSingleton<IWorkflowTestEntryContextResolver>(new ContextResolver());
        await using var provider = services.BuildServiceProvider();
        async Task StartAsync()
        {
            foreach (var task in provider.GetServices<IStartupTask>()) await task.ExecuteAsync(CancellationToken.None);
        }
        if (providers > 1)
            Assert.Contains("at most one trusted test-entry", (await Assert.ThrowsAsync<InvalidOperationException>(StartAsync)).Message);
        else
            await StartAsync();
    }

    [Fact]
    public async Task Admitted_entry_runs_only_selected_leaf_on_unchanged_full_artifact()
    {
        await using var fixture = await Fixture.CreateAsync();
        var before = JsonSerializer.Serialize(await fixture.Material.FindAsync(fixture.Entry.Executable.ArtifactId));
        await fixture.StartAsync();
        var state = await fixture.ExecutionAsync();
        var activity = Assert.Single(await fixture.ActivitiesAsync());
        Assert.Equal("selected", activity.Execution.ExecutableNodeId);
        Assert.Null(activity.ParentActivityExecutionId);
        Assert.Equal(ActivityExecutionStatus.Completed, activity.Status);
        Assert.Equal(WorkflowExecutionStatus.Completed, state.Status);
        Assert.True(fixture.Entry.Matches(state.TestEntry));
        Assert.Equal(before, JsonSerializer.Serialize(await fixture.Material.FindAsync(fixture.Entry.Executable.ArtifactId)));
    }

    [Fact]
    public async Task Ordinary_start_keeps_root_and_trigger_semantics()
    {
        await using var fixture = await Fixture.CreateAsync();
        var run = await fixture.Harness.RunAsync(fixture.Candidate, JsonSerializer.SerializeToElement(new { }), "selected");
        run.AssertWorkflowCompleted();
        run.AssertRan("container", "before", "selected", "after");
        Assert.Null(run.WorkflowState!.TestEntry);
    }

    [Theory]
    [InlineData("fingerprint")]
    [InlineData("context")]
    [InlineData("node")]
    [InlineData("missing-node")]
    [InlineData("scope")]
    [InlineData("hash")]
    [InlineData("authority")]
    public async Task Descriptor_mismatch_refuses_before_any_activity(string change)
    {
        await using var fixture = await Fixture.CreateAsync();
        var original = fixture.Entry;
        var requested = new WorkflowTestEntry(change == "context" ? "different" : original.ContextId,
            change == "fingerprint" ? "different" : original.ContextFingerprint,
            change == "hash" ? original.Executable with { ArtifactHash = "different" } : original.Executable,
            change == "node" ? "before" : change == "missing-node" ? "missing" : original.ExecutableNodeId,
            change == "scope" ? new("other-scope", original.Scope.ExpiresAt, original.Scope.TenantId, original.Scope.Partition) : original.Scope,
            change == "authority" ? new("different", "different") : original.Authority);
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync(requested));
        Assert.Empty(await fixture.ActivitiesAsync());
        Assert.Null(await fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(fixture.Harness.ExecutionId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task Missing_or_duplicate_context_provider_refuses(int providers)
    {
        await using var fixture = await Fixture.CreateAsync(providers: providers);
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync());
        Assert.Equal("test-entry-unavailable", error.ReasonCode);
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Published_start_cannot_enable_selected_entry()
    {
        await using var fixture = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync(requiredScope: WorkflowExecutableReferenceScope.Published));
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Closed_scope_refuses_without_activity_execution()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.Harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CloseAsync(
            new(fixture.Entry.Scope.ScopeId, WorkflowTestScopeCloseReason.ExplicitTeardown, TimeProvider.System.GetUtcNow()));
        await Assert.ThrowsAnyAsync<Exception>(() => fixture.StartAsync());
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Changed_entry_cannot_replace_existing_execution_on_duplicate()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync();
        var original = await fixture.ExecutionAsync();
        var changed = new WorkflowTestEntry(fixture.Entry.ContextId, "changed", fixture.Entry.Executable,
            fixture.Entry.ExecutableNodeId, fixture.Entry.Scope, fixture.Entry.Authority);
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => fixture.StartAsync(changed));
        Assert.Equal("test-entry-duplicate-mismatch", error.ReasonCode);
        Assert.True(original.TestEntry!.Matches((await fixture.ExecutionAsync()).TestEntry));
        Assert.Single(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Exact_keyed_duplicate_preserves_entry_when_partition_is_implicit()
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync(omitPartition: true);
        await fixture.StartAsync(omitPartition: true);
        Assert.Single(await fixture.ActivitiesAsync());
        Assert.True(fixture.Entry.Matches((await fixture.ExecutionAsync()).TestEntry));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Queued_start_revalidates_context_and_source_before_scheduling(bool retireSource)
    {
        await using var fixture = await Fixture.CreateAsync();
        var queue = fixture.Harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var handler = new WorkflowStartSchedulerWorkHandler(fixture.Material, queue,
            fixture.Harness.Services.GetRequiredService<IRuntimeExecutionIdGenerator>(), TimeProvider.System,
            fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>(),
            fixture.Harness.Services, [fixture.Resolver], fixture.Harness.Services.GetRequiredService<IWorkflowTestScopeStore>(),
            fixture.Harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>());
        if (retireSource)
            await fixture.Harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
                .RetireAsync(fixture.Reference.SourceReferenceId, TimeProvider.System.GetUtcNow(), "test-retirement");
        else
            fixture.Resolver.Entry = null;
        var payload = new WorkflowExecutionStartCommandPayload(fixture.Entry.Executable, fixture.Entry.Executable.ArtifactId,
            null, null, null, null, WorkflowRunKind.TestRun, WorkflowExecutableSourceProvenance.From(fixture.Reference),
            null, null, null, fixture.Entry.Scope.Partition, fixture.Entry.Authority, null, 0, fixture.Entry.Scope)
            { TestEntry = fixture.Entry };
        var work = new RuntimeSchedulerWorkItem("queued", fixture.Harness.ExecutionId, "command", WorkflowExecutionCommandKind.Start,
            "envelope", "idempotency", TimeProvider.System.GetUtcNow(), TimeProvider.System.GetUtcNow(),
            payload: JsonSerializer.SerializeToElement(payload));
        var error = await Assert.ThrowsAsync<WorkflowExecutableStartRejectedException>(() => handler.HandleAsync(work).AsTask());
        Assert.Equal(retireSource ? "test-entry-source-required" : "test-entry-not-admitted", error.ReasonCode);
        Assert.Empty(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(fixture.Harness.ExecutionId)));
        Assert.Empty(await fixture.ActivitiesAsync());
    }

    [Fact]
    public async Task Fault_and_operator_cancel_preserve_entry_and_do_not_start_containing_graph()
    {
        await using (var fault = await Fixture.CreateAsync(selectedKind: "fault"))
        {
            await fault.StartAsync();
            Assert.Equal(WorkflowExecutionStatus.Faulted, (await fault.ExecutionAsync()).Status);
            Assert.True(fault.Entry.Matches((await fault.ExecutionAsync()).TestEntry));
            Assert.Equal("selected", Assert.Single(await fault.ActivitiesAsync()).Execution.ExecutableNodeId);
        }
        await using var waiting = await Fixture.CreateAsync(selectedKind: "waiting");
        await waiting.StartAsync();
        Assert.False((await waiting.ExecutionAsync()).Status.IsTerminal(),
            string.Join("; ", (await waiting.ActivitiesAsync()).Select(activity => activity.Fault?.Message)));
        await waiting.Harness.CancelAsync(waiting.Harness.ExecutionId, waiting.Entry.Scope.Partition);
        Assert.Equal(WorkflowExecutionStatus.Cancelled, (await waiting.ExecutionAsync()).Status);
        Assert.True(waiting.Entry.Matches((await waiting.ExecutionAsync()).TestEntry));
        Assert.Equal("selected", Assert.Single(await waiting.ActivitiesAsync()).Execution.ExecutableNodeId);
    }

    [Theory]
    [InlineData(true, RuntimeCheckpointNames.WorkflowCompleted)]
    [InlineData(false, RuntimeCheckpointNames.ActivityCompleted)]
    public async Task Zero_outcome_completes_only_admitted_selected_boundary(bool selectedEntry, string expectedCheckpoint)
    {
        await using var fixture = await Fixture.CreateAsync();
        await fixture.StartAsync();
        var store = fixture.Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>();
        var completed = await fixture.ExecutionAsync();
        await store.SaveAsync(completed with { Status = WorkflowExecutionStatus.Running, CompletedAt = null,
            TestEntry = selectedEntry ? completed.TestEntry : null });
        var queue = fixture.Harness.Services.GetRequiredService<IWorkflowSchedulerWorkQueue>();
        var target = Assert.Single(await fixture.ActivitiesAsync());
        var handler = new WorkflowCompleteActivitySchedulerWorkHandler(
            fixture.Harness.Services.GetRequiredService<IActivityExecutionStateStore>(), queue,
            fixture.Material, TimeProvider.System, workflowExecutionStateStore: store);
        var payload = new RuntimeCompleteActivityCommandPayload(fixture.Entry.Executable, "selected", target.InvocationId,
            null, null, [], "probe-zero-outcome", SchedulerCompletionKind.ContinuationScheduling);
        await handler.HandleAsync(new("zero-outcome", fixture.Harness.ExecutionId, "zero-command", WorkflowExecutionCommandKind.CompleteActivity,
            "zero-envelope", "zero-key", TimeProvider.System.GetUtcNow(), TimeProvider.System.GetUtcNow(), payload: JsonSerializer.SerializeToElement(payload)));
        var checkpoint = Assert.Single(await queue.ListAllAsync(new RuntimeSchedulerWorkQuery(fixture.Harness.ExecutionId)));
        Assert.Equal(expectedCheckpoint, checkpoint.Payload!.Value.Deserialize<RuntimeCheckpointCommandPayload>()!.CheckpointName);
    }

    private sealed class ContextResolver : IWorkflowTestEntryContextResolver, IRuntimeTestContextSeedSource
    {
        public WorkflowTestEntry? Entry { get; set; }
        public ValueTask<WorkflowTestEntry?> FindAsync(string contextId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(Entry?.ContextId == contextId ? Entry : null);
        public ValueTask<RuntimeTestContextSeed?> ResolveAsync(WorkflowTestEntry entry, WorkflowExecutable executable,
            string workflowExecutionId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimeTestContextSeed?>(Entry?.Matches(entry) == true
                ? new(entry.ContextId, entry.ContextFingerprint, [], []) : null);
    }

    public sealed class WaitingActivity : StatefulActivity<ActivityUnit, int, int>
    {
        protected override ValueTask<ActivityTransition<ActivityUnit, int>> ExecuteAsync(ActivityExecutionContext context) =>
            ValueTask.FromResult(Suspend(1, [new ActivityTriggerRegistration<int>("wait", "test.wait", "selected-wait")]));
        [ResumeTarget("wait")]
        protected override ValueTask<ActivityTransition<ActivityUnit, int>> ResumeAsync(ActivityResumeContext<int, int> context) =>
            ValueTask.FromResult(Complete(ActivityUnit.Value));
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public required WorkflowExecutionHarness Harness { get; init; }
        public required WorkflowExecutable Candidate { get; init; }
        public required WorkflowExecutableSourceReference Reference { get; init; }
        public required WorkflowTestEntry Entry { get; init; }
        public required ContextResolver Resolver { get; init; }
        public IWorkflowExecutableStore Material => Harness.Services.GetRequiredService<IWorkflowExecutableStore>();
        public ValueTask DisposeAsync() => Harness.DisposeAsync();
        public async Task<WorkflowExecutionState> ExecutionAsync() =>
            (await Harness.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(Harness.ExecutionId))!;
        public async Task<IReadOnlyCollection<ActivityExecutionState>> ActivitiesAsync() =>
            await Harness.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(Harness.ExecutionId);

        public static async Task<Fixture> CreateAsync(int providers = 1, string selectedKind = "literal", Action<IServiceCollection>? configure = null)
        {
            var resolver = new ContextResolver();
            var harness = WorkflowExecutionHarness.Create()
                .WithFeature(services => new ActivitiesSequenceFeature().ConfigureServices(services))
                .ConfigureServices(services => {
                    for (var index = 0; index < providers; index++) services.AddSingleton<IWorkflowTestEntryContextResolver>(resolver);
                    services.AddSingleton<IRuntimeTestContextSeedSource>(resolver);
                    services.AddSingleton<IRuntimeTestEffectPolicySource, TestOnlyPureEffectPolicy>();
                    configure?.Invoke(services);
                }).Build(WorkflowExecutionHarness.Identity,
                    KeyedWorkflowStartIdentity.For("entry-key", WorkflowExecutionHarness.Identity.ArtifactId).WorkflowExecutionId,
                    Enumerable.Range(1, 40).Select(index => $"activity-{index}"));
            harness.Services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(WaitingActivity), TypeAliasConvention.CanonicalAlias(typeof(WaitingActivity)));
            var selected = selectedKind == "fault" ? WorkflowExecutionHarness.NewFaultingNode("selected") :
                selectedKind == "waiting" ? new ExecutableNode("selected", "selected", typeof(WaitingActivity).FullName!, "1",
                    "test.wait", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()) :
                    WorkflowExecutionHarness.NewProbeNode("selected");
            var children = new[] { WorkflowExecutionHarness.NewProbeNode("before"), selected, WorkflowExecutionHarness.NewProbeNode("after") };
            var root = new ExecutableNode("container", "container", typeof(SequenceActivity).FullName!, "1",
                "test.sequence", JsonSerializer.SerializeToElement(new { }), new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>(),
                childSlots: [new(SequenceActivity.ActivitiesSlotName, children)],
                structure: new(SequenceActivity.StructureKind, SequenceActivity.StructureSchemaVersion,
                    JsonSerializer.SerializeToElement(new { activities = children.Select(node => node.ExecutableNodeId).ToArray() })));
            var candidate = WorkflowExecutionHarness.NewExecutable(root);
            if (selectedKind == "waiting")
                candidate = new WorkflowExecutable(candidate.Identity, root,
                    new Dictionary<string, WorkflowExecutableResumeTarget> {
                        ["selected:wait"] = new("selected:wait", "selected", "ResumeAsync", new Dictionary<string, string>(), "wait")
                    }, candidate.CreatedAt, candidate.CompatibilityMetadata, candidate.IncidentStrategy);
            var published = await harness.PublishAsync(candidate, "published-source");
            var now = TimeProvider.System.GetUtcNow();
            var scope = new WorkflowTestScope("test-entry-scope", now.AddMinutes(10), null, new(WorkflowExecutionPartition.DefaultValue));
            var reference = published with { SourceReferenceId = "test-source", Scope = WorkflowExecutableReferenceScope.TestRun,
                ExpiresAt = scope.ExpiresAt, PublishedAt = null, ActivationId = null, SlotId = null };
            await harness.Services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
            await harness.Services.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(scope, now);
            var entry = new WorkflowTestEntry("context-1", "context-fingerprint", candidate.Identity, "selected", scope, new("tests", "tests"));
            resolver.Entry = entry;
            return new() { Harness = harness, Candidate = candidate, Reference = reference, Entry = entry, Resolver = resolver };
        }

        public async Task StartAsync(WorkflowTestEntry? entry = null,
            WorkflowExecutableReferenceScope requiredScope = WorkflowExecutableReferenceScope.TestRun, bool omitPartition = false)
        {
            entry ??= Entry;
            var request = new WorkflowExecutionStartDispatchRequest(entry.Executable.ArtifactId, "tests", Harness.ExecutionId,
                "entry-key", null, null, null, null, null, WorkflowRunKind.TestRun, new(Reference.SourceReferenceId),
                WorkflowExecutableProvenanceRequirement.RequireLiveReference, null, null, null, omitPartition ? null : Entry.Scope.Partition,
                Entry.Authority, null, 0, Entry.Scope) { TestEntry = entry };
            await Harness.Services.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(request, requiredScope);
        }
    }
}
