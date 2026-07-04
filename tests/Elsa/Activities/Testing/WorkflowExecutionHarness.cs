using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Serialization.Core;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

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
    private bool _activityTypesRegistered;

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
        // Register the loaded activity CLR types into the well-known type registry now, not at Build() time.
        // The CLR construction descriptor resolves an activity's stable alias back to its type through this
        // registry, and RegisterActivityTypesStartupTask discovers types by scanning the loaded assemblies. A
        // caller materializes its activity graph (the `typeof(ForActivity)`/`typeof(While)`/... in the node
        // builders that force those assemblies to load) *between* Build() and this call, so scanning at Build()
        // time races the AppDomain load order: an activity whose assembly a prior test happened to load already
        // resolves, one it did not faults with UnknownActivityTypeException. Running the (idempotent) scan here —
        // after the graph, and therefore every activity assembly it references, is loaded — makes construction
        // deterministic regardless of test order. Guarded on the registry being composed: only CLR-construction
        // tests add the SerializationFeature that registers it; probe-only graphs don't need it.
        EnsureActivityTypesRegistered();

        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await _provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest());

        var dispatch = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity));
        if (dispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
            throw new InvalidOperationException($"Start command was not accepted (status: {dispatch.Status}). Reason: {dispatch.Reason}");

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

    // Runs RegisterActivityTypesStartupTask once, on first RunAsync. The real host runs it at startup after every
    // feature assembly is composed; the harness builds the provider directly (no startup-task runner) and its
    // caller loads the activity assemblies lazily via the graph builders, so it runs here — when they are all
    // loaded — rather than in Build(). Idempotent by design, but flagged so repeat RunAsync calls don't re-scan.
    private void EnsureActivityTypesRegistered()
    {
        if (_activityTypesRegistered)
            return;

        _activityTypesRegistered = true;

        if (_provider.GetService<IWellKnownTypeRegistry>() is { } typeRegistry)
            new RegisterActivityTypesStartupTask(
                    typeRegistry,
                    _provider.GetServices<IFeatureAssemblyProvider>(),
                    _provider,
                    NullLogger<RegisterActivityTypesStartupTask>.Instance)
                .ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
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

    /// <summary>Builds a leaf node that always faults during execution (see <see cref="FaultingActivity"/>).</summary>
    public static ExecutableNode NewFaultingNode(string nodeId, string? message = null) =>
        new(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: FaultingActivity.FaultingActivityType,
            activityTypeVersion: "1.0.0",
            descriptorType: FaultingActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new FaultingDescriptor(message ?? $"Branch '{nodeId}' faulted.")),
            inputBindings: new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: new Dictionary<string, string>());

    private static WorkflowExecutionActorActivationRequest NewActivationRequest() =>
        new(
            workflowExecutionId: WorkflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "activity-execution-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

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
        private bool _faultingRegistered;

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

        /// <summary>Registers the shared <see cref="FaultingActivityConstructor"/> so faulting leaf nodes can be constructed.</summary>
        public Builder WithFaultingLeaf()
        {
            _faultingRegistered = true;
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

            if (_faultingRegistered)
                services.AddSingleton<IActivityConstructor, FaultingActivityConstructor>();

            services.AddSingleton<IRuntimeExecutionIdGenerator>(new DeterministicRuntimeExecutionIdGenerator(activityExecutionIds));

            foreach (var configurator in _featureConfigurators)
                configurator(services);

            var provider = services.BuildServiceProvider();

            // Activity CLR types are registered into the well-known type registry lazily, on first RunAsync (see
            // EnsureActivityTypesRegistered), not here — the caller loads the activity assemblies via its graph
            // builders after Build() returns, so scanning them at Build() time would be order-dependent.
            return new WorkflowExecutionHarness(provider);
        }
    }
}
