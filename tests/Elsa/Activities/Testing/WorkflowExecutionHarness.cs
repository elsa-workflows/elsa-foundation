using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using CShells.Features;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Runtime.Tasks;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Api.Coalescing;
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
/// activity's feature via <see cref="Builder"/>, build a root node (use
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

    /// <summary>The fixed timestamp every harness run stamps, for callers that build their own executables.</summary>
    public static DateTimeOffset Timestamp => Now;

    private readonly ServiceProvider _provider;
    private readonly string _workflowExecutionId;
    private readonly WorkflowExecutableIdentity _identity;
    private bool _activityTypesRegistered;

    private WorkflowExecutionHarness(ServiceProvider provider, string workflowExecutionId, WorkflowExecutableIdentity identity)
    {
        _provider = provider;
        _workflowExecutionId = workflowExecutionId;
        _identity = identity;
    }

    /// <summary>The configured service provider (escape hatch for advanced assertions).</summary>
    public IServiceProvider Services => _provider;

    /// <summary>The workflow-execution id this harness drives (defaults to <see cref="WorkflowExecutionId"/>).</summary>
    public string ExecutionId => _workflowExecutionId;

    /// <summary>The executable identity this harness saves and starts (defaults to <see cref="Identity"/>).</summary>
    public WorkflowExecutableIdentity ExecutableIdentity => _identity;

    /// <summary>
    /// Runs the harness's CLR activity-type registration pass without starting a workflow. Restart tests use
    /// this to model normal host startup before a durable timer or resume backlog activates an existing run.
    /// </summary>
    public void InitializeActivityTypes() => EnsureActivityTypesRegistered();

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    /// <summary>Starts configuring a harness. Register the activity feature(s), then call <see cref="Builder.Build"/>.</summary>
    public static Builder Create() => new();

    /// <summary>
    /// Saves the executable, starts the in-process agent, and drains the scheduler to completion.
    /// Returns a <see cref="WorkflowExecutionRun"/> exposing the persisted activity/workflow state.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false);

    /// <summary>
    /// Runs with workflow inputs seeded on the start command (name → JSON value), mirroring how the start
    /// dispatcher carries caller inputs into the workflow-started checkpoint.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable, IReadOnlyDictionary<string, JsonElement> inputs) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false, inputs: inputs);

    /// <summary>
    /// Runs with a stimulus payload on the start command's dedicated stimulus-input channel (spec 089 A),
    /// mirroring how the stimulus router's start path delivers a trigger's payload.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable, JsonElement stimulusInput) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false, stimulusInput: stimulusInput);

    /// <summary>
    /// Runs with a stimulus payload AND the matched trigger's node id on the start command's dedicated channels
    /// (spec 089 A/D), mirroring how the stimulus router's start path delivers the payload and the trigger-node
    /// identity for a trigger-started run. Lets an activity that plays both start-trigger and mid-flow roles
    /// (e.g. <c>HttpEndpoint</c>) tell whether it is the node that triggered the run.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsync(WorkflowExecutable executable, JsonElement stimulusInput, string triggerNodeId) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false, stimulusInput: stimulusInput, triggerNodeId: triggerNodeId);

    /// <summary>
    /// Runs as a trigger-started run carrying the matched trigger binding's node id and metadata map on the start
    /// command's reserved channels (spec 117 D4), mirroring how the stimulus router forwards <c>binding.Metadata</c>
    /// so a structural trigger activity (e.g. <c>BpmnProcess</c>) can resolve which start element the delivery
    /// targeted. Seed only the metadata a real binding would carry — it is spoof-proof state, not user input.
    /// </summary>
    public Task<WorkflowExecutionRun> RunAsTriggerDeliveryAsync(
        WorkflowExecutable executable,
        string triggerNodeId,
        IReadOnlyDictionary<string, string> triggerMetadata) =>
        RunAsync(executable, allowPendingWorkOnTerminalCompletion: false, triggerNodeId: triggerNodeId, triggerMetadata: triggerMetadata);

    /// <summary>
    /// Saves the executable, starts the in-process agent, and drains the scheduler.
    /// </summary>
    /// <param name="allowPendingWorkOnTerminalCompletion">
    /// When <c>false</c> (the default) the run requires the scheduler to drain to an empty queue. When
    /// <c>true</c>, queued work left behind is tolerated <em>only</em> if the workflow reached a terminal
    /// status — the #293 contract: a <c>Finish</c> inside a parallel fork terminates the run and the drainer
    /// intentionally abandons the already-queued sibling work rather than dispatching post-completion state.
    /// </param>
    public async Task<WorkflowExecutionRun> RunAsync(
        WorkflowExecutable executable,
        bool allowPendingWorkOnTerminalCompletion,
        IReadOnlyDictionary<string, JsonElement>? inputs = null,
        JsonElement? stimulusInput = null,
        string? triggerNodeId = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
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

        executable = PinClrActivityContracts(executable);
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);
        var agent = await _provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest());

        var dispatch = await agent.EnqueueAsync(NewStartEnvelope(executable.Identity, inputs, stimulusInput, triggerNodeId, triggerMetadata));
        if (dispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
            throw new InvalidOperationException($"Start command was not accepted (status: {dispatch.Status}). Reason: {dispatch.Reason}");

        var states = await _provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(_workflowExecutionId);
        var workflowState = await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(_workflowExecutionId);

        var pending = await _provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
            .ListAllAsync(new RuntimeSchedulerWorkQuery(_workflowExecutionId));
        if (pending.Count != 0)
        {
            var terminated = workflowState?.Status is WorkflowExecutionStatus.Completed
                or WorkflowExecutionStatus.Faulted or WorkflowExecutionStatus.Cancelled;
            if (!allowPendingWorkOnTerminalCompletion || !terminated)
                throw new InvalidOperationException($"Scheduler did not drain to completion ({pending.Count} work item(s) remain).");
        }

        return new WorkflowExecutionRun(states, workflowState);
    }

    /// <summary>
    /// Dispatches a <c>ResumeBookmark</c> command against the same in-process agent and drains it, mirroring how a
    /// matched stimulus resumes a waiting bookmark. Use after a <see cref="RunAsync"/> that suspended: the executable
    /// and durable state persist on the shared provider, so start → suspend → resume is a real round-trip. The
    /// resume input rides the dedicated resume-input channel (spec 089 D).
    /// </summary>
    public async Task<WorkflowExecutionRun> ResumeAsync(
        WorkflowExecutableIdentity pinnedExecutable,
        string bookmarkId,
        string activityExecutionId,
        string executableNodeId,
        string resumeTargetId,
        string stimulusType,
        string stimulusHash,
        JsonElement? input = null,
        RuntimeTypedTriggerDeliveryMetadata? triggerDelivery = null)
    {
        var agent = await _provider.GetRequiredService<IWorkflowExecutionActorProvider>()
            .GetAgentAsync(NewActivationRequest());

        var payload = new RuntimeResumeBookmarkCommandPayload(
            pinnedExecutable: pinnedExecutable,
            bookmarkId: bookmarkId,
            activityExecutionId: activityExecutionId,
            executableNodeId: executableNodeId,
            resumeTargetId: resumeTargetId,
            stimulusType: stimulusType,
            stimulusHash: stimulusHash,
            input: input,
            reason: RuntimeResumeBookmarkCommandPayload.StimulusMatchedReason,
            triggerDelivery: triggerDelivery);

        var command = new WorkflowExecutionCommand(
            CommandId: "command-resume",
            WorkflowExecutionId: _workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.ResumeBookmark,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        var envelope = new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-resume",
            workflowExecutionId: _workflowExecutionId,
            command: command,
            idempotencyKey: $"{_workflowExecutionId}:resume:{bookmarkId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 2,
            metadata: new Dictionary<string, string>());

        var dispatch = await agent.EnqueueAsync(envelope);
        if (dispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
            throw new InvalidOperationException($"Resume command was not accepted (status: {dispatch.Status}). Reason: {dispatch.Reason}");

        var states = await _provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(_workflowExecutionId);
        var workflowState = await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(_workflowExecutionId);
        return new WorkflowExecutionRun(states, workflowState);
    }

    /// <summary>The durable partition every <see cref="StartPublishedAsync"/> run pins.</summary>
    public static readonly WorkflowExecutionPartition Partition = new("partition-test");

    /// <summary>
    /// Saves an executable together with the live Published source reference the start dispatcher gates on
    /// (ADR 0040), returning the reference so it can be started through <see cref="StartPublishedAsync"/>.
    /// Applies the same CLR contract pinning <see cref="RunAsync(WorkflowExecutable)"/> does, so a hand-built
    /// graph of CLR activities needs no contract of its own.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="RunAsync(WorkflowExecutable)"/>, this does not assume one executable per harness: a
    /// drive that needs a second workflow (a dispatch target, a reusable-activity definition) publishes each
    /// one and starts only the root.
    /// </remarks>
    public async Task<WorkflowExecutableSourceReference> PublishAsync(WorkflowExecutable executable, string sourceReferenceId)
    {
        EnsureActivityTypesRegistered();
        executable = PinClrActivityContracts(executable);
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(executable);

        var reference = new WorkflowExecutableSourceReference(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: executable.Identity.ArtifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: executable.Identity.DefinitionVersionId,
            SourceVersion: executable.Identity.ArtifactVersion,
            DefinitionId: executable.Identity.DefinitionId,
            DefinitionVersionId: executable.Identity.DefinitionVersionId,
            ArtifactVersion: executable.Identity.ArtifactVersion,
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            ActivationId: $"publication-{sourceReferenceId}",
            SlotId: $"slot-{sourceReferenceId}");
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(reference);
        return reference;
    }

    /// <summary>
    /// Retires a published source reference, so a later start gated on a live Published reference is refused.
    /// Models an author unpublishing a workflow between something committing an intent to run it and the intent
    /// being delivered.
    /// </summary>
    public async Task RetirePublicationAsync(WorkflowExecutableSourceReference reference, string reason)
    {
        var retired = await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>()
            .RetireAsync(reference.SourceReferenceId, Now.AddMinutes(1), reason);
        if (!retired)
            throw new InvalidOperationException($"Source reference '{reference.SourceReferenceId}' could not be retired.");
    }

    /// <summary>
    /// Starts a published executable through the real <see cref="IWorkflowStartDispatcher"/> and drains it.
    /// </summary>
    /// <remarks>
    /// <see cref="RunAsync(WorkflowExecutable)"/> hand-builds its start envelope and therefore seeds no durable
    /// partition, authority or source provenance on the workflow state. An activity that reads its <em>parent
    /// execution's</em> durable context rather than only its own inputs — anything that starts or addresses
    /// another workflow — cannot run on that path at all. This is the same route the production start API takes,
    /// so those snapshots are present and the run is admission-gated on a live Published reference.
    /// Throws when the command is not accepted, matching <see cref="RunAsync(WorkflowExecutable)"/> — a start the
    /// value-flow guard refuses surfaces as a rejection naming the offending input, not a silent no-op.
    /// </remarks>
    public async Task<WorkflowExecutionStartDispatchResult> StartPublishedAsync(
        WorkflowExecutableSourceReference reference,
        string workflowExecutionId,
        string? correlationId = null,
        string? tenantId = null)
    {
        EnsureActivityTypesRegistered();
        var authority = new WorkflowExecutionAuthoritySnapshot(
            systemIdentity: "activity-behavioural-drive",
            rootInitiator: "activity-behavioural-drive");

        await using var scope = _provider.CreateAsyncScope();
        var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: reference.ArtifactId,
                requestedBy: authority.SystemIdentity,
                workflowExecutionId: workflowExecutionId,
                idempotencyKey: $"start:{workflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.PublishedRun,
                sourceSelection: new WorkflowExecutableSourceSelection(reference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: correlationId,
                tenantId: tenantId,
                partition: Partition,
                authority: authority));

        if (start.CommandDispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
        {
            throw new InvalidOperationException(
                $"Start command was not accepted (status: {start.CommandDispatch.Status}). Reason: {start.CommandDispatch.Reason}");
        }

        return start;
    }

    /// <summary>
    /// Runs one resumption sweep: re-delivers due post-commit outbox items and re-drives stranded scheduler work.
    /// Requires the caller to have registered the resumption feature via <see cref="Builder.WithFeature"/>.
    /// </summary>
    /// <remarks>
    /// The sweep is how a post-commit intent — the durable "do this after the checkpoint lands" record — actually
    /// gets delivered. A drive whose activity stages an intent rather than acting inline has not observed anything
    /// until the sweep runs, and each sweep delivers one generation of intents, so reaching an outcome that is two
    /// intents deep (stage → child runs → resume parent) takes two sweeps.
    /// </remarks>
    public async Task<RuntimeResumptionSweepResult> SweepAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
            .SweepAsync(new RuntimeResumptionSweepRequest());
    }

    /// <summary>
    /// Sweeps repeatedly until a sweep finds nothing due, so a chain of post-commit intents — each one committed
    /// by the delivery of the previous — runs to quiescence. Bounded, and it throws rather than looping forever:
    /// a chain that never settles is a defect to surface, not a hang to wait out.
    /// </summary>
    /// <remarks>
    /// The loop condition is <em>attempted</em>, not delivered. A failed delivery is still progress: exhausting a
    /// child start is exactly what commits the intent that reports the failure back, so stopping at the first
    /// sweep that delivers nothing would abandon the chain one step before its conclusion.
    /// </remarks>
    public async Task<int> SweepUntilQuietAsync(int maxSweeps = 8)
    {
        for (var sweep = 1; sweep <= maxSweeps; sweep++)
            if ((await SweepAsync()).OutboxAttemptedCount == 0)
                return sweep;

        throw new InvalidOperationException($"Resumption sweeps still found due work after {maxSweeps} passes.");
    }

    /// <summary>
    /// Delivers one Cancel command to a running execution through its agent — the control-plane path an operator
    /// (or a parent cancelling its child) takes — and drains it.
    /// </summary>
    /// <remarks>
    /// Activates the agent on <see cref="Partition"/>, so this pairs with executions started by
    /// <see cref="StartPublishedAsync"/> (or dispatched beneath one). A run started by
    /// <see cref="RunAsync(WorkflowExecutable)"/> carries no partition at all and is not what this targets.
    /// </remarks>
    public async Task CancelAsync(string workflowExecutionId)
    {
        var agent = await _provider.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(
            new WorkflowExecutionActorActivationRequest(
                workflowExecutionId: workflowExecutionId,
                reason: WorkflowExecutionActorActivationReason.ControlPlaneCommand,
                requestedAt: Now,
                requestedBy: "activity-behavioural-drive",
                requiredCapabilities: WorkflowExecutionActorCapabilities.None,
                partition: Partition));

        var command = new WorkflowExecutionCommand(
            CommandId: $"command-cancel-{workflowExecutionId}",
            WorkflowExecutionId: workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Cancel,
            EnqueuedAt: Now,
            Payload: null,
            Metadata: new Dictionary<string, string>());

        var dispatch = await agent.EnqueueAsync(new WorkflowExecutionCommandEnvelope(
            envelopeId: $"envelope-cancel-{workflowExecutionId}",
            workflowExecutionId: workflowExecutionId,
            command: command,
            idempotencyKey: $"{workflowExecutionId}:cancel",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            metadata: new Dictionary<string, string>(),
            partition: Partition));
        if (dispatch.Status != WorkflowExecutionCommandDispatchStatus.Accepted)
            throw new InvalidOperationException($"Cancel command was not accepted (status: {dispatch.Status}). Reason: {dispatch.Reason}");
    }

    /// <summary>Reads the persisted run of any workflow execution these stores know about.</summary>
    public async Task<WorkflowExecutionRun> ReadRunAsync(string workflowExecutionId)
    {
        var states = await _provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId);
        var workflowState = await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId);
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

    /// <summary>Convenience: wrap a root node in a <see cref="WorkflowExecutable"/> with the default harness identity.</summary>
    public static WorkflowExecutable NewExecutable(ExecutableNode rootActivity) =>
        NewExecutable(rootActivity, Identity);

    /// <summary>
    /// Convenience: wrap a root node in a <see cref="WorkflowExecutable"/> with the default harness identity and
    /// the given workflow-scope variable declarations (seeded into the root frame at execution start, #972).
    /// </summary>
    public static WorkflowExecutable NewExecutable(ExecutableNode rootActivity, params RuntimeVariableDeclaration[] workflowVariables) =>
        NewExecutable(rootActivity, Identity, workflowVariables);

    /// <summary>
    /// Wraps a root node in a <see cref="WorkflowExecutable"/> with a caller-supplied identity. Concurrency harnesses
    /// that run many executions against one shared store use this to give each execution a distinct artifact identity
    /// (the executable store keys on <see cref="WorkflowExecutableIdentity.ArtifactId"/>).
    /// </summary>
    public static WorkflowExecutable NewExecutable(
        ExecutableNode rootActivity,
        WorkflowExecutableIdentity identity,
        params RuntimeVariableDeclaration[] workflowVariables) =>
        new(
            identity: identity,
            rootActivity: rootActivity,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies: null,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference,
            checkpointCadence: null,
            workflowVariables: workflowVariables);

    /// <summary>Builds a leaf probe node that records execution and emits the given outcomes (default <c>Done</c>).</summary>
    public static ExecutableNode NewProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        NewProbeNode(nodeId, typeof(ProbeActivity), nameof(ProbeActivity.Outcomes), outcomes);

    /// <summary>
    /// Builds a leaf probe node whose pinned contract declares <see cref="SideEffectProfile.ReplaySafe"/> (spec 123).
    /// Behaviorally identical to <see cref="NewProbeNode(string, IReadOnlyCollection{string})"/>, but eligible for the
    /// ReplaySafe hop-fusion pass so fusion guardrails can drive a shape whose leaves actually resolve ReplaySafe.
    /// </summary>
    public static ExecutableNode NewReplaySafeProbeNode(string nodeId, IReadOnlyCollection<string>? outcomes = null) =>
        NewProbeNode(nodeId, typeof(ReplaySafeProbeActivity), nameof(ReplaySafeProbeActivity.Outcomes), outcomes);

    private static ExecutableNode NewProbeNode(
        string nodeId,
        Type activityType,
        string outcomesInputKey,
        IReadOnlyCollection<string>? outcomes)
    {
        var selectedOutcomes = outcomes ?? [ActivityOutcomes.Done];
        if (selectedOutcomes.Count != 1)
            throw new ArgumentException("A probe invocation must complete with exactly one outcome.", nameof(outcomes));

        var descriptor = NewClrDescriptor(activityType);
        var inputType = NewValueType(typeof(IReadOnlyCollection<string>));
        var binding = NewLiteralBinding(outcomesInputKey, inputType, selectedOutcomes);
        var contract = NewActivityContract(
            activityType,
            descriptor,
            [new ActivityInputContract(outcomesInputKey, outcomesInputKey, inputType, true, false, false, null, ActivityValuePolicy.Default)],
            selectedOutcomes);

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: descriptor.Kind,
            descriptorPayload: descriptor.Payload,
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    /// <summary>Builds a leaf node that always faults during execution (see <see cref="FaultingActivity"/>).</summary>
    public static ExecutableNode NewFaultingNode(string nodeId, string? message = null)
    {
        var activityType = typeof(FaultingActivity);
        var descriptor = NewClrDescriptor(activityType);
        var inputType = NewValueType(typeof(string));
        var binding = NewLiteralBinding(nameof(FaultingActivity.Message), inputType, message ?? $"Branch '{nodeId}' faulted.");
        var contract = NewActivityContract(
            activityType,
            descriptor,
            [new ActivityInputContract(nameof(FaultingActivity.Message), nameof(FaultingActivity.Message), inputType, true, false, false, null, ActivityValuePolicy.Default)],
            [ActivityOutcomes.Done]);

        return new ExecutableNode(
            executableNodeId: nodeId,
            authoredActivityId: $"authored-{nodeId}",
            activityType: activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptorType: descriptor.Kind,
            descriptorPayload: descriptor.Payload,
            inputBindings: new Dictionary<string, RuntimeInputBinding> { [binding.InputKey] = binding },
            metadata: new Dictionary<string, string>(),
            activityContract: contract);
    }

    private WorkflowExecutable PinClrActivityContracts(WorkflowExecutable executable) =>
        new(
            executable.Identity,
            PinClrActivityContract(executable.RootActivity),
            executable.ResumeTargets,
            executable.CreatedAt,
            executable.CompatibilityMetadata,
            inputContract: executable.InputContract,
            dependencies: executable.Dependencies,
            runtimeRequirements: null,
            storageDriverRequirements: null,
            incidentStrategy: executable.IncidentStrategy,
            checkpointCadence: executable.CheckpointCadence,
            workflowVariables: executable.WorkflowVariables);

    private ExecutableNode PinClrActivityContract(ExecutableNode node)
    {
        var childSlots = node.ChildSlots
            .Select(slot => new ExecutableChildSlot(slot.Name, slot.Activities.Select(PinClrActivityContract).ToArray()))
            .ToArray();

        if (node.IntrinsicKind is not null || node.ActivityContract is not null)
            return CopyNode(node, childSlots, node.DescriptorPayload, node.InputBindings, node.ActivityContract);

        var activityType = ResolveActivityType(node.ActivityType);
        var descriptor = NewClrDescriptor(activityType);
        var reflectedInputs = activityType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<ActivityInputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .ToArray();
        var inputs = reflectedInputs.Select(candidate =>
        {
            var key = candidate.Attribute!.Key ?? candidate.Property.Name;
            return new ActivityInputContract(
                key,
                candidate.Property.Name,
                NewValueType(candidate.Property.PropertyType),
                candidate.Property.GetCustomAttribute<RequiredAttribute>(inherit: true) is not null,
                IsNullable(candidate.Property),
                false,
                null,
                ActivityValuePolicy.Default);
        }).ToArray();
        var bindings = reflectedInputs.ToDictionary(
            candidate => candidate.Attribute!.Key ?? candidate.Property.Name,
            candidate => NormalizeBinding(node, candidate.Property, candidate.Attribute!),
            StringComparer.Ordinal);
        var outcomes = ResolveOutcomes(node, activityType);
        var contract = NewActivityContract(activityType, descriptor, inputs, outcomes);

        return CopyNode(node, childSlots, descriptor.Payload, bindings, contract);
    }

    private static bool IsNullable(PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true;
        if (property.PropertyType.IsValueType)
            return false;
        return new NullabilityInfoContext().Create(property).ReadState is not NullabilityState.NotNull;
    }

    private static ExecutableNode CopyNode(
        ExecutableNode node,
        IReadOnlyCollection<ExecutableChildSlot> childSlots,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding> inputBindings,
        ActivityContract? contract)
    {
        var consumerKey = StringComparer.Ordinal.Equals(
            contract?.DescriptorKind,
            typeof(ClrActivityDescriptor).FullName)
                ? WellKnownRuntimeActivityConsumers.ClrActivity
                : node.DescriptorType;
        return new(
            node.ExecutableNodeId,
            node.AuthoredActivityId,
            node.ActivityType,
            node.ActivityTypeVersion,
            new RuntimeActivityDescriptor(consumerKey, node.DescriptorSchemaVersion, descriptorPayload),
            inputBindings,
            node.OutputCaptures,
            node.Metadata,
            childSlots,
            node.Structure,
            contract,
            node.IntrinsicKind,
            node.IntrinsicVariable);
    }

    private static Type ResolveActivityType(string activityType)
    {
        var matches = AppDomain.CurrentDomain.GetAssemblies()
            .SelectMany(GetLoadableTypes)
            .Where(type => !type.IsAbstract && typeof(IActivity).IsAssignableFrom(type))
            .Where(type => StringComparer.Ordinal.Equals(type.FullName, activityType) ||
                           StringComparer.Ordinal.Equals(ActivityTypeMetadata.GetDeclaredActivityType(type), activityType))
            .Distinct()
            .ToArray();

        return matches.Length switch
        {
            1 => matches[0],
            0 => throw new InvalidOperationException($"No loaded CLR activity type matches executable activity type '{activityType}'."),
            _ => throw new InvalidOperationException($"Executable activity type '{activityType}' matches more than one loaded CLR activity type.")
        };
    }

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
    }

    private static RuntimeInputBinding NormalizeBinding(ExecutableNode node, PropertyInfo property, ActivityInputAttribute attribute)
    {
        var key = attribute.Key ?? property.Name;
        var type = NewValueType(property.PropertyType);
        if (!node.InputBindings.TryGetValue(key, out var binding) &&
            !node.InputBindings.TryGetValue(property.Name, out binding))
            return NewLiteralBinding(key, type, DefaultInputValue(property, attribute));

        return binding.Source switch
        {
            RuntimeInputBindingSource.Literal => NewLiteralBinding(key, type, LiteralValue(binding)),
            RuntimeInputBindingSource.Expression => new RuntimeInputBinding(
                key,
                type,
                ValueProtectionPolicy.InstanceInline,
                RuntimeInputBindingSource.Expression,
                expression: binding.Expression ?? throw new InvalidOperationException($"Input '{key}' has no expression payload."),
                metadata: binding.Metadata),
            RuntimeInputBindingSource.WorkflowRequest => new RuntimeInputBinding(
                key, type, ValueProtectionPolicy.InstanceInline, binding.Source, workflowRequest: binding.WorkflowRequest, metadata: binding.Metadata),
            RuntimeInputBindingSource.VariableRead => new RuntimeInputBinding(
                key, type, ValueProtectionPolicy.InstanceInline, binding.Source, variable: binding.Variable, metadata: binding.Metadata),
            RuntimeInputBindingSource.ActivityResult => new RuntimeInputBinding(
                key, type, ValueProtectionPolicy.InstanceInline, binding.Source, activityResult: binding.ActivityResult, metadata: binding.Metadata),
            _ => throw new InvalidOperationException(
                $"Test executable input '{key}' on node '{node.ExecutableNodeId}' still uses legacy binding source '{binding.Source}'.")
        };
    }

    private static object? LiteralValue(RuntimeInputBinding binding)
    {
        var payload = binding.Literal?.InlineValue ?? binding.LiteralValue;
        return payload is not { } value || value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
            ? null
            : value;
    }

    private static object? DefaultInputValue(PropertyInfo property, ActivityInputAttribute attribute)
    {
        if (attribute.DefaultValue is { } declared)
        {
            var converter = TypeDescriptor.GetConverter(property.PropertyType);
            if (converter.CanConvertFrom(typeof(string)))
                return converter.ConvertFromInvariantString(declared);
        }

        return property.PropertyType.IsValueType ? Activator.CreateInstance(property.PropertyType) : null;
    }

    private static IReadOnlyCollection<string> ResolveOutcomes(ExecutableNode node, Type activityType)
    {
        var outcomes = activityType.GetCustomAttributes<ActivityOutcomeAttribute>(inherit: true)
            .Select(attribute => attribute.Key)
            .ToHashSet(StringComparer.Ordinal);

        // Switch case outcomes are authored data, not a finite CLR type declaration. The publishing compiler
        // pins them from the executable structure; mirror that behavior for hand-built legacy test graphs.
        if (StringComparer.Ordinal.Equals(node.Structure?.Kind, "elsa.switch.structure") &&
            node.Structure.Payload.TryGetProperty("cases", out var cases))
        {
            foreach (var @case in cases.EnumerateArray())
                if (@case.TryGetProperty("match", out var match) && match.GetString() is { Length: > 0 } value)
                    outcomes.Add(value);
        }

        // spec 125: a BPMN transaction subprocess declares the additional "Cancelled" outcome (structure-dependent,
        // the same FlowSwitch/VF-ACT-006 pattern). Mirror ExecutableNodeCompiler.ResolveOutcomes for test graphs.
        if (StringComparer.Ordinal.Equals(node.Structure?.Kind, "elsa.bpmn.structure") &&
            node.Structure.Payload.TryGetProperty("isTransaction", out var isTransaction) &&
            isTransaction.ValueKind == JsonValueKind.True)
        {
            outcomes.Add("Cancelled");
        }

        if (outcomes.Count == 0)
            outcomes.Add(ActivityOutcomes.Done);
        return outcomes.ToArray();
    }

    private static RuntimeInputBinding NewLiteralBinding(string key, ValueTypeDescriptor type, object? value)
    {
        var envelope = value switch
        {
            null => ValueEnvelope.Null(type, ValueProtectionPolicy.InstanceInline),
            JsonElement element => ValueEnvelope.Inline(type, element, ValueProtectionPolicy.InstanceInline),
            _ => ValueEnvelope.Inline(type, JsonSerializer.SerializeToElement(value, value.GetType()), ValueProtectionPolicy.InstanceInline)
        };
        return new RuntimeInputBinding(
            key,
            type,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: envelope);
    }

    private static ActivityContract NewActivityContract(
        Type activityType,
        (string Kind, JsonElement Payload) descriptor,
        IEnumerable<ActivityInputContract> inputs,
        IReadOnlyCollection<string> outcomes)
    {
        var resultTypes = activityType.GetInterfaces()
            .Where(candidate => candidate.IsGenericType && candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .Select(candidate => candidate.GetGenericArguments()[0])
            .Distinct()
            .ToArray();
        if (resultTypes.Length != 1)
            throw new InvalidOperationException($"CLR activity type '{activityType.FullName}' must declare exactly one atomic result type.");

        var resultType = resultTypes[0];
        var projections = resultType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => (Property: property, Attribute: property.GetCustomAttribute<OutputAttribute>(inherit: true)))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => new ActivityResultProjectionContract(
                candidate.Attribute!.Key ?? candidate.Property.Name,
                candidate.Attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(candidate.Property.Name),
                NewValueType(candidate.Property.PropertyType),
                candidate.Attribute.IsRequired,
                ActivityValuePolicy.Default));
        var alias = TypeAliasConvention.CanonicalAlias(activityType);
        return new ActivityContract(
            alias,
            "1.0.0",
            descriptor.Kind,
            descriptor.Payload,
            inputs,
            new ActivityResultContract(NewValueType(resultType), true, ActivityValuePolicy.Default, projections),
            outcomes,
            new ActivityActivationRequirement(descriptor.Kind, alias),
            // Mirror ExecutableNodeCompiler.ResolveSideEffectProfile (ADR 0032 R1 / spec 107): unmarked ⇒ External.
            sideEffectProfile: activityType.GetCustomAttribute<ActivitySideEffectProfileAttribute>(inherit: true)?.Profile
                ?? SideEffectProfile.External);
    }

    private static (string Kind, JsonElement Payload) NewClrDescriptor(Type activityType)
    {
        var kind = typeof(ClrActivityDescriptor).FullName!;
        return (kind, JsonSerializer.SerializeToElement(new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType))));
    }

    private static ValueTypeDescriptor NewValueType(Type type)
    {
        var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
        return new ValueTypeDescriptor(reference.Alias, reference.CollectionKind);
    }

    private WorkflowExecutionActorActivationRequest NewActivationRequest() =>
        new(
            workflowExecutionId: _workflowExecutionId,
            reason: WorkflowExecutionActorActivationReason.Start,
            requestedAt: Now,
            requestedBy: "activity-execution-test",
            requiredCapabilities: WorkflowExecutionActorCapabilities.InProcessMailbox);

    private WorkflowExecutionCommandEnvelope NewStartEnvelope(
        WorkflowExecutableIdentity pinnedExecutable,
        IReadOnlyDictionary<string, JsonElement>? inputs = null,
        JsonElement? stimulusInput = null,
        string? triggerNodeId = null,
        IReadOnlyDictionary<string, string>? triggerMetadata = null)
    {
        var payload = new WorkflowExecutionStartCommandPayload(pinnedExecutable, pinnedExecutable.ArtifactId, inputs: inputs, stimulusInput: stimulusInput, triggerNodeId: triggerNodeId, triggerMetadata: triggerMetadata);
        var command = new WorkflowExecutionCommand(
            CommandId: "command-start",
            WorkflowExecutionId: _workflowExecutionId,
            Kind: WorkflowExecutionCommandKind.Start,
            EnqueuedAt: Now,
            Payload: JsonSerializer.SerializeToElement(payload),
            Metadata: new Dictionary<string, string>());

        return new WorkflowExecutionCommandEnvelope(
            envelopeId: "envelope-start",
            workflowExecutionId: _workflowExecutionId,
            command: command,
            idempotencyKey: $"{_workflowExecutionId}:start:{pinnedExecutable.ArtifactId}",
            deliveryMode: WorkflowExecutionCommandDeliveryMode.AtLeastOnce,
            enqueuedAt: Now,
            sequence: 1,
            metadata: new Dictionary<string, string>());
    }

    /// <summary>Fluent configuration for a <see cref="WorkflowExecutionHarness"/>.</summary>
    public sealed class Builder
    {
        private readonly List<Action<IServiceCollection>> _featureConfigurators = [];

        internal Builder()
        {
            // The runtime stack and probe leaf are always needed.
            _featureConfigurators.Add(services => new WorkflowsRuntimeApiFeature().ConfigureServices(services));
            _featureConfigurators.Add(services => new ActivitiesRuntimeFeature().ConfigureServices(services));
            _featureConfigurators.Add(services => new SerializationFeature().ConfigureServices(services));
            _featureConfigurators.Add(services => new ActivitiesPrimitivesFeature().ConfigureServices(services));
        }

        /// <summary>Registers an activity feature whose <c>ConfigureServices</c> wires the activity under test.</summary>
        public Builder WithFeature(Action<IServiceCollection> configureFeature)
        {
            _featureConfigurators.Add(configureFeature);
            return this;
        }

        /// <summary>
        /// Drives the harness under the opt-in burst-coalescing checkpoint persistence policy (ADR 0032 / spec 115)
        /// instead of the default Immediate cadence, so intra-drain checkpoints fold into one atomic commit at
        /// quiescence — the mode a ReplaySafe hop-fusion pass engages inside (spec 123 research §2). Applied after the
        /// runtime feature registration (which the ctor seeds first), so the coalescing decorators capture the runtime
        /// stores. Optionally caps the per-segment checkpoint count (models a mid-drain fold-and-flush).
        /// </summary>
        public Builder WithCoalescing(int? maxSegmentCheckpoints = null)
        {
            _featureConfigurators.Add(services => services.AddCoalescingRuntimeCheckpointPersistence(options =>
            {
                if (maxSegmentCheckpoints is { } cap)
                    options.MaxSegmentCheckpoints = cap;
            }));
            return this;
        }

        /// <summary>Includes the shared probe leaf in the graph. CLR activities are activated directly by the runtime.</summary>
        public Builder WithProbeLeaf()
        {
            return this;
        }

        /// <summary>Includes the shared faulting leaf in the graph. CLR activities are activated directly by the runtime.</summary>
        public Builder WithFaultingLeaf()
        {
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
        public WorkflowExecutionHarness Build(IEnumerable<string> activityExecutionIds) =>
            Build(Identity, WorkflowExecutionId, activityExecutionIds);

        /// <summary>
        /// Builds a harness that drives a caller-supplied workflow-execution id and executable identity. Concurrency
        /// harnesses stand up many of these against ONE shared document store — each with a distinct id/identity — to
        /// measure N concurrent executions contending on the single durable writer.
        /// </summary>
        public WorkflowExecutionHarness Build(
            WorkflowExecutableIdentity identity,
            string workflowExecutionId,
            IEnumerable<string> activityExecutionIds)
        {
            var services = new ServiceCollection();

            services.AddSingleton<IRuntimeExecutionIdGenerator>(
                new DeterministicRuntimeExecutionIdGenerator(workflowExecutionId, activityExecutionIds));

            foreach (var configurator in _featureConfigurators)
                configurator(services);

            var provider = services.BuildServiceProvider();

            // Activity CLR types are registered into the well-known type registry lazily, on first RunAsync (see
            // EnsureActivityTypesRegistered), not here — the caller loads the activity assemblies via its graph
            // builders after Build() returns, so scanning them at Build() time would be order-dependent.
            return new WorkflowExecutionHarness(provider, workflowExecutionId, identity);
        }
    }
}
