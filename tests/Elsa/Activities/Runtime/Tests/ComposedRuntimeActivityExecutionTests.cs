using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Activities.Testing;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class ComposedRuntimeActivityExecutionTests
{
    private readonly DateTimeOffset _now = new(2026, 6, 11, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task InProcessAgent_DrainsStartThroughComposedActivityInvocation()
    {
        var observer = new RecordingSchedulerDrainObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerDrainObserver>(observer);
        services.AddSingleton<InlineExecutionProbe>();
        services.AddScoped<RequestScopedExecutionProbe>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await ActivityConstructorTestHost.InitializeAsync(provider);
        var executable = NewExecutable(_now);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var dispatchResult = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));

        var queuedItems = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var activityState = Assert.Single(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1"));
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        var drainResult = Assert.Single(observer.ObservedResults);
        var probe = provider.GetRequiredService<InlineExecutionProbe>();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, dispatchResult.Status);
        Assert.Empty(queuedItems);
        Assert.Equal(ActivityExecutionStatus.Completed, activityState.Status);
        Assert.Equal("node-start", activityState.Execution.ExecutableNodeId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        Assert.Equal($"probe:node-start:{activityState.Execution.ActivityExecutionId}", probe.Invocation);
        Assert.False(drainResult.StoppedOnFault);
        Assert.Equal(
            [
                WorkflowExecutionCommandKind.Start,
                WorkflowExecutionCommandKind.Checkpoint,
                WorkflowExecutionCommandKind.ScheduleActivity,
                WorkflowExecutionCommandKind.StartActivity,
                WorkflowExecutionCommandKind.InvokeActivity,
                WorkflowExecutionCommandKind.CompleteActivity,
                WorkflowExecutionCommandKind.CompleteActivity,
                WorkflowExecutionCommandKind.Checkpoint
            ],
            drainResult.Items.Select(item => item.CommandKind).ToArray());
        Assert.Contains(drainResult.Items, item => item.HandlerName == WorkflowInvokeActivitySchedulerWorkHandler.HandlerName);
        Assert.DoesNotContain(drainResult.Items, item => item.HandlerName == MissingActivityInvocationSchedulerWorkHandler.HandlerName);
    }

    [Fact]
    public async Task InProcessAgent_UsesDispatchAmbientServicesForActivityExecutionContext()
    {
        var services = new ServiceCollection();
        services.AddSingleton<InlineExecutionProbe>();
        services.AddScoped<RequestScopedExecutionProbe>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await ActivityConstructorTestHost.InitializeAsync(provider);
        await using var requestScope = provider.CreateAsyncScope();
        var executable = NewExecutable(_now);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var requestProbe = requestScope.ServiceProvider.GetRequiredService<RequestScopedExecutionProbe>();
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var dispatchResult = await agent.EnqueueAsync(
            NewStartEnvelope(executable.Identity),
            new WorkflowExecutionCommandDispatchOptions(requestScope.ServiceProvider));

        var activityState = Assert.Single(await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1"));
        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, dispatchResult.Status);
        Assert.Equal($"probe:node-start:{activityState.Execution.ActivityExecutionId}", requestProbe.Invocation);
    }

    [Fact]
    public async Task StartDispatcher_WithAmbientServices_LeavesNoServiceProviderInPersistedState()
    {
        // Spec 089 T003 / FR-021: driving a real start through the T002 dispatcher spine WITH ambient services must
        // (a) route those services to the inline-drained activity — proven by the request-scoped probe firing — while
        // (b) leaving no live-service reference anywhere in the durably persisted stores. The persisted state models
        // are string/JSON/primitive by design; assert both the type surface and the serialized JSON stay provider-free.
        var services = new ServiceCollection();
        services.AddSingleton<InlineExecutionProbe>();
        services.AddScoped<RequestScopedExecutionProbe>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await ActivityConstructorTestHost.InitializeAsync(provider);
        await using var requestScope = provider.CreateAsyncScope();
        var executable = NewExecutable(_now);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var requestProbe = requestScope.ServiceProvider.GetRequiredService<RequestScopedExecutionProbe>();

        // The exact call the stimulus router makes on the sync-mode HTTP path (spec 089 E-D4).
        var result = await provider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(executable.Identity.ArtifactId, "runtime-test"),
            dispatchOptions: new WorkflowExecutionCommandDispatchOptions(requestScope.ServiceProvider));

        // (a) The ambient request scope reached the inline-drained activity execution context.
        var activityStates = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync(result.WorkflowExecutionId);
        var activityState = Assert.Single(activityStates);
        Assert.Equal($"probe:node-start:{activityState.Execution.ActivityExecutionId}", requestProbe.Invocation);

        // (b) No persisted store carries a live-service reference — neither on the model type surface nor in its JSON.
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(result.WorkflowExecutionId);
        var durableValues = await provider.GetRequiredService<IDurableValueStateStore>().ListAsync(result.WorkflowExecutionId);

        AssertNoServiceProviderReference(workflowState!);
        foreach (var state in activityStates)
            AssertNoServiceProviderReference(state);
        foreach (var durableValue in durableValues)
            AssertNoServiceProviderReference(durableValue);
    }

    private static void AssertNoServiceProviderReference(object persisted)
    {
        Assert.DoesNotContain(
            persisted.GetType().GetProperties(),
            property => typeof(IServiceProvider).IsAssignableFrom(property.PropertyType));

        // The persisted state is serializable; a leaked provider would surface as its concrete type name in the JSON.
        var json = JsonSerializer.Serialize(persisted);
        Assert.DoesNotContain("ServiceProvider", json, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task InProcessAgent_DrainsComposedParentAndChildActivityExecutionToQuiescence()
    {
        var observer = new RecordingSchedulerDrainObserver();
        var services = new ServiceCollection();
        services.AddSingleton<IWorkflowSchedulerDrainObserver>(observer);
        services.AddSingleton<InlineExecutionProbe>();
        services.AddScoped<RequestScopedExecutionProbe>();
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ParentCompositeActivityConstructor>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        await ActivityConstructorTestHost.InitializeAsync(provider);
        var executable = NewCompositeExecutable(_now);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var startEnvelope = NewStartEnvelope(executable.Identity);
        var agent = await provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest("wfexec-1"));

        var dispatchResult = await agent.EnqueueAsync(startEnvelope);

        var queuedItems = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));
        var activityStates = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var parentState = Assert.Single(activityStates, state => state.Execution.ExecutableNodeId == "node-parent");
        var childState = Assert.Single(activityStates, state => state.Execution.ExecutableNodeId == "node-child");
        var workflowState = await provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("wfexec-1");
        var inspectionSummaries = await provider.GetRequiredService<IActivityExecutionInspectionStore>()
            .ListSummariesAsync("wfexec-1");
        var drainResult = Assert.Single(observer.ObservedResults);
        var probe = provider.GetRequiredService<InlineExecutionProbe>();

        Assert.Equal(WorkflowExecutionCommandDispatchStatus.Accepted, dispatchResult.Status);
        Assert.Empty(queuedItems);
        Assert.All(activityStates, state => Assert.Equal(ActivityExecutionStatus.Completed, state.Status));
        Assert.Null(parentState.ParentActivityExecutionId);
        Assert.Equal(parentState.Execution.ActivityExecutionId, childState.ParentActivityExecutionId);
        Assert.Equal(parentState.Execution.ActivityExecutionId, childState.SchedulingActivityExecutionId);
        Assert.Equal(WorkflowExecutionStatus.Completed, workflowState?.Status);
        Assert.Contains($"parent:execute:node-parent:{parentState.Execution.ActivityExecutionId}", probe.Invocations);
        Assert.Contains($"probe:node-child:{childState.Execution.ActivityExecutionId}", probe.Invocations);
        Assert.Contains($"parent:child-completed:node-parent:{parentState.Execution.ActivityExecutionId}:{childState.Execution.ActivityExecutionId}", probe.Invocations);
        Assert.Equal(2, inspectionSummaries.Count);
        Assert.Contains(inspectionSummaries, summary => summary.ActivityExecutionId == parentState.Execution.ActivityExecutionId && summary.Status == ActivityExecutionStatus.Completed);
        Assert.Contains(inspectionSummaries, summary => summary.ActivityExecutionId == childState.Execution.ActivityExecutionId && summary.Status == ActivityExecutionStatus.Completed);
        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, drainResult.StopReason);
        Assert.False(drainResult.StoppedOnFault);
        Assert.False(drainResult.StoppedOnPause);
        Assert.True(drainResult.OutboxDeliveredCount >= 2);
        Assert.True(drainResult.Items.Count(item => item.CommandKind == WorkflowExecutionCommandKind.ScheduleActivity) >= 2);
        Assert.True(drainResult.Items.Count(item => item.CommandKind == WorkflowExecutionCommandKind.StartActivity) >= 2);
        Assert.True(drainResult.Items.Count(item => item.CommandKind == WorkflowExecutionCommandKind.InvokeActivity) >= 2);
        Assert.True(drainResult.Items.Count(item => item.CommandKind == WorkflowExecutionCommandKind.CompleteActivity) >= 3);

        var rerunResult = await provider.GetRequiredService<IWorkflowDrainOrchestrator>()
            .DrainAsync(startEnvelope, new RuntimeSchedulerDrainRequest("wfexec-1"));
        var rerunStates = await provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync("wfexec-1");
        var rerunQueuedItems = await provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAsync(new RuntimeSchedulerWorkQuery("wfexec-1"));

        Assert.Equal(RuntimeSchedulerDrainStopReason.Quiesced, rerunResult.StopReason);
        Assert.Empty(rerunResult.Items);
        Assert.Equal(0, rerunResult.OutboxDeliveredCount);
        Assert.Empty(rerunQueuedItems);
        Assert.Equal(2, rerunStates.Count);
        Assert.Equal(2, observer.ObservedResults.Count);
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: "wfexec-1",
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: _now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string> { ["source"] = "test" });

        return new(
            envelopeId: "envelope-start",
            workflowExecutionId: "wfexec-1",
            command: command,
            idempotencyKey: "wfexec-1:start:artifact-1",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: _now,
            sequence: 1,
            metadata: new Dictionary<string, string> { ["transport"] = "in-process" });
    }

    private static WorkflowExecutable NewExecutable(DateTimeOffset now)
    {
        var descriptor = new ProbeActivityDescriptor("probe");
        var node = new ExecutableNode(
            executableNodeId: "node-start",
            authoredActivityId: "authored-node-start",
            activityType: "test/probe",
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(ProbeActivityConstructor.ConsumerKeyValue, RuntimeActivityDescriptor.InitialSchemaVersion, JsonSerializer.SerializeToElement(descriptor)),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static WorkflowExecutable NewCompositeExecutable(DateTimeOffset now)
    {
        var child = NewNode(
            executableNodeId: "node-child",
            activityType: "test/probe",
            descriptor: new RuntimeActivityDescriptor(
                ProbeActivityConstructor.ConsumerKeyValue,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new ProbeActivityDescriptor("probe"))));
        var parent = NewNode(
            executableNodeId: "node-parent",
            activityType: "test/parent",
            descriptor: new RuntimeActivityDescriptor(
                ParentCompositeActivityConstructor.ConsumerKeyValue,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(new ParentCompositeActivityDescriptor("parent"))),
            childSlots:
            [
                new ExecutableChildSlot("children", [child])
            ]);

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            rootActivity: parent,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static ExecutableNode NewNode(
        string executableNodeId,
        string activityType,
        RuntimeActivityDescriptor descriptor,
        IReadOnlyCollection<ExecutableChildSlot>? childSlots = null) =>
        new(
            executableNodeId: executableNodeId,
            authoredActivityId: $"authored-{executableNodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptor: descriptor,
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>(),
            childSlots: childSlots);

    private sealed class ProbeActivityConstructor : IActivityConstructor<ProbeActivityDescriptor>
    {
        public static string ConsumerKeyValue => typeof(ProbeActivityDescriptor).FullName!;

        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var descriptor = payload.Deserialize<ProbeActivityDescriptor>()
                             ?? throw new InvalidOperationException("Probe descriptor resolved to null.");
            return Construct(descriptor, inputs, outputs, cancellationToken);
        }

        public ValueTask<IActivity> Construct(
            ProbeActivityDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new ProbeActivity(descriptor.Message));
    }

    private sealed record ProbeActivityDescriptor(string Message);

    private sealed class ProbeActivity(string message) : CodeActivity("test/probe")
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            var probe = context.GetRequiredService<InlineExecutionProbe>();
            probe.Record($"{message}:{NodeId}:{Id}");
            context.GetRequiredService<RequestScopedExecutionProbe>().Record($"{message}:{NodeId}:{Id}");
        }
    }

    private sealed class ParentCompositeActivityConstructor : IActivityConstructor<ParentCompositeActivityDescriptor>
    {
        public static string ConsumerKeyValue => typeof(ParentCompositeActivityDescriptor).FullName!;

        public string ConsumerKey => ConsumerKeyValue;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var descriptor = payload.Deserialize<ParentCompositeActivityDescriptor>()
                             ?? throw new InvalidOperationException("Parent composite descriptor resolved to null.");
            return Construct(descriptor, inputs, outputs, cancellationToken);
        }

        public ValueTask<IActivity> Construct(
            ParentCompositeActivityDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new ParentCompositeActivity(descriptor.Message));
    }

    private sealed record ParentCompositeActivityDescriptor(string Message);

    private sealed class ParentCompositeActivity(string message) : CodeActivity("test/parent"), IActivityChildCompletionHandler
    {
        protected override void Execute(IActivityExecutionContext context)
        {
            var runtimeContext = Assert.IsAssignableFrom<IRuntimeActivityExecutionContext>(context);
            var parentActivityExecutionId = runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId;
            context.GetRequiredService<InlineExecutionProbe>()
                .Record($"{message}:execute:{NodeId}:{Id}");

            runtimeContext.ScheduleChildActivity(
                "node-child",
                parentActivityExecutionId,
                new Dictionary<string, string> { ["test.parentActivityExecutionId"] = parentActivityExecutionId });
        }

        public ValueTask OnChildCompletedAsync(ActivityChildCompletedContext context)
        {
            var runtimeContext = Assert.IsAssignableFrom<IRuntimeActivityExecutionContext>(context.ParentContext);
            context.ParentContext.GetRequiredService<InlineExecutionProbe>()
                .Record($"{message}:child-completed:{NodeId}:{Id}:{context.CompletedChildActivityExecutionId}");
            runtimeContext.CompleteCompositeActivity([ActivityOutcomes.Done]);
            return ValueTask.CompletedTask;
        }
    }

    private sealed class InlineExecutionProbe
    {
        private readonly List<string> _invocations = [];

        public string? Invocation => _invocations.LastOrDefault();
        public IReadOnlyList<string> Invocations => _invocations;

        public void Record(string invocation) => _invocations.Add(invocation);
    }

    private sealed class RequestScopedExecutionProbe
    {
        private readonly List<string> _invocations = [];

        public string? Invocation => _invocations.LastOrDefault();
        public IReadOnlyList<string> Invocations => _invocations;

        public void Record(string invocation) => _invocations.Add(invocation);
    }

    private sealed class RecordingSchedulerDrainObserver : IWorkflowSchedulerDrainObserver
    {
        public List<RuntimeSchedulerDrainResult> ObservedResults { get; } = [];

        public ValueTask OnDrainedAsync(
            WorkflowExecutionCommandEnvelope envelope,
            RuntimeSchedulerDrainResult result,
            CancellationToken cancellationToken = default)
        {
            ObservedResults.Add(result);
            return ValueTask.CompletedTask;
        }
    }
}
