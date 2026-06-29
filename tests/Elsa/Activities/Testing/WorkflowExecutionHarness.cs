using System.Text.Json;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Activities.Testing;

/// <summary>
/// Reusable execution-test harness for composite/leaf activities. Wires the in-process workflow
/// agent/scheduler over a deterministic id generator, runs an arbitrary <see cref="WorkflowExecutable"/>
/// (built from a small activity graph) to completion, and exposes the resulting
/// <see cref="ActivityExecutionState"/>s and workflow state for assertions.
///
/// Activity issues build their execution tests on this instead of re-deriving a fixture: register the
/// activity's feature and constructor via <see cref="Builder"/>, build a root node (use
/// <see cref="NewProbeNode"/> for leaf children), call <see cref="RunAsync"/>, then assert against the
/// returned <see cref="WorkflowExecutionRun"/>.
/// </summary>
public sealed class WorkflowExecutionHarness : IAsyncDisposable
{
    /// <summary>The deterministic workflow execution id every harness run uses.</summary>
    public const string WorkflowExecutionId = "wfexec-1";

    /// <summary>The deterministic executable artifact identity every harness run uses.</summary>
    public static readonly WorkflowExecutableIdentity Identity =
        new("artifact-1", "definition-1", "version-1", "1.0.0", "sha256:test");

    private static readonly DateTimeOffset Now = new(2026, 6, 12, 12, 0, 0, TimeSpan.Zero);

    private readonly ServiceProvider _provider;

    private WorkflowExecutionHarness(ServiceProvider provider) => _provider = provider;

    /// <summary>The configured service provider (escape hatch for advanced assertions).</summary>
    public IServiceProvider Services => _provider;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>Starts configuring a harness. Register the activity feature(s) and constructor(s), then call <see cref="Builder.Build"/>.</summary>
    public static Builder Create() => new();

    /// <summary>
    /// Saves the executable, starts the in-process agent, and drains the scheduler to completion.
    /// Returns a <see cref="WorkflowExecutionRun"/> exposing the persisted activity/workflow state.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false);

    /// <summary>
    /// Saves the executable, starts the in-process agent, and drains the scheduler.
    /// </summary>
    /// <param name="allowPendingWorkOnTerminalCompletion">
    /// When <c>false</c> (the default) the run requires the scheduler to drain to an empty queue. When
    /// <c>true</c>, queued work left behind is tolerated <em>only</em> if the workflow reached a terminal
    /// status — the #293 contract: a <c>Finish</c> inside a parallel fork terminates the run and the drainer
    /// intentionally abandons the already-queued sibling work rather than dispatching post-completion state.
    /// </param>
    public async Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable, bool allowPendingWorkOnTerminalCompletion)
    {
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await _provider.GetRequiredService<IWorkflowExecutionAgentProvider>()
            .GetAgentAsync(NewActivationRequest());

        var dispatch = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
        if (dispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
            throw new InvalidOperationException($"Start command was not accepted (status: {dispatch.Status}).");

        var states = await _provider.GetRequiredService<IActivityExecutionStateStore>().ListAsync(WorkflowExecutionId);
        var workflowState = await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(WorkflowExecutionId);

        var pending = await _provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAsync(new RuntimeSchedulerWorkQuery(WorkflowExecutionId));
        if (pending.Count != 0)
        {
            var terminated = workflowState?.Status is WorkflowExecutionStatus.Completed
                or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled;
            if (!allowPendingWorkOnTerminalCompletion || !terminated)
                throw new InvalidOperationException($"Scheduler did not drain to completion ({pending.Count} work item(s) remain).");
        }

        return new WorkflowExecutionRun(states, workflowState);
    }

    /// <summary>Convenience: wrap a root node in a <see cref="WorkflowExecutable"/> with the harness identity.</summary>
    public static WorkflowExecutable NewExecutable(ExecutableNode rootActivity) =>
        new(
            identity: Identity,
            rootActivity: rootActivity,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            publishedAt: Now,
            compatibilityMetadata: new Dictionary<string, string>());

    /// <summary>Builds a leaf probe node that records execution and emits the given outcomes (default <c>Done</c>).</summary>
    public static ExecutableNode NewProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: ProbeActivity.ProbeActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: ProbeActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new ProbeDescriptor(outcomes ?? [ActivityOutcomes.Done])),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutionAgentActivationRequest NewActivationRequest() =>
        new(
            workflowExecutionId: WorkflowExecutionId,
            reason: WorkflowExecutionAgentActivationReason.Start,
            requestedAt: Now,
            requestedBy: "activity-execution-test",
            requiredCapabilities: WorkflowExecutionAgentCapabilities.InProcessMailbox);

    private static WorkflowExecutionCommandEnvelope NewStartEnvelope(WorkflowExecutableIdentity pinnedExecutable)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: WorkflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: WorkflowExecutionId,
            command: command,
            idempotencyKey: $"{WorkflowExecutionId}:start:{pinnedExecutable.ArtifactId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    /// <summary>Fluent configuration for a <see cref="WorkflowExecutionHarness"/>.</summary>
    public sealed class Builder
    {
        private readonly List<Action<IServiceCollection>> _featureConfigurators = [];
        private readonly List<Action<IServiceCollection>> _constructorRegistrations = [];
        private bool _probeRegistered;

        internal Builder()
        {
            // The runtime stack and probe leaf are always needed.
            _featureConfigurators.Add(services => new WorkflowsRuntimeApiFeature().ConfigureServices(services));
            _featureConfigurators.Add(services => new ActivitiesRuntimeFeature().ConfigureServices(services));
        }

        /// <summary>Registers an activity feature whose <c>ConfigureServices</c> wires the activity under test.</summary>
        public Builder WithFeature(Action<IServiceCollection> configureFeature)
        {
            _featureConfigurators.Add(configureFeature);
            return this;
        }

        /// <summary>Registers an <see cref="IActivityConstructor"/> for the activity under test.</summary>
        public Builder WithConstructor<TConstructor>() where TConstructor : class, IActivityConstructor
        {
            _constructorRegistrations.Add(services => services.AddSingleton<IActivityConstructor, TConstructor>());
            return this;
        }

        /// <summary>Registers a pre-built <see cref="IActivityConstructor"/> instance for the activity under test.</summary>
        public Builder WithConstructor(IActivityConstructor constructor)
        {
            _constructorRegistrations.Add(services => services.AddSingleton(constructor));
            return this;
        }

        /// <summary>Registers the shared <see cref="ProbeActivityConstructor"/> so leaf probe nodes can be constructed.</summary>
        public Builder WithProbeLeaf()
        {
            _probeRegistered = true;
            return this;
        }

        /// <summary>Escape hatch for additional service overrides (custom id generator, stubs, etc.).</summary>
        public Builder ConfigureServices(Action<IServiceCollection> configure)
        {
            _featureConfigurators.Add(configure);
            return this;
        }

        /// <summary>
        /// Builds the harness with a deterministic id generator that hands out the supplied
        /// activity-execution ids in order (used to assert parent/scheduling identity).
        /// </summary>
        public WorkflowExecutionHarness Build(params string[] activityExecutionIds) =>
            Build((IEnumerable<string>)activityExecutionIds);

        /// <inheritdoc cref="Build(string[])"/>
        public WorkflowExecutionHarness Build(IEnumerable<string> activityExecutionIds)
        {
            var services = new ServiceCollection();

            foreach (var registration in _constructorRegistrations)
                registration(services);

            if (_probeRegistered)
                services.AddSingleton<IActivityConstructor, ProbeActivityConstructor>();

            services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));

            foreach (var configurator in _featureConfigurators)
                configurator(services);

            return new WorkflowExecutionHarness(services.BuildServiceProvider());
        }
    }
}
