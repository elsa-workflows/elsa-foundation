using System.Text.Json;
using CShells.Lifecycle;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Services;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Services.ActivityExecutions;
using Elsa.Workflows.Runtime.Services.Dispatch;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;

namespace Elsa.Activities.DispatchWorkflow.Tests;

/// <summary>Constructed retained-boundary controls and actual host admission; authored-control delivery remains a later gate.</summary>
public sealed class WorkflowControlBoundaryTests
{
    [Fact]
    public async Task Actual_waited_dispatch_admits_capability_and_carries_the_hint_into_the_child_checkpoint()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var run = await fixture.StartParentAsync("control-capability", "control-parent-workflow", "correlation",
            waitForCompletion: true, controlConsumerVersion: 1);
        var record = run.Dispatch;
        Assert.Equal("test.control", record.Metadata[RuntimeMetadataKeys.ChildControlConsumerKey]);
        Assert.Equal("1", record.Metadata[RuntimeMetadataKeys.ChildControlConsumerVersion]);
        await fixture.SweepAsync();
        var child = await fixture.Services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(record.ChildWorkflowExecutionId);
        Assert.NotNull(child);
        Assert.Equal(record.DispatchId, child.SystemMetadata[RuntimeMetadataKeys.ChildControlDispatchId]);
        var root = Assert.Single(await fixture.Services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(record.ChildWorkflowExecutionId));
        var duringExecution = Assert.Single(fixture.ChildProbe.ControlObservations);
        Assert.Equal(WorkflowDispatchStatus.Started, duringExecution.DispatchStatus);
        Assert.Equal(root.InvocationId, duringExecution.RootActivityExecutionId);
        Assert.Equal(record.ChildWorkflowExecutionId, duringExecution.WorkflowExecutionId);
        Assert.Equal(record.DispatchId, duringExecution.Target!.DispatchId);
        Assert.Equal(record.ParentActivityExecutionId, duringExecution.Target.CallingActivityExecutionId);
        await using var scope = fixture.Services.CreateAsyncScope();
        var target = await scope.ServiceProvider.GetRequiredService<IRuntimeWorkflowControlBoundary>()
            .ResolveAsync(record.ChildWorkflowExecutionId, root.InvocationId);
        Assert.Equal(record.ParentActivityExecutionId, target!.CallingActivityExecutionId);
        var caller = await fixture.Services.GetRequiredService<IActivityExecutionStateStore>()
            .FindAsync(record.ParentWorkflowExecutionId, record.ParentActivityExecutionId);
        Assert.Equal(caller!.ParentActivityExecutionId, target.ConsumerActivityExecutionId);
        Assert.Equal(target.ConsumerActivityExecutionId, duringExecution.Target.ConsumerActivityExecutionId);
    }

    [Fact]
    public async Task Ordinary_child_root_observes_no_capability_during_actual_execution()
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        await fixture.StartParentAsync("ordinary-boundary", "ordinary-parent", "correlation", waitForCompletion: true);
        await fixture.SweepAsync();
        var observed = Assert.Single(fixture.ChildProbe.ControlObservations);
        Assert.Null(observed.Target);
        Assert.Null(observed.DispatchStatus);
    }

    [Fact]
    public async Task Store_failure_is_wrapped_with_context_and_original_cause()
    {
        var cause = new IOException("Injected durable-store outage.");
        await using var fixture = await Fixture.CreateAsync("valid", afterFeature: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkflowDispatchStore>(new ThrowingDispatchStore(cause))));
        var failure = await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () =>
            await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root"));
        Assert.Same(cause, failure.InnerException);
        Assert.Equal(fixture.Identity.ChildWorkflowExecutionId, failure.WorkflowExecutionId);
        Assert.Equal("child-root", failure.RootActivityExecutionId);
    }

    [Fact]
    public async Task Cancellation_passes_through_without_wrapping()
    {
        var cause = new OperationCanceledException();
        await using var fixture = await Fixture.CreateAsync("valid", afterFeature: services =>
            services.Replace(ServiceDescriptor.Singleton<IWorkflowDispatchStore>(new ThrowingDispatchStore(cause))));
        var failure = await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root"));
        Assert.Same(cause, failure);
    }

    [Fact]
    public async Task Explicit_scoped_override_is_preserved_and_default_is_scoped()
    {
        await using var replaced = await Fixture.CreateAsync("valid", beforeFeature: services =>
            services.AddScoped<IRuntimeWorkflowControlBoundary, ReplacementBoundary>());
        await replaced.InitializeControlAsync();
        Assert.IsType<ReplacementBoundary>(replaced.Boundary);
        await using var defaults = await Fixture.CreateAsync("valid");
        await defaults.InitializeControlAsync();
        using var first = defaults.Services.CreateScope();
        using var second = defaults.Services.CreateScope();
        var boundary = first.ServiceProvider.GetRequiredService<IRuntimeWorkflowControlBoundary>();
        Assert.Same(boundary, first.ServiceProvider.GetRequiredService<IRuntimeWorkflowControlBoundary>());
        Assert.NotSame(boundary, second.ServiceProvider.GetRequiredService<IRuntimeWorkflowControlBoundary>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Startup_rejects_missing_or_duplicate_providers(bool duplicate)
    {
        await using var fixture = await Fixture.CreateAsync("valid", afterFeature: services =>
        {
            if (duplicate) services.AddScoped<IRuntimeWorkflowControlBoundary, ReplacementBoundary>();
            else services.RemoveAll<IRuntimeWorkflowControlBoundary>();
        });
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.InitializeControlAsync());
        Assert.Contains("exactly one", failure.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false, 1)]
    [InlineData(true, 2)]
    public async Task Actual_dispatch_refuses_unadmitted_control_without_starting_a_child(bool wait, int requestedVersion)
    {
        await using var fixture = await DispatchWorkflowRuntimeTestFixture.CreateAsync();
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await fixture.StartParentAsync("refused-control", "refused-control-parent", "correlation",
                waitForCompletion: wait, controlConsumerVersion: requestedVersion));
        Assert.Contains(wait ? "pinned parent does not declare requested control protocol" :
            "control requires an exact waited direct-child invocation", exception.Message, StringComparison.Ordinal);
        Assert.Empty(await fixture.Services.GetRequiredService<IWorkflowDispatchStore>().ListAsync("refused-control-parent"));
        Assert.Empty(fixture.ChildProbe.Observations);
    }

    [Theory]
    [InlineData("valid")]
    [InlineData("cancelled-after-admission")]
    public async Task Exact_admitted_boundary_resolves_without_using_delivery_status_as_authority(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync(scenario);
        var target = await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root");
        Assert.NotNull(target);
        Assert.Equal(fixture.Identity.DispatchId, target.DispatchId);
        Assert.Equal("parent-workflow", target.ParentWorkflowExecutionId);
        Assert.Equal("call-invocation", target.CallingActivityExecutionId);
        Assert.Equal("consumer-invocation", target.ConsumerActivityExecutionId);
        Assert.Equal("child-root", target.RootActivityExecutionId);
        Assert.Equal("test.control", target.ConsumerKey);
        Assert.Equal(1, target.ConsumerVersion);
    }

    [Theory]
    [InlineData("legacy")]
    [InlineData("parent-id-only")]
    public async Task No_capability_hint_keeps_legacy_and_ordinary_root_behavior(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync(scenario);
        Assert.Null(await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root"));
    }

    [Theory]
    [InlineData("pending")]
    [InlineData("cancelled-before-admission")]
    [InlineData("fire-and-forget")]
    [InlineData("missing-record-capability")]
    [InlineData("foreign-dispatch-hint")]
    [InlineData("foreign-workflow-parent")]
    [InlineData("foreign-child-pin")]
    [InlineData("foreign-tenant")]
    [InlineData("foreign-partition")]
    [InlineData("foreign-authority")]
    [InlineData("foreign-test-scope")]
    [InlineData("wrong-depth")]
    [InlineData("wrong-provenance-parent")]
    [InlineData("missing-caller-capability")]
    [InlineData("wrong-consumer-version")]
    [InlineData("not-a-root")]
    [InlineData("foreign-root-scheduling-workflow")]
    [InlineData("foreign-root-scheduler")]
    [InlineData("foreign-root-provenance-scheduler")]
    [InlineData("duplicate-root")]
    public async Task Conflicting_retained_boundary_refuses_control(string scenario)
    {
        await using var fixture = await Fixture.CreateAsync(scenario);
        var exception = await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () =>
            await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "child-root"));
        Assert.Contains("control", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_different_root_invocation_cannot_reuse_the_same_workflow_hint()
    {
        await using var fixture = await Fixture.CreateAsync("valid");
        await Assert.ThrowsAsync<WorkflowControlBoundaryException>(async () =>
            await fixture.Boundary.ResolveAsync(fixture.Identity.ChildWorkflowExecutionId, "arbitrary-root"));
    }

    internal sealed class Fixture(ServiceProvider services, WorkflowDispatchIdentity identity) : IAsyncDisposable
    {
        public ServiceProvider Services => services;
        public WorkflowDispatchIdentity Identity { get; } = identity;
        public IRuntimeWorkflowControlBoundary Boundary => services.GetRequiredService<IRuntimeWorkflowControlBoundary>();
        public ValueTask DisposeAsync() => services.DisposeAsync();

        public Task InitializeControlAsync() => services.GetServices<IShellInitializer>()
            .Single(initializer => initializer is not WorkflowDispatchReadinessInitializer).InitializeAsync(CancellationToken.None);

        public static async Task<Fixture> CreateAsync(string scenario, Action<IServiceCollection>? beforeFeature = null,
            Action<IServiceCollection>? afterFeature = null)
        {
            var now = DateTimeOffset.UnixEpoch.AddDays(1);
            var identity = new WorkflowDispatchIdentity("parent-workflow", "call-invocation");
            var parentPin = new WorkflowExecutableIdentity("parent-artifact", "parent-definition", "parent-version", "1.0.0", "parent-hash");
            var childPin = new WorkflowExecutableIdentity("child-artifact", "child-definition", "child-version", "1.0.0", "child-hash");
            var capability = new Dictionary<string, string>
            {
                [RuntimeMetadataKeys.ChildControlConsumerKey] = "test.control",
                [RuntimeMetadataKeys.ChildControlConsumerVersion] = "1"
            };
            var parentMetadata = new Dictionary<string, string>(capability);
            if (scenario == "wrong-consumer-version")
                parentMetadata[RuntimeMetadataKeys.ChildControlConsumerVersion] = "2";
            var callNode = Node("call-node", DispatchWorkflowConstants.ActivityType);
            var consumerNode = Node("consumer-node", "test.consumer", parentMetadata, [new ExecutableChildSlot("Activities", [callNode])]);
            var rootNode = Node("child-root-node", "test.root");
            var parentExecutable = new WorkflowExecutable(parentPin, consumerNode, new Dictionary<string, WorkflowExecutableResumeTarget>(), now,
                new Dictionary<string, string>(), new IncidentStrategyReference("Fault", "1"));
            var childExecutable = new WorkflowExecutable(childPin, rootNode, new Dictionary<string, WorkflowExecutableResumeTarget>(), now,
                new Dictionary<string, string>(), new IncidentStrategyReference("Fault", "1"));
            var workflows = new InMemoryWorkflowExecutionStateStore();
            var activities = new InMemoryActivityExecutionStateStore();
            var executables = new InMemoryWorkflowExecutableStore();
            var dispatches = new InMemoryWorkflowDispatchStore();
            await executables.SaveAsync(parentExecutable);
            await executables.SaveAsync(childExecutable);
            var partition = new WorkflowExecutionPartition("partition");
            var authority = WorkflowExecutionAuthoritySnapshot.CreateRoot("owner");
            var childAuthority = new WorkflowExecutionAuthoritySnapshot("parent-workflow", "owner");
            var scope = new WorkflowTestScope("test-scope", now.AddDays(1), "tenant", partition);
            var parentWorkflow = new WorkflowExecutionState("parent-workflow", parentPin, WorkflowExecutionStatus.Running, null,
                now, now, now, null, null, null, "tenant", new Dictionary<string, string>())
            { Partition = partition, Authority = authority, RunKind = WorkflowRunKind.TestRun, TestScope = scope };
            var hints = new Dictionary<string, string>();
            if (scenario is not ("legacy" or "parent-id-only"))
                hints[RuntimeMetadataKeys.ChildControlDispatchId] = scenario == "foreign-dispatch-hint" ? "foreign-dispatch" : identity.DispatchId;
            var childWorkflow = new WorkflowExecutionState(identity.ChildWorkflowExecutionId,
                scenario == "foreign-child-pin" ? childPin with { ArtifactHash = "other" } : childPin,
                WorkflowExecutionStatus.Running, null, now, now, now, null, null,
                scenario == "foreign-workflow-parent" ? "foreign-parent" : "parent-workflow",
                scenario == "foreign-tenant" ? "other" : "tenant", hints)
            {
                Partition = scenario == "foreign-partition" ? new WorkflowExecutionPartition("other") : partition,
                Authority = scenario == "foreign-authority" ? WorkflowExecutionAuthoritySnapshot.CreateRoot("other") : childAuthority,
                RunKind = WorkflowRunKind.TestRun,
                DispatchNestingDepth = scenario == "wrong-depth" ? 2 : 1,
                TestScope = scenario == "foreign-test-scope" ? new WorkflowTestScope("other-scope", now.AddDays(1), "tenant", partition) :
                    scenario is "foreign-tenant" or "foreign-partition" ? null : scope
            };
            await workflows.SaveAsync(parentWorkflow);
            await workflows.SaveAsync(childWorkflow);
            await activities.SaveAsync(State("consumer-invocation", "parent-workflow", consumerNode, null, null, now));
            var callCapability = scenario == "missing-caller-capability" ? new Dictionary<string, string>() : capability;
            var call = State("call-invocation", "parent-workflow", callNode, "consumer-invocation", callCapability, now);
            if (scenario == "wrong-provenance-parent")
                call = call with { Provenance = call.Provenance with { ParentActivityExecutionId = "other" } };
            await activities.SaveAsync(call);
            var root = State("child-root", identity.ChildWorkflowExecutionId, rootNode,
                scenario == "not-a-root" ? "other" : null, null, now);
            root = scenario switch
            {
                "foreign-root-scheduling-workflow" => root with { Provenance = root.Provenance with { SchedulingWorkflowExecutionId = "foreign" } },
                "foreign-root-scheduler" => root with { SchedulingActivityExecutionId = "foreign" },
                "foreign-root-provenance-scheduler" => root with { Provenance = root.Provenance with { SchedulingActivityExecutionId = "foreign" } },
                _ => root
            };
            await activities.SaveAsync(root);
            if (scenario == "duplicate-root")
                await activities.SaveAsync(root with { Execution = root.Execution with { ActivityExecutionId = "second-root" } });
            var source = new WorkflowExecutableSourceProvenance("source", "WorkflowDefinition", "child-definition", "1",
                "child-definition", "child-version", "1.0.0", "publication", "default");
            var record = new WorkflowDispatchRecord(identity.DispatchId, "parent-workflow", "call-invocation",
                identity.ChildWorkflowExecutionId, childPin, source,
                scenario == "fire-and-forget" ? WorkflowDispatchMode.FireAndForget : WorkflowDispatchMode.WaitForCompletion,
                WorkflowDispatchStatus.Pending, null, "tenant", partition, WorkflowRunKind.TestRun, childAuthority, [], now, now,
                scenario is "missing-record-capability" or "legacy" or "parent-id-only" ? new Dictionary<string, string>() : capability, 1, scope);
            await dispatches.SaveAsync(record);
            if (scenario == "cancelled-before-admission")
                await dispatches.SaveAsync(WorkflowDispatchLifecycle.CancelBeforeAdmission(record, now));
            else if (scenario != "pending")
            {
                var admitted = await dispatches.TryAdmitAsync(identity.DispatchId, now);
                if (scenario == "cancelled-after-admission")
                    await dispatches.SaveAsync(admitted.Record.TransitionTo(WorkflowDispatchStatus.Cancelled, now));
            }
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddSingleton<IWorkflowOutputSource, NoOutputs>();
            services.AddSingleton<IPostCommitOutboxLookupStore, NoOutboxLookup>();
            services.AddSingleton<IWorkflowExecutionStateStore>(workflows);
            services.AddSingleton<IActivityExecutionStateStore>(activities);
            services.AddSingleton<IWorkflowExecutableStore>(executables);
            services.AddSingleton<IWorkflowDispatchStore>(dispatches);
            beforeFeature?.Invoke(services);
            new DispatchWorkflowRuntimeFeature().ConfigureServices(services);
            afterFeature?.Invoke(services);
            return new Fixture(services.BuildServiceProvider(), identity);
        }

        private static ExecutableNode Node(string id, string type, IReadOnlyDictionary<string, string>? metadata = null,
            IReadOnlyCollection<ExecutableChildSlot>? children = null) => new(id, "authored-" + id, type, "1.0.0",
            new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
            new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, RuntimeOutputCapture>(), metadata ?? new Dictionary<string, string>(), children);

        private static ActivityExecutionState State(string id, string workflow, ExecutableNode node, string? parent,
            IReadOnlyDictionary<string, string>? metadata, DateTimeOffset now) => new(
            new ActivityExecution(id, workflow, node.ExecutableNodeId, node.AuthoredActivityId, node.ActivityType, node.ActivityTypeVersion),
            ActivityExecutionStatus.Running, null, 0, now, now, null, parent, parent, null, null,
            ActivitySchedulingProvenance.From(workflow, parent, parent, null, null, null, null, null, metadata),
            null, [], [], 0, 0, new Dictionary<string, string>());
    }

    private sealed class ReplacementBoundary : IRuntimeWorkflowControlBoundary
    {
        public ReplacementBoundary() { }
        public ValueTask<RuntimeChildControlTarget?> ResolveAsync(string workflowExecutionId, string rootActivityExecutionId,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<RuntimeChildControlTarget?>(null);
    }

    private sealed class NoOutputs : IWorkflowOutputSource
    {
        public NoOutputs() { }
        public ValueTask<IReadOnlyCollection<RuntimeWorkflowOutput>> ReadAsync(string workflowExecutionId,
            IReadOnlyCollection<RuntimeStateChange<DurableValueState>>? pendingDurableValueChanges = null,
            CancellationToken cancellationToken = default) => ValueTask.FromResult<IReadOnlyCollection<RuntimeWorkflowOutput>>([]);
    }

    private sealed class NoOutboxLookup : IPostCommitOutboxLookupStore
    {
        public NoOutboxLookup() { }
        public ValueTask<RuntimePostCommitOutboxItem?> FindAsync(string outboxItemId, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<RuntimePostCommitOutboxItem?>(null);
    }

    private sealed class ThrowingDispatchStore(Exception cause) : IWorkflowDispatchStore
    {
        public ValueTask<WorkflowDispatchRecord?> FindAsync(string dispatchId, CancellationToken cancellationToken = default) =>
            ValueTask.FromException<WorkflowDispatchRecord?>(cause);
        public ValueTask<WorkflowDispatchRecord> SaveAsync(WorkflowDispatchRecord record, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListAsync(string parentWorkflowExecutionId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
