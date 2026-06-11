using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Services;
using Elsa.Workflows.Runtime.Api;
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
        services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        await using var provider = services.BuildServiceProvider();
        var executable = NewExecutable(_now);
        await provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await provider.GetRequiredService<IWorkflowExecutionAgentProvider>()
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

    private WorkflowExecutionAgentActivationRequest NewActivationRequest(string workflowExecutionId) =>
        new(
            workflowExecutionId: workflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: _now,
            requestedBy: "runtime-test",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox);

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
            descriptorType: ProbeActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(descriptor),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

        return new(
            identity: new WorkflowExecutableIdentity("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test"),
            nodes: [node],
            edges: [],
            startNodeIds: ["node-start"],
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: now,
            publishedAt: now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private sealed class ProbeActivityConstructor : IActivityConstructor<ProbeActivityDescriptor>
    {
        public static string DescriptorTypeKey => typeof(ProbeActivityDescriptor).FullName!;

        public string DescriptorType => DescriptorTypeKey;

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
        }
    }

    private sealed class InlineExecutionProbe
    {
        public string? Invocation { get; private set; }

        public void Record(string invocation) => Invocation = invocation;
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
