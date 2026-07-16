using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Abstractions;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Serialization.SystemText;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.Services;
using Elsa.Workflows.Runtime.Resumption;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using DispatchWorkflowActivity = Elsa.Activities.DispatchWorkflow.Runtime.Activities.DispatchWorkflow;

namespace Elsa.Activities.DispatchWorkflow.Tests;

internal sealed class DispatchWorkflowRuntimeTestFixture : IAsyncDisposable
{
    internal static readonly DateTimeOffset Now = new(2026, 7, 16, 12, 0, 0, TimeSpan.Zero);
    internal static readonly WorkflowExecutableIdentity ChildIdentity =
        new("artifact-child", "definition-child", "version-child", "3.2.1", "sha256:child");

    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly ServiceProvider _provider;

    private DispatchWorkflowRuntimeTestFixture(ServiceProvider provider) => _provider = provider;

    internal IServiceProvider Services => _provider;
    internal CheckpointRecorder Checkpoints => _provider.GetRequiredService<CheckpointRecorder>();
    internal RecordingWorkflowExecutionActorProvider Actors => _provider.GetRequiredService<RecordingWorkflowExecutionActorProvider>();
    internal ChildExecutionProbe ChildProbe => _provider.GetRequiredService<ChildExecutionProbe>();

    internal static async ValueTask<DispatchWorkflowRuntimeTestFixture> CreateAsync()
    {
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new FixedTimeProvider(Now));
        services.AddSingleton<CheckpointRecorder>();
        services.AddSingleton<ChildExecutionProbe>();
        services.AddSingleton<IActivityConstructor, DispatchActivityConstructor>();
        services.AddSingleton<IActivityConstructor, ChildProbeActivityConstructor>();

        new SerializationFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        new DispatchWorkflowRuntimeFeature().ConfigureServices(services);

        services.AddSingleton<InProcessWorkflowExecutionActorProvider>(serviceProvider =>
            new InProcessWorkflowExecutionActorProvider(serviceProvider.GetRequiredService<IWorkflowExecutionCommandExecutor>()));
        services.AddSingleton<RecordingWorkflowExecutionActorProvider>();
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingWorkflowExecutionActorProvider>()));
        services.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(serviceProvider =>
            new RecordingRuntimeCheckpointCommitStore(
                serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(),
                serviceProvider.GetRequiredService<CheckpointRecorder>())));

        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var fixture = new DispatchWorkflowRuntimeTestFixture(provider);
        await fixture.SeedChildAsync();
        return fixture;
    }

    internal async ValueTask<ParentDispatchRun> StartParentAsync(
        string caseId,
        string parentWorkflowExecutionId,
        string parentCorrelationId,
        string? correlationOverride = null)
    {
        var parentExecutable = NewParentExecutable(caseId, correlationOverride);
        var parentReference = NewSourceReference(
            sourceReferenceId: $"source-parent-{caseId}",
            identity: parentExecutable.Identity,
            publicationId: $"publication-parent-{caseId}",
            slotId: $"slot-parent-{caseId}");
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(parentExecutable);
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(parentReference);

        var authority = new WorkflowExecutionAuthoritySnapshot(
            systemIdentity: "parent-caller",
            rootInitiator: "root-initiator",
            metadata: new Dictionary<string, string> { ["authority.source"] = "root-request" });
        await using var scope = _provider.CreateAsyncScope();
        var start = await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
            new WorkflowExecutionStartDispatchRequest(
                artifactId: parentExecutable.Identity.ArtifactId,
                requestedBy: authority.SystemIdentity,
                workflowExecutionId: parentWorkflowExecutionId,
                idempotencyKey: $"start:{parentWorkflowExecutionId}",
                metadata: null,
                variables: null,
                inputs: null,
                stimulusInput: null,
                triggerNodeId: null,
                runKind: WorkflowRunKind.BackgroundWeaverRun,
                sourceSelection: new WorkflowExecutableSourceSelection(parentReference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: parentCorrelationId,
                tenantId: "tenant-42",
                partition: new WorkflowExecutionPartition("partition-eu"),
                authority: authority));

        var activityState = AssertSingle(await _provider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAsync(parentWorkflowExecutionId));
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, activityState.Execution.ActivityExecutionId);
        var dispatch = await _provider.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId)
            ?? throw new InvalidOperationException($"Dispatch '{identity.DispatchId}' was not persisted.");
        var commit = Checkpoints.SingleDispatchCommit(identity.DispatchId);
        return new ParentDispatchRun(start, activityState, identity, dispatch, commit);
    }

    internal async ValueTask<RuntimeResumptionSweepResult> SweepAsync()
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRuntimeResumptionService>()
            .SweepAsync(new RuntimeResumptionSweepRequest());
    }

    internal async ValueTask<RuntimeCheckpointCommitStoreResult> ReplayAsync(RuntimeCheckpointCommit commit)
    {
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IRuntimeCheckpointCommitStore>().CommitAsync(
            commit,
            new RuntimeCheckpointPersistenceDecision(RuntimeCheckpointPersistenceMode.Immediate));
    }

    internal async ValueTask<WorkflowExecutionState?> FindWorkflowAsync(string workflowExecutionId) =>
        await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(workflowExecutionId);

    internal async ValueTask<IReadOnlyCollection<DurableValueState>> ListDurableValuesAsync(string workflowExecutionId) =>
        await _provider.GetRequiredService<IDurableValueStateStore>().ListAsync(workflowExecutionId);

    internal async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListDispatchesAsync(string parentWorkflowExecutionId) =>
        await _provider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(parentWorkflowExecutionId);

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private async ValueTask SeedChildAsync()
    {
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewChildExecutable());
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(ChildSourceReference());
    }

    internal static WorkflowExecutableSourceReference ChildSourceReference() =>
        NewSourceReference("source-child", ChildIdentity, "publication-child", "slot-child");

    private static WorkflowExecutable NewChildExecutable() =>
        new(
            identity: ChildIdentity,
            rootActivity: NewNode(
                executableNodeId: "node-child-probe",
                activityType: "test/dispatch-child-probe",
                descriptorType: ChildProbeActivityConstructor.DescriptorTypeKey,
                descriptorPayload: JsonSerializer.SerializeToElement(new ChildProbeDescriptor())),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>());

    private static WorkflowExecutable NewParentExecutable(string caseId, string? correlationOverride)
    {
        var inputs = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                ChildIdentity.DefinitionId,
                typeof(string)),
            [nameof(DispatchWorkflowActivity.Inputs)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.Inputs),
                new Dictionary<string, object?>
                {
                    ["message"] = "hello child",
                    ["count"] = 7
                },
                typeof(IReadOnlyDictionary<string, object?>))
        };
        if (correlationOverride is not null)
        {
            inputs[nameof(DispatchWorkflowActivity.CorrelationId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.CorrelationId),
                correlationOverride,
                typeof(string));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(
                new DispatchWorkflowPin(ChildIdentity, WorkflowExecutableSourceProvenance.From(ChildSourceReference())),
                SerializerOptions)
        };
        var node = NewNode(
            executableNodeId: "node-dispatch",
            activityType: DispatchWorkflowConstants.ActivityType,
            descriptorType: DispatchActivityConstructor.DescriptorTypeKey,
            descriptorPayload: JsonSerializer.SerializeToElement(new DispatchActivityDescriptor()),
            inputBindings: inputs,
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>
            {
                [nameof(DispatchWorkflowActivity.ChildWorkflowExecutionId)] = new(
                    outputName: nameof(DispatchWorkflowActivity.ChildWorkflowExecutionId),
                    valueId: $"dispatch-child-id-{caseId}",
                    type: new RuntimeValueTypeDescriptor("clr", typeof(string).FullName, null),
                    lifecycle: DurableValueLifecycle.Instance,
                    storage: DurableValueStorage.Inline,
                    captureOnSuccessfulCompletion: true)
            },
            metadata: metadata);
        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                $"artifact-parent-{caseId}",
                $"definition-parent-{caseId}",
                $"version-parent-{caseId}",
                "1.0.0",
                $"sha256:parent-{caseId}"),
            rootActivity: node,
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>());
    }

    private static RuntimeInputBinding LiteralBinding(string name, object value, Type type) =>
        new(
            inputName: name,
            source: RuntimeInputBindingSource.Literal,
            literalValue: JsonSerializer.SerializeToElement(value, value.GetType()),
            metadata: new Dictionary<string, string>
            {
                [RuntimeActivityInputMaterializer.InputTypeMetadataKey] = type.AssemblyQualifiedName!
            });

    private static ExecutableNode NewNode(
        string executableNodeId,
        string activityType,
        string descriptorType,
        JsonElement descriptorPayload,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null,
        IReadOnlyDictionary<string, RuntimeOutputCapture>? outputCaptures = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new(
            executableNodeId: executableNodeId,
            authoredActivityId: $"authored-{executableNodeId}",
            activityType: activityType,
            activityTypeVersion: "1.0.0",
            descriptorType: descriptorType,
            descriptorPayload: descriptorPayload,
            inputBindings: inputBindings ?? new Dictionary<string, RuntimeInputBinding>(),
            outputCaptures: outputCaptures ?? new Dictionary<string, RuntimeOutputCapture>(),
            metadata: metadata ?? new Dictionary<string, string>());

    private static WorkflowExecutableSourceReference NewSourceReference(
        string sourceReferenceId,
        WorkflowExecutableIdentity identity,
        string publicationId,
        string slotId) =>
        new(
            SourceReferenceId: sourceReferenceId,
            ArtifactId: identity.ArtifactId,
            SourceKind: "WorkflowDefinitionVersion",
            SourceId: identity.DefinitionVersionId,
            SourceVersion: identity.ArtifactVersion,
            DefinitionId: identity.DefinitionId,
            DefinitionVersionId: identity.DefinitionVersionId,
            ArtifactVersion: identity.ArtifactVersion,
            CreatedAt: Now,
            PublishedAt: Now,
            Scope: WorkflowExecutableReferenceScope.Published,
            ExpiresAt: null,
            PublicationId: publicationId,
            SlotId: slotId);

    private static T AssertSingle<T>(IReadOnlyCollection<T> values) =>
        values.Count == 1
            ? values.Single()
            : throw new InvalidOperationException($"Expected one value but found {values.Count}.");

    private sealed class DispatchActivityConstructor : IActivityConstructor<DispatchActivityDescriptor>
    {
        internal static string DescriptorTypeKey => typeof(DispatchActivityDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new DispatchActivityDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            DispatchActivityDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken)
        {
            var activity = new DispatchWorkflowActivity();
            if (inputs is not null)
            {
                if (inputs.TryGetValue(nameof(activity.WorkflowDefinitionId), out var definitionId))
                    activity.WorkflowDefinitionId = (InputArgument<string>)definitionId;
                if (inputs.TryGetValue(nameof(activity.Inputs), out var dispatchInputs))
                    activity.Inputs = (InputArgument<IReadOnlyDictionary<string, object?>>)dispatchInputs;
                if (inputs.TryGetValue(nameof(activity.CorrelationId), out var correlationId))
                    activity.CorrelationId = (InputArgument<string>)correlationId;
            }

            if (outputs is not null && outputs.TryGetValue(nameof(activity.ChildWorkflowExecutionId), out var childId))
                activity.ChildWorkflowExecutionId = new OutputArgument<string>(childId.MemoryBlockReference());
            return new(activity);
        }
    }

    private sealed record DispatchActivityDescriptor;

    private sealed class ChildProbeActivityConstructor : IActivityConstructor<ChildProbeDescriptor>
    {
        internal static string DescriptorTypeKey => typeof(ChildProbeDescriptor).FullName!;
        public string DescriptorType => DescriptorTypeKey;

        public ValueTask<IActivity> Construct(
            JsonElement payload,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            Construct(new ChildProbeDescriptor(), inputs, outputs, cancellationToken);

        public ValueTask<IActivity> Construct(
            ChildProbeDescriptor descriptor,
            IDictionary<string, InputArgument>? inputs,
            IDictionary<string, OutputArgument>? outputs,
            CancellationToken cancellationToken) =>
            new(new ChildProbeActivity());
    }

    private sealed record ChildProbeDescriptor;

    private sealed class ChildProbeActivity : CodeActivity
    {
        public ChildProbeActivity() : base("test/dispatch-child-probe") { }

        protected override void Execute(IActivityExecutionContext context)
        {
            var runtimeContext = (IRuntimeActivityExecutionContext)context;
            var executionState = (IExecutionExpressionState)context.ExpressionExecutionContext;
            context.GetRequiredService<ChildExecutionProbe>().Record(
                runtimeContext.WorkflowExecutionId,
                runtimeContext.ActivityExecutionState.Execution.ActivityExecutionId,
                executionState.WorkflowInputs);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}

internal sealed record ParentDispatchRun(
    WorkflowExecutionStartDispatchResult Start,
    ActivityExecutionState Activity,
    WorkflowDispatchIdentity Identity,
    WorkflowDispatchRecord Dispatch,
    RuntimeCheckpointCommit CompletionCommit);

internal sealed class CheckpointRecorder
{
    private readonly Lock _gate = new();
    private readonly List<RuntimeCheckpointCommit> _commits = [];

    internal void Record(RuntimeCheckpointCommit commit)
    {
        lock (_gate)
            _commits.Add(commit);
    }

    internal RuntimeCheckpointCommit SingleDispatchCommit(string dispatchId)
    {
        lock (_gate)
            return _commits.Single(commit => commit.StateChanges.WorkflowDispatches.Any(change => change.StateId == dispatchId));
    }
}

internal sealed class RecordingRuntimeCheckpointCommitStore(
    InMemoryRuntimeCheckpointCommitStore inner,
    CheckpointRecorder recorder) : IRuntimeCheckpointCommitStore
{
    public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(commit);
        return inner.CommitAsync(commit, decision, cancellationToken);
    }
}

internal sealed class RecordingWorkflowExecutionActorProvider(
    InProcessWorkflowExecutionActorProvider inner) : IWorkflowExecutionActorProvider
{
    private readonly Lock _gate = new();
    private readonly List<WorkflowExecutionActorActivationRequest> _activations = [];
    private readonly List<WorkflowExecutionCommandEnvelope> _envelopes = [];

    internal IReadOnlyCollection<WorkflowExecutionActorActivationRequest> Activations
    {
        get { lock (_gate) return _activations.ToArray(); }
    }

    internal IReadOnlyCollection<WorkflowExecutionCommandEnvelope> Envelopes
    {
        get { lock (_gate) return _envelopes.ToArray(); }
    }

    public WorkflowExecutionActorCapabilities Capabilities => inner.Capabilities;

    public async ValueTask<IWorkflowExecutionActor> GetAgentAsync(
        WorkflowExecutionActorActivationRequest request,
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _activations.Add(request);
        var actor = await inner.GetAgentAsync(request, cancellationToken);
        return new RecordingWorkflowExecutionActor(actor, Record);
    }

    public ValueTask PassivateAsync(
        WorkflowExecutionActorPassivationRequest request,
        CancellationToken cancellationToken = default) =>
        inner.PassivateAsync(request, cancellationToken);

    private void Record(WorkflowExecutionCommandEnvelope envelope)
    {
        lock (_gate)
            _envelopes.Add(envelope);
    }

    private sealed class RecordingWorkflowExecutionActor(
        IWorkflowExecutionActor innerActor,
        Action<WorkflowExecutionCommandEnvelope> record) : IWorkflowExecutionActor
    {
        public WorkflowExecutionActorDescriptor Descriptor => innerActor.Descriptor;

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            CancellationToken cancellationToken = default)
        {
            record(envelope);
            return innerActor.EnqueueAsync(envelope, cancellationToken);
        }

        public ValueTask<WorkflowExecutionCommandDispatchResult> EnqueueAsync(
            WorkflowExecutionCommandEnvelope envelope,
            WorkflowExecutionCommandDispatchOptions options,
            CancellationToken cancellationToken = default)
        {
            record(envelope);
            return innerActor.EnqueueAsync(envelope, options, cancellationToken);
        }
    }
}

internal sealed record ChildExecutionObservation(
    string WorkflowExecutionId,
    string ActivityExecutionId,
    IReadOnlyDictionary<string, object?> WorkflowInputs);

internal sealed class ChildExecutionProbe
{
    private readonly Lock _gate = new();
    private readonly List<ChildExecutionObservation> _observations = [];

    internal IReadOnlyCollection<ChildExecutionObservation> Observations
    {
        get { lock (_gate) return _observations.ToArray(); }
    }

    internal void Record(
        string workflowExecutionId,
        string activityExecutionId,
        IReadOnlyDictionary<string, object?> workflowInputs)
    {
        lock (_gate)
        {
            _observations.Add(new ChildExecutionObservation(
                workflowExecutionId,
                activityExecutionId,
                workflowInputs.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)));
        }
    }
}
