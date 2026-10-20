using System.Text.Json;
using Elsa.Activities.Flowchart;
using Elsa.Activities.Flowchart.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime.Contracts;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.Testing;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Services.Recovery;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Xunit;
using FlowchartActivity = Elsa.Activities.Flowchart.Activities.Flowchart;
using ForActivity = Elsa.Activities.For.Activities.For;
using ForEachActivity = Elsa.Activities.ForEach.Activities.ForEach;
using SequenceActivity = Elsa.Activities.Sequence.Activities.Sequence;
using WhileActivity = Elsa.Activities.While.Activities.While;

namespace Elsa.Activities.Runtime.Tests;

/// <summary>
/// Spec 258 R-3: a fault under a composite that does not handle child faults (Sequence, ForEach, While, For) reaches
/// the nearest ancestor that opted into descendant faults, with the composite it passed through. Absorbing it cancels
/// that composite's subtree; requesting intervention keeps the incident blocking. A fault-handling composite in
/// between (a Flowchart) or a reusable-activity boundary ends the walk, and a tree with no opted-in ancestor behaves
/// as before.
/// </summary>
public sealed class DescendantFaultExecutionTests
{
    private static readonly string[] Ids = Enumerable.Range(1, 24).Select(index => $"actexec-{index}").ToArray();
    private static readonly IPayloadSerializer Serializer = SerializationServices.PayloadSerializer();

    [Fact]
    public async Task AFaultInsideASequenceReachesTheReceiver_AndAbsorbingItCancelsTheSequence()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(Sequence("node-sequence",
                WorkflowExecutionHarness.NewProbeNode("node-a"),
                WorkflowExecutionHarness.NewFaultingNode("node-b"),
                WorkflowExecutionHarness.NewProbeNode("node-c")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var delivery = Assert.Single(log.Deliveries);
        Assert.Equal("node-b", delivery.FaultedNodeId);
        Assert.Equal("node-sequence", delivery.ViaNodeId);
        Assert.Equal(run.State("node-sequence").Execution.ActivityExecutionId, delivery.ViaActivityExecutionId);
        Assert.Equal(run.State("node-b").IncidentIds.Single(), delivery.IncidentId);
        Assert.NotNull(delivery.FaultCode);

        run.AssertWorkflowCompleted();
        run.AssertCompleted("node-receiver");
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-sequence").Status);
        run.AssertDidNotRun("node-c");
        var incident = Assert.Single(await Incidents(harness));
        Assert.Equal(IncidentStatus.Resolved, incident.Status);
        Assert.Equal(IncidentResolutionActionKinds.AbsorbFault, incident.ResolutionOutcome?.ActionKind);
    }

    [Fact]
    public async Task AFaultInsideAForEachBodyReachesTheReceiverThroughTheForEach()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(ForEach("node-foreach", WorkflowExecutionHarness.NewFaultingNode("node-body")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal("node-foreach", Assert.Single(log.Deliveries).ViaNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task AFaultInsideAWhileBodyReachesTheReceiverThroughTheWhile()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(While("node-while", WorkflowExecutionHarness.NewFaultingNode("node-body")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal("node-while", Assert.Single(log.Deliveries).ViaNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task AFaultInsideAForBodyReachesTheReceiverThroughTheFor()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(For("node-for", WorkflowExecutionHarness.NewFaultingNode("node-body")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Equal("node-for", Assert.Single(log.Deliveries).ViaNodeId);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task NestedPassiveCompositesAreWalkedPast_AndTheViaChildIsTheReceiversDirectChild()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(Sequence("node-outer-sequence",
                For("node-for", Sequence("node-inner-sequence", WorkflowExecutionHarness.NewFaultingNode("node-body")))))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        var delivery = Assert.Single(log.Deliveries);
        Assert.Equal("node-body", delivery.FaultedNodeId);
        Assert.Equal("node-outer-sequence", delivery.ViaNodeId);
        run.AssertWorkflowCompleted();
        Assert.Equal(ActivityExecutionStatus.Cancelled, run.State("node-for").Status);
    }

    [Fact]
    public async Task RequestingInterventionKeepsTheIncidentBlockingAndTheRunRunning()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Intervene: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(Sequence("node-sequence",
                WorkflowExecutionHarness.NewFaultingNode("node-b"),
                WorkflowExecutionHarness.NewProbeNode("node-c")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Single(log.Deliveries);
        Assert.Equal(WorkflowExecutionStatus.Running, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-sequence").Status);
        var incident = Assert.Single(await Incidents(harness));
        Assert.True(incident.IsBlocking);
        Assert.Equal(IncidentResolutionActionKinds.WaitForIntervention, incident.ResolutionOutcome?.ActionKind);
        run.AssertDidNotRun("node-c");
    }

    [Fact]
    public async Task AFlowchartInBetweenEndsTheWalk_SoTheStepSuspendsAsBefore()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(Flowchart("node-flowchart",
                Sequence("node-sequence", WorkflowExecutionHarness.NewFaultingNode("node-b"))))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        // Known limit (spec 258 R-3): the Flowchart expects faults from its direct children only.
        Assert.Empty(log.Deliveries);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task AReusableActivityBoundaryEndsTheWalk()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(GraphBoundary("node-boundary",
                Sequence("node-sequence", WorkflowExecutionHarness.NewFaultingNode("node-b"))))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Empty(log.Deliveries);
        Assert.NotEqual(WorkflowExecutionStatus.Completed, run.WorkflowState?.Status);
    }

    [Fact]
    public async Task WithoutAnOptedInAncestorASequenceFaultBehavesAsBefore()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(StructuralExecutionTestSupport.NewStructuralNode("node-plain", typeof(PassthroughStructuralActivity),
                Sequence("node-sequence", WorkflowExecutionHarness.NewFaultingNode("node-b")))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Empty(log.Deliveries);
        Assert.Equal(WorkflowExecutionStatus.Faulted, run.WorkflowState?.Status);
        Assert.Equal(ActivityExecutionStatus.Running, run.State("node-sequence").Status);
    }

    [Fact]
    public async Task AReceiverStillGetsItsDirectChildsFaultThroughTheChildFaultCallback()
    {
        var log = new DescendantFaultLog();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true));

        var run = await harness.RunAsync(
            WorkflowExecutionHarness.NewExecutable(Receiver(WorkflowExecutionHarness.NewFaultingNode("node-b"))),
            allowPendingWorkOnTerminalCompletion: true,
            allowAcceptedButFaulted: true);

        Assert.Empty(log.Deliveries);
        Assert.Equal(["child-fault:node-b"], log.Entries);
        run.AssertWorkflowCompleted();
    }

    [Fact]
    public async Task AHostStopBeforeTheDeliveryIsEvaluatedDeliversItOnceAfterRestart()
    {
        var log = new DescendantFaultLog();
        var clock = new ShiftableTimeProvider();
        await using var harness = NewHarness(log, new ReceiverDirective(Absorb: true), services =>
        {
            services.TryAddScoped<IRuntimeResumptionService, RuntimeResumptionService>();
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(clock);
            StopHostOnFirstDescendantFaultDelivery(services);
        });

        var executable = WorkflowExecutionHarness.NewExecutable(Receiver(Sequence("node-sequence",
            WorkflowExecutionHarness.NewFaultingNode("node-b"),
            WorkflowExecutionHarness.NewProbeNode("node-c"))));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            harness.RunAsync(executable, allowPendingWorkOnTerminalCompletion: true, allowAcceptedButFaulted: true));
        Assert.Empty(log.Deliveries);

        // The stopped drain's ownership lease outlives it; a restarted process waits it out.
        clock.Advance(TimeSpan.FromHours(1));

        await harness.SweepUntilQuietAsync();

        var run = await harness.ReadRunAsync(WorkflowExecutionHarness.WorkflowExecutionId);
        Assert.Single(log.Deliveries);
        run.AssertWorkflowCompleted();
        run.AssertDidNotRun("node-c");
    }

    private static void StopHostOnFirstDescendantFaultDelivery(IServiceCollection services)
    {
        var descriptor = services.Single(candidate =>
            candidate.ServiceType == typeof(IWorkflowSchedulerWorkHandler) &&
            candidate.ImplementationType == typeof(WorkflowParentActivityCompletionSchedulerWorkHandler));
        services.Remove(descriptor);
        var stopped = 0;
        services.Add(ServiceDescriptor.Describe(
            typeof(IWorkflowSchedulerWorkHandler),
            serviceProvider => new HostStoppingDeliveryHandler(
                ActivatorUtilities.CreateInstance<WorkflowParentActivityCompletionSchedulerWorkHandler>(serviceProvider),
                () => Interlocked.Exchange(ref stopped, 1) == 0),
            descriptor.Lifetime));
    }

    private sealed class ShiftableTimeProvider : TimeProvider
    {
        private TimeSpan _offset;

        public void Advance(TimeSpan by) => _offset += by;

        public override DateTimeOffset GetUtcNow() => System.GetUtcNow() + _offset;
    }

    private sealed class HostStoppingDeliveryHandler(IWorkflowSchedulerWorkHandler inner, Func<bool> stopNow) : IWorkflowSchedulerWorkHandler
    {
        public string Name => inner.Name;

        public bool CanHandle(RuntimeSchedulerWorkItem workItem) => inner.CanHandle(workItem);

        public ValueTask HandleAsync(RuntimeSchedulerWorkItem workItem, CancellationToken cancellationToken = default) =>
            workItem.CommandMetadata.ContainsKey(RuntimeMetadataKeys.DescendantFault) && stopNow()
                ? throw new OperationCanceledException("The host stopped during the pass.")
                : inner.HandleAsync(workItem, cancellationToken);
    }

    private static async Task<IReadOnlyCollection<IncidentState>> Incidents(WorkflowExecutionHarness harness) =>
        await harness.Services.GetRequiredService<IIncidentStateStore>().ListAsync(WorkflowExecutionHarness.WorkflowExecutionId);

    private static WorkflowExecutionHarness NewHarness(DescendantFaultLog log, ReceiverDirective directive, Action<IServiceCollection>? configure = null) =>
        WorkflowExecutionHarness.Create()
            .WithFeature(services => new SerializationFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesPrimitivesFeature().ConfigureServices(services))
            .WithFeature(services => new ActivitiesFlowchartFeature().ConfigureServices(services))
            .ConfigureServices(services =>
            {
                services.AddSingleton(log);
                services.AddSingleton(directive);
                services.AddScoped<IActivityActivationStrategy, GraphBoundaryActivationStrategy>();
                configure?.Invoke(services);
            })
            .WithProbeLeaf()
            .Build(Ids);

    private static ExecutableNode Receiver(ExecutableNode child) =>
        StructuralExecutionTestSupport.NewStructuralNode("node-receiver", typeof(DescendantFaultReceiverActivity), child);

    private static ExecutableNode Sequence(string nodeId, params ExecutableNode[] activities) =>
        Composite(
            nodeId,
            typeof(SequenceActivity),
            new Dictionary<string, RuntimeInputBinding>(),
            [new ExecutableChildSlot(SequenceActivity.ActivitiesSlotName, activities)],
            new ExecutableActivityStructure(
                SequenceActivity.StructureKind,
                SequenceActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { activities = activities.Select(activity => activity.ExecutableNodeId).ToArray() })));

    private static ExecutableNode ForEach(string nodeId, ExecutableNode body)
    {
        var type = new ValueTypeDescriptor("String", CollectionKind.List);
        return Composite(
            nodeId,
            typeof(ForEachActivity),
            new Dictionary<string, RuntimeInputBinding>
            {
                [nameof(ForEachActivity.Collection)] = new(
                    nameof(ForEachActivity.Collection),
                    type,
                    ValueProtectionPolicy.InstanceInline,
                    RuntimeInputBindingSource.Literal,
                    literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(new[] { "first", "second" }), ValueProtectionPolicy.InstanceInline))
            },
            [new ExecutableChildSlot(ForEachActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                ForEachActivity.StructureKind,
                ForEachActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));
    }

    private static ExecutableNode While(string nodeId, ExecutableNode body) =>
        Composite(
            nodeId,
            typeof(WhileActivity),
            new Dictionary<string, RuntimeInputBinding> { ["Condition"] = Literal("Condition", "Boolean", true) },
            [new ExecutableChildSlot(WhileActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                WhileActivity.StructureKind,
                WhileActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    private static ExecutableNode For(string nodeId, ExecutableNode body) =>
        Composite(
            nodeId,
            typeof(ForActivity),
            new Dictionary<string, RuntimeInputBinding>
            {
                ["Start"] = Literal("Start", "Int32", 0),
                ["End"] = Literal("End", "Int32", 3)
            },
            [new ExecutableChildSlot(ForActivity.BodySlotName, [body])],
            new ExecutableActivityStructure(
                ForActivity.StructureKind,
                ForActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new { body = body.ExecutableNodeId })));

    private static ExecutableNode Flowchart(string nodeId, ExecutableNode start) =>
        Composite(
            nodeId,
            typeof(FlowchartActivity),
            new Dictionary<string, RuntimeInputBinding>(),
            [new ExecutableChildSlot(FlowchartActivity.ActivitiesSlotName, [start])],
            new ExecutableActivityStructure(
                FlowchartActivity.StructureKind,
                FlowchartActivity.StructureSchemaVersion,
                JsonSerializer.SerializeToElement(new FlowchartStructure([], start.ExecutableNodeId))));

    private static ExecutableNode Composite(
        string nodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        ExecutableActivityStructure structure) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(WellKnownRuntimeActivityConsumers.ClrActivity, RuntimeActivityDescriptor.InitialSchemaVersion, ClrConstruction.Payload(Serializer, activityType)),
            inputBindings: inputBindings,
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots,
            structure: structure);

    private static RuntimeInputBinding Literal(string inputName, string alias, object value)
    {
        var type = new ValueTypeDescriptor(alias);
        return new RuntimeInputBinding(
            inputName,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    // A reusable-activity boundary is any node activated from the graph consumer key; this one passes child faults by.
    private static ExecutableNode GraphBoundary(string nodeId, ExecutableNode child)
    {
        var consumerKey = WellKnownRuntimeActivityConsumers.GraphActivity;
        var payload = JsonSerializer.SerializeToElement(new { type = "boundary" });
        var contract = new ActivityContract(
            "test-boundary",
            "1.0.0",
            consumerKey,
            payload,
            [],
            new ActivityResultContract(
                new ValueTypeDescriptor("Object"),
                true,
                ActivityValuePolicy.Default with { Lifecycle = ActivityValueLifecycle.Result },
                []),
            [ActivityOutcomes.Done],
            new ActivityActivationRequirement(consumerKey, consumerKey));

        return new ExecutableNode(
            nodeId,
            $"authored-{nodeId}",
            "test-boundary",
            "1.0.0",
            new RuntimeActivityDescriptor(consumerKey, RuntimeActivityDescriptor.InitialSchemaVersion, payload),
            new Dictionary<string, RuntimeInputBinding>(),
            new Dictionary<string, RuntimeOutputCapture>(),
            new Dictionary<string, string>(),
            [new ExecutableChildSlot(StructuralExecutionTestSupport.ChildSlotName, [child])],
            structure: null,
            activityContract: contract);
    }

    private sealed class GraphBoundaryActivationStrategy(IServiceScopeFactory scopeFactory) : IActivityActivationStrategy
    {
        public string ConsumerKey => WellKnownRuntimeActivityConsumers.GraphActivity;
        public IReadOnlyCollection<string> SupportedSchemaVersions => [RuntimeActivityDescriptor.InitialSchemaVersion];
        public bool RequiresInputHydration => false;

        public ValueTask<ActivityActivationLease> ActivateAsync(ActivityActivationStrategyRequest request, CancellationToken cancellationToken = default)
        {
            var scope = scopeFactory.CreateAsyncScope();
            var activity = ActivatorUtilities.CreateInstance<PassiveBoundaryActivity>(scope.ServiceProvider);
            return ValueTask.FromResult(new ActivityActivationLease(activity, scope));
        }
    }

    public sealed record ReceiverDirective(bool Absorb = false, bool Intervene = false);

    public sealed record DescendantFaultDelivery(
        string FaultedNodeId,
        string? IncidentId,
        string? FaultCode,
        string ViaActivityExecutionId,
        string ViaNodeId);

    public sealed class DescendantFaultLog
    {
        private readonly List<DescendantFaultDelivery> _deliveries = [];
        private readonly List<string> _entries = [];
        public IReadOnlyList<DescendantFaultDelivery> Deliveries { get { lock (_deliveries) return _deliveries.ToArray(); } }
        public IReadOnlyList<string> Entries { get { lock (_entries) return _entries.ToArray(); } }
        public void Add(DescendantFaultDelivery delivery) { lock (_deliveries) _deliveries.Add(delivery); }
        public void Add(string entry) { lock (_entries) _entries.Add(entry); }
    }

    /// <summary>
    /// Opts into descendant faults: records each delivery and absorbs it (then completes) or requests intervention
    /// (then waits). A direct child's fault arrives through the ordinary child-fault callback and is absorbed.
    /// </summary>
    public sealed class DescendantFaultReceiverActivity(DescendantFaultLog log, ReceiverDirective directive) : StructuralActivity,
        IRuntimeStructuralActivity,
        IRuntimeActivityChildCompletionHandler,
        IRuntimeActivityChildFaultHandler,
        IRuntimeActivityDescendantFaultHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());

        public ValueTask<RuntimeStructuralContinuation> OnChildFaultedAsync(ActivityChildFaultedContext context)
        {
            log.Add($"child-fault:{context.FaultedChildExecutableNodeId}");
            ((IRuntimeActivityExecutionContext)context.ParentContext).RequestChildFaultAbsorption(context.IncidentId!, "absorbed");
            return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
        }

        public ValueTask<RuntimeStructuralContinuation> OnDescendantFaultedAsync(ActivityDescendantFaultedContext context)
        {
            log.Add(new DescendantFaultDelivery(
                context.FaultedDescendantExecutableNodeId,
                context.IncidentId,
                context.FaultCode,
                context.ViaChildActivityExecutionId,
                context.ViaChildExecutableNodeId));
            var runtimeContext = (IRuntimeActivityExecutionContext)context.ParentContext;
            if (directive.Intervene)
            {
                runtimeContext.RequestChildFaultIntervention(context.IncidentId!, "operator");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
            }

            if (directive.Absorb)
            {
                runtimeContext.RequestChildFaultAbsorption(context.IncidentId!, "absorbed");
                return ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
            }

            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }
    }

    /// <summary>A boundary that completes with its child and does not handle child faults.</summary>
    public sealed class PassiveBoundaryActivity : StructuralActivity, IRuntimeStructuralActivity, IRuntimeActivityChildCompletionHandler
    {
        public ValueTask<RuntimeStructuralContinuation> ExecuteStructureAsync(IRuntimeActivityExecutionContext context)
        {
            var child = Assert.Single(Assert.Single(context.ExecutableNode.ChildSlots).Activities);
            context.ScheduleChildActivity(child.ExecutableNodeId, context.ActivityExecutionState.InvocationId);
            return ValueTask.FromResult(RuntimeStructuralContinuation.Defer);
        }

        public ValueTask<RuntimeStructuralContinuation> OnChildCompletedAsync(ActivityChildCompletedContext context) =>
            ValueTask.FromResult(RuntimeStructuralContinuation.Complete());
    }
}
