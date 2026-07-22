using System.Text.Json;
using Elsa.Activities.DispatchWorkflow.Runtime;
using Elsa.Activities.DispatchWorkflow.Runtime.Configuration;
using Elsa.Activities.DispatchWorkflow.Runtime.Constants;
using Elsa.Activities.DispatchWorkflow.Runtime.Models;
using Elsa.Activities.Primitives;
using Elsa.Activities.Runtime;
using Elsa.Activities.Runtime.Core;
using Elsa.Activities.Runtime.Core.Attributes;
using Elsa.Activities.Runtime.Core.Contracts;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Primitives.Models;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Serialization.SystemText.Services;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Constants;
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
    private readonly AdjustableTimeProvider _timeProvider;

    private DispatchWorkflowRuntimeTestFixture(ServiceProvider provider, AdjustableTimeProvider timeProvider)
    {
        _provider = provider;
        _timeProvider = timeProvider;
    }

    internal IServiceProvider Services => _provider;
    internal CheckpointRecorder Checkpoints => _provider.GetRequiredService<CheckpointRecorder>();
    internal RecordingWorkflowExecutionActorProvider Actors => _provider.GetRequiredService<RecordingWorkflowExecutionActorProvider>();
    internal ChildExecutionProbe ChildProbe => _provider.GetRequiredService<ChildExecutionProbe>();
    internal RecordingDispatchBookmarkObserver BookmarkObserver => _provider.GetRequiredService<RecordingDispatchBookmarkObserver>();
    internal void AdvanceTime(TimeSpan duration) => _timeProvider.Advance(duration);

    internal static async ValueTask<DispatchWorkflowRuntimeTestFixture> CreateAsync(
        int maxNestingDepth = DispatchWorkflowOptions.DefaultMaxNestingDepth,
        bool failDispatchCommit = false)
    {
        var services = new ServiceCollection();
        var timeProvider = new AdjustableTimeProvider(Now);
        services.AddSingleton<TimeProvider>(timeProvider);
        services.AddSingleton<CheckpointRecorder>();
        services.AddSingleton<ChildExecutionProbe>();
        var typeRegistry = new WellKnownTypeRegistry();
        typeRegistry.RegisterType(typeof(string), "String");
        typeRegistry.RegisterType(typeof(int), "Int32");
        services.AddSingleton<IWellKnownTypeRegistry>(typeRegistry);
        services.AddSingleton<RecordingDispatchBookmarkObserver>();
        services.AddSingleton<IBookmarkLifecycleObserver>(serviceProvider => serviceProvider.GetRequiredService<RecordingDispatchBookmarkObserver>());
        new SerializationFeature().ConfigureServices(services);
        new WorkflowsRuntimeApiFeature().ConfigureServices(services);
        new ActivitiesRuntimeFeature().ConfigureServices(services);
        new ActivitiesPrimitivesFeature().ConfigureServices(services);
        new WorkflowsRuntimeResumptionFeature().ConfigureServices(services);
        new DispatchWorkflowRuntimeFeature { MaxNestingDepth = maxNestingDepth }.ConfigureServices(services);
        services.Replace(ServiceDescriptor.Singleton<IWellKnownTypeRegistry>(typeRegistry));

        services.AddSingleton<InProcessWorkflowExecutionActorProvider>(serviceProvider =>
            new InProcessWorkflowExecutionActorProvider(serviceProvider.GetRequiredService<IWorkflowExecutionCommandExecutor>()));
        services.AddSingleton<RecordingWorkflowExecutionActorProvider>();
        services.Replace(ServiceDescriptor.Singleton<IWorkflowExecutionActorProvider>(serviceProvider =>
            serviceProvider.GetRequiredService<RecordingWorkflowExecutionActorProvider>()));
        services.Replace(ServiceDescriptor.Scoped<IRuntimeCheckpointCommitStore>(serviceProvider =>
            new RecordingRuntimeCheckpointCommitStore(
                serviceProvider.GetRequiredService<InMemoryRuntimeCheckpointCommitStore>(),
                serviceProvider.GetRequiredService<CheckpointRecorder>(),
                failDispatchCommit)));

        RegisterActivityTypes(typeRegistry);
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var fixture = new DispatchWorkflowRuntimeTestFixture(provider, timeProvider);
        await fixture.SeedChildAsync();
        return fixture;
    }

    internal async ValueTask<ParentDispatchRun> StartParentAsync(
        string caseId,
        string parentWorkflowExecutionId,
        string parentCorrelationId,
        string? correlationOverride = null,
        IReadOnlyDictionary<string, object?>? dispatchInputs = null,
        int dispatchNestingDepth = 0,
        string? parentDefinitionId = null,
        Func<ValueTask>? beforeStart = null,
        bool waitForCompletion = false,
        bool? cancelChildOnParentCancellation = null,
        WorkflowRunKind runKind = WorkflowRunKind.BackgroundWeaverRun,
        WorkflowTestScope? testScope = null)
    {
        var parentExecutable = NewParentExecutable(
            caseId,
            correlationOverride,
            dispatchInputs,
            parentDefinitionId,
            waitForCompletion,
            cancelChildOnParentCancellation);
        var parentReference = NewSourceReference(
            sourceReferenceId: $"source-parent-{caseId}",
            identity: parentExecutable.Identity,
            publicationId: $"publication-parent-{caseId}",
            slotId: $"slot-parent-{caseId}");
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(parentExecutable);
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(parentReference);
        if (testScope is not null)
        {
            await _provider.GetRequiredService<IWorkflowTestScopeStore>().CreateAsync(
                testScope,
                Now.AddMinutes(-1));
        }
        if (beforeStart is not null)
            await beforeStart();

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
                runKind: runKind,
                sourceSelection: new WorkflowExecutableSourceSelection(parentReference.SourceReferenceId),
                provenanceRequirement: WorkflowExecutableProvenanceRequirement.RequireLiveReference,
                parentWorkflowExecutionId: null,
                correlationId: parentCorrelationId,
                tenantId: "tenant-42",
                partition: new WorkflowExecutionPartition("partition-eu"),
                authority: authority,
                startAuthority: null,
                dispatchNestingDepth: dispatchNestingDepth,
                testScope: testScope));

        var activityState = AssertSingle(await _provider.GetRequiredService<IActivityExecutionStateStore>()
            .ListAllAsync(parentWorkflowExecutionId));
        var identity = new WorkflowDispatchIdentity(parentWorkflowExecutionId, activityState.Execution.ActivityExecutionId);
        var dispatch = await _provider.GetRequiredService<IWorkflowDispatchStore>().FindAsync(identity.DispatchId);
        if (dispatch is null)
        {
            var incidents = await _provider.GetRequiredService<IIncidentStateStore>().ListAsync(parentWorkflowExecutionId);
            var incidentSummary = string.Join(" | ", incidents.Select(incident => $"{incident.FailureType}: {incident.Message}"));
            var parentState = await _provider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(parentWorkflowExecutionId);
            var queuedWork = await _provider.GetRequiredService<IWorkflowSchedulerWorkQueue>()
                .ListAllAsync(new RuntimeSchedulerWorkQuery(parentWorkflowExecutionId));
            var poisonRecords = await _provider.GetRequiredService<IWorkflowSchedulerPoisonStore>()
                .ListAsync(parentWorkflowExecutionId);
            var commits = Checkpoints.Commits
                .Where(commit => StringComparer.Ordinal.Equals(commit.Checkpoint.WorkflowExecutionId, parentWorkflowExecutionId))
                .ToArray();
            await using var diagnosisScope = _provider.CreateAsyncScope();
            var outboxLookup = diagnosisScope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>();
            var intentStates = new List<string>();
            foreach (var checkpointCommit in commits)
            {
                foreach (var intent in checkpointCommit.PostCommitIntents)
                {
                    var outboxItemId = RuntimePostCommitOutboxItems.OutboxItemId(checkpointCommit.CommitId, intent);
                    var outboxItem = await outboxLookup.FindAsync(outboxItemId);
                    intentStates.Add(
                        $"{checkpointCommit.Checkpoint.Name}:{intent.Kind}/{intent.IntentId}=" +
                        (outboxItem is null
                            ? "missing"
                            : $"{outboxItem.Status}[attempts={outboxItem.DeliveryAttemptCount}, failure={outboxItem.LastFailureMessage}]"));
                }
            }
            throw new InvalidOperationException(
                $"Dispatch '{identity.DispatchId}' was not persisted. Activity: {activityState.Status}/{activityState.SubStatus}. " +
                $"Metadata: {string.Join(", ", activityState.Metadata.Select(item => $"{item.Key}={item.Value}"))}. " +
                $"Workflow: {parentState?.Status}/{parentState?.SubStatus}. " +
                $"Queue: {string.Join(", ", queuedWork.Select(item => $"{item.CommandKind}/{item.WorkItemId}"))}. " +
                $"Poison: {string.Join(" | ", poisonRecords.Select(item => $"{item.CommandKind}/{item.WorkItemId}: {item.Fault.ToSummaryString()}"))}. " +
                $"Commits: {string.Join(", ", commits.Select(commit => $"{commit.Checkpoint.Name}[intents={commit.PostCommitIntents.Count}]"))}. " +
                $"Outbox: {string.Join(" | ", intentStates)}. Incidents: {incidentSummary}");
        }
        var commit = Checkpoints.SingleDispatchCommit(identity.DispatchId);
        return new ParentDispatchRun(start, activityState, identity, dispatch, commit);
    }

    internal async ValueTask<WorkflowExecutionStartDispatchResult> StartParentExpectingFailureAsync(
        string caseId,
        string parentWorkflowExecutionId,
        bool waitForCompletion)
    {
        var parentExecutable = NewParentExecutable(
            caseId,
            correlationOverride: null,
            dispatchInputs: null,
            parentDefinitionId: null,
            waitForCompletion,
            cancelChildOnParentCancellation: null);
        var parentReference = NewSourceReference(
            sourceReferenceId: $"source-parent-{caseId}",
            identity: parentExecutable.Identity,
            publicationId: $"publication-parent-{caseId}",
            slotId: $"slot-parent-{caseId}");
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(parentExecutable);
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(parentReference);
        var authority = new WorkflowExecutionAuthoritySnapshot("parent-caller", "root-initiator");
        await using var scope = _provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<IWorkflowStartDispatcher>().DispatchAsync(
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
                correlationId: "correlation-parent",
                tenantId: "tenant-42",
                partition: new WorkflowExecutionPartition("partition-eu"),
                authority: authority,
                startAuthority: null,
                dispatchNestingDepth: 0));
    }

    internal async ValueTask<IReadOnlyCollection<ActivityExecutionState>> ListActivitiesAsync(string workflowExecutionId) =>
        await _provider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(workflowExecutionId);

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

    internal async ValueTask<IReadOnlyCollection<WorkflowDispatchRecord>> ListDispatchesAsync(string parentWorkflowExecutionId) =>
        await _provider.GetRequiredService<IWorkflowDispatchStore>().ListAsync(parentWorkflowExecutionId);

    internal async ValueTask ReplaceOrUnpublishChildAsync(bool replace)
    {
        var references = _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        await references.RetireAsync("source-child", Now.AddMinutes(1), replace ? "replaced" : "unpublished");
        if (!replace)
            return;

        var replacementIdentity = new WorkflowExecutableIdentity(
            "artifact-child-replacement",
            ChildIdentity.DefinitionId,
            "version-child-replacement",
            "4.0.0",
            "sha256:child-replacement");
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewChildExecutable(replacementIdentity));
        await references.SaveAsync(NewSourceReference(
            "source-child-replacement",
            replacementIdentity,
            "publication-child-replacement",
            "slot-child"));
    }

    internal ValueTask<BookmarkState?> FindBookmarkAsync(string workflowExecutionId, string bookmarkId) =>
        _provider.GetRequiredService<IBookmarkStateStore>().FindAsync(workflowExecutionId, bookmarkId);

    internal ValueTask RetireChildPublicationAsync() => ReplaceOrUnpublishChildAsync(replace: false);

    internal ValueTask ReplaceChildPublicationAsync() => ReplaceOrUnpublishChildAsync(replace: true);

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    private async ValueTask SeedChildAsync()
    {
        await _provider.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(NewChildExecutable());
        await _provider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(ChildSourceReference());
    }

    internal static WorkflowExecutableSourceReference ChildSourceReference() =>
        NewSourceReference("source-child", ChildIdentity, "publication-child", "slot-child");

    private static WorkflowExecutable NewChildExecutable(WorkflowExecutableIdentity? identity = null) =>
        new(
            identity: identity ?? ChildIdentity,
            rootActivity: NewNode(
                executableNodeId: "node-child-probe",
                activityType: typeof(ChildProbeActivity),
                inputBindings: new Dictionary<string, RuntimeInputBinding>
                {
                    [nameof(ChildProbeActivity.Message)] = WorkflowInputBinding(
                        nameof(ChildProbeActivity.Message),
                        typeof(string),
                        "message"),
                    [nameof(ChildProbeActivity.Count)] = WorkflowInputBinding(
                        nameof(ChildProbeActivity.Count),
                        typeof(int),
                        "count"),
                    [nameof(ChildProbeActivity.Tenant)] = WorkflowInputBinding(
                        nameof(ChildProbeActivity.Tenant),
                        typeof(string),
                        "tenant"),
                    [nameof(ChildProbeActivity.Defaulted)] = WorkflowInputBinding(
                        nameof(ChildProbeActivity.Defaulted),
                        typeof(string),
                        "defaulted")
                }),
            resumeTargets: new Dictionary<string, WorkflowExecutableResumeTarget>(),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: new WorkflowExecutableInputContract(
                WorkflowExecutableInputContract.CurrentVersion,
                [
                    new WorkflowDeclaredInput("message", new Elsa.Primitives.Models.TypeReference("String"), true),
                    new WorkflowDeclaredInput("count", new Elsa.Primitives.Models.TypeReference("Int32"), true),
                    new WorkflowDeclaredInput("tenant", new Elsa.Primitives.Models.TypeReference("String"), false),
                    new WorkflowDeclaredInput(
                        "defaulted",
                        new Elsa.Primitives.Models.TypeReference("String"),
                        false,
                        JsonSerializer.SerializeToElement("from-default"))
                ]),
            dependencies: []);

    private static WorkflowExecutable NewParentExecutable(
        string caseId,
        string? correlationOverride,
        IReadOnlyDictionary<string, object?>? dispatchInputs,
        string? parentDefinitionId,
        bool waitForCompletion,
        bool? cancelChildOnParentCancellation)
    {
        dispatchInputs ??= new Dictionary<string, object?>
        {
            ["message"] = "hello child",
            ["count"] = 7,
            ["tenant"] = "workflow-input-tenant"
        };
        var jsonDispatchInputs = dispatchInputs.ToDictionary(
            item => item.Key,
            item => JsonSerializer.SerializeToElement(item.Value, item.Value?.GetType() ?? typeof(object)),
            StringComparer.Ordinal);
        var inputs = new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal)
        {
            [nameof(DispatchWorkflowActivity.WorkflowDefinitionId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WorkflowDefinitionId),
                ChildIdentity.DefinitionId,
                typeof(string)),
            [nameof(DispatchWorkflowActivity.Inputs)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.Inputs),
                jsonDispatchInputs,
                typeof(IReadOnlyDictionary<string, JsonElement>))
        };
        if (correlationOverride is not null)
        {
            inputs[nameof(DispatchWorkflowActivity.CorrelationId)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.CorrelationId),
                correlationOverride,
                typeof(string));
        }
        if (waitForCompletion)
        {
            inputs[nameof(DispatchWorkflowActivity.WaitForCompletion)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.WaitForCompletion),
                true,
                typeof(bool));
        }
        if (cancelChildOnParentCancellation is not null)
        {
            inputs[nameof(DispatchWorkflowActivity.CancelChildOnParentCancellation)] = LiteralBinding(
                nameof(DispatchWorkflowActivity.CancelChildOnParentCancellation),
                cancelChildOnParentCancellation.Value,
                typeof(bool));
        }

        var metadata = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [DispatchWorkflowConstants.PinnedTargetMetadataKey] = JsonSerializer.Serialize(
                new DispatchWorkflowPin(ChildIdentity, WorkflowExecutableSourceProvenance.From(ChildSourceReference())),
                SerializerOptions)
        };
        var node = NewNode(
            executableNodeId: "node-dispatch",
            activityType: typeof(DispatchWorkflowActivity),
            inputBindings: inputs,
            metadata: metadata);
        return new WorkflowExecutable(
            identity: new WorkflowExecutableIdentity(
                $"artifact-parent-{caseId}",
                parentDefinitionId ?? $"definition-parent-{caseId}",
                $"version-parent-{caseId}",
                "1.0.0",
                $"sha256:parent-{caseId}"),
            rootActivity: node,
            resumeTargets: NodeScopedResumeTargets(node),
            createdAt: Now,
            compatibilityMetadata: new Dictionary<string, string>(),
            inputContract: null,
            dependencies:
            [
                new WorkflowExecutableDependency(
                    ChildIdentity.ArtifactId,
                    ChildIdentity.ArtifactHash,
                    [node.ExecutableNodeId])
            ]);
    }

    /// <summary>
    /// Builds the resume-target map the production <c>ExecutableNodeCompiler</c> would emit for the dispatch
    /// node: node-scoped key/id with the local id preserved, so resolution exercises the production
    /// (ExecutableNodeId, LocalResumeTargetId) fallback rather than a raw-id direct hit.
    /// </summary>
    public static Dictionary<string, WorkflowExecutableResumeTarget> NodeScopedResumeTargets(ExecutableNode node)
    {
        var scopedId = WorkflowExecutableResumeTarget.ComposeScopedId(
            node.ExecutableNodeId,
            DispatchWorkflowConstants.CompletionResumeTargetId);
        return new Dictionary<string, WorkflowExecutableResumeTarget>
        {
            [scopedId] = new(
                scopedId,
                node.ExecutableNodeId,
                "OnChildCompletedAsync",
                new Dictionary<string, string>(),
                DispatchWorkflowConstants.CompletionResumeTargetId)
        };
    }

    private static RuntimeInputBinding LiteralBinding(string name, object value, Type type)
    {
        var valueType = ValueType(type);
        return new RuntimeInputBinding(
            name,
            valueType,
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(
                valueType,
                JsonSerializer.SerializeToElement(value, value.GetType()),
                ValueProtectionPolicy.InstanceInline));
    }

    private static RuntimeInputBinding WorkflowInputBinding(
        string name,
        Type type,
        string workflowInputName) =>
        new(
            name,
            ValueType(type),
            ValueProtectionPolicy.InstanceInline,
            RuntimeInputBindingSource.WorkflowRequest,
            workflowRequest: new RuntimeWorkflowRequestReference(workflowInputName));

    private static ExecutableNode NewNode(
        string executableNodeId,
        Type activityType,
        IReadOnlyDictionary<string, RuntimeInputBinding>? inputBindings = null,
        IReadOnlyDictionary<string, string>? metadata = null)
    {
        var contract = ActivityContractFor(activityType);
        return new ExecutableNode(
            executableNodeId: executableNodeId,
            authoredActivityId: $"authored-{executableNodeId}",
            activityType: ActivityTypeMetadata.GetDeclaredActivityType(activityType) ?? activityType.FullName!,
            activityTypeVersion: "1.0.0",
            descriptor: new RuntimeActivityDescriptor(
                WellKnownRuntimeActivityConsumers.ClrActivity,
                RuntimeActivityDescriptor.InitialSchemaVersion,
                JsonSerializer.SerializeToElement(
                    new ClrActivityDescriptor(TypeAliasConvention.CanonicalAlias(activityType)))),
            inputBindings: CompleteInputBindings(contract, inputBindings),
            outputCaptures: new Dictionary<string, RuntimeOutputCapture>(),
            metadata: metadata ?? new Dictionary<string, string>(),
            activityContract: contract);
    }

    private static ActivityContract ActivityContractFor(Type activityType)
    {
        var inputs = activityType.GetProperties()
            .Select(property => (Property: property, Attribute: property.GetCustomAttributes(typeof(ActivityInputAttribute), inherit: true)
                .Cast<ActivityInputAttribute>()
                .SingleOrDefault()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate =>
            {
                var attribute = candidate.Attribute!;
                var hasDefault = attribute.DefaultValue is not null;
                return new ActivityInputContract(
                    attribute.Key ?? candidate.Property.Name,
                    candidate.Property.Name,
                    ValueType(candidate.Property.PropertyType),
                    candidate.Property.IsDefined(typeof(RequiredAttribute), inherit: true),
                    IsNullable(candidate.Property),
                    hasDefault,
                    hasDefault
                        ? JsonSerializer.SerializeToElement(ParseDefault(candidate.Property.PropertyType, attribute.DefaultValue!))
                        : null,
                    ActivityValuePolicy.Default);
            })
            .ToArray();
        var resultType = activityType.GetInterfaces()
            .Single(candidate => candidate.IsGenericType &&
                                 candidate.GetGenericTypeDefinition() == typeof(IActivityResult<>))
            .GetGenericArguments()[0];
        var projections = resultType.GetProperties()
            .Select(property => (Property: property, Attribute: property.GetCustomAttributes(typeof(OutputAttribute), inherit: true)
                .Cast<OutputAttribute>()
                .SingleOrDefault()))
            .Where(candidate => candidate.Attribute is not null)
            .Select(candidate => new ActivityResultProjectionContract(
                candidate.Attribute!.Key ?? candidate.Property.Name,
                candidate.Attribute.Path ?? JsonNamingPolicy.CamelCase.ConvertName(candidate.Property.Name),
                ValueType(candidate.Property.PropertyType),
                candidate.Attribute.IsRequired,
                ActivityValuePolicy.Default))
            .ToArray();
        var descriptorType = typeof(ClrActivityDescriptor).FullName!;
        var alias = TypeAliasConvention.CanonicalAlias(activityType);
        return new ActivityContract(
            activityType.FullName!,
            "1.0.0",
            descriptorType,
            JsonSerializer.SerializeToElement(new ClrActivityDescriptor(alias)),
            inputs,
            new ActivityResultContract(
                ValueType(resultType),
                true,
                ActivityValuePolicy.Default,
                projections),
            activityType.GetCustomAttributes(typeof(ActivityOutcomeAttribute), inherit: true)
                .Cast<ActivityOutcomeAttribute>()
                .Select(attribute => attribute.Key)
                .DefaultIfEmpty(ActivityOutcomes.Done)
                .ToArray(),
            new ActivityActivationRequirement(descriptorType, alias));
    }

    private static IReadOnlyDictionary<string, RuntimeInputBinding> CompleteInputBindings(
        ActivityContract contract,
        IReadOnlyDictionary<string, RuntimeInputBinding>? authoredBindings)
    {
        var bindings = authoredBindings?.ToDictionary(item => item.Key, item => item.Value, StringComparer.Ordinal)
                       ?? new Dictionary<string, RuntimeInputBinding>(StringComparer.Ordinal);
        foreach (var input in contract.Inputs.Values.Where(input => !bindings.ContainsKey(input.Key)))
        {
            var policy = ValueProtectionPolicy.InstanceInline;
            var value = input.HasDefault
                ? ValueEnvelope.Inline(input.Type, input.DefaultValue!.Value, policy)
                : input.IsNullable == true
                    ? ValueEnvelope.Absent(input.Type, policy)
                    : throw new InvalidOperationException(
                        $"Test executable omits non-nullable activity input '{input.Key}' without a pinned default.");
            bindings.Add(input.Key, new RuntimeInputBinding(
                input.Key,
                input.Type,
                policy,
                RuntimeInputBindingSource.Literal,
                literal: value));
        }

        return bindings;
    }

    private static bool IsNullable(System.Reflection.PropertyInfo property)
    {
        if (Nullable.GetUnderlyingType(property.PropertyType) is not null)
            return true;
        if (property.PropertyType.IsValueType)
            return false;
        return new System.Reflection.NullabilityInfoContext().Create(property).ReadState !=
               System.Reflection.NullabilityState.NotNull;
    }

    private static object ParseDefault(Type type, string value) =>
        type == typeof(bool) ? bool.Parse(value) :
        type == typeof(int) ? int.Parse(value, System.Globalization.CultureInfo.InvariantCulture) :
        type == typeof(TimeSpan) ? TimeSpan.Parse(value, System.Globalization.CultureInfo.InvariantCulture) :
        value;

    private static ValueTypeDescriptor ValueType(Type type)
    {
        var reference = TypeReferenceFactory.FromClrType(type, TypeAliasConvention.CanonicalAlias);
        return new ValueTypeDescriptor(reference.Alias, reference.CollectionKind);
    }

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

    private static void RegisterActivityTypes(WellKnownTypeRegistry registry)
    {
        var types = new[]
        {
            typeof(DispatchWorkflowActivity),
            typeof(DispatchWorkflowActivityResult),
            typeof(DispatchWorkflowState),
            typeof(WorkflowDispatchParentResumePayload),
            typeof(DispatchWorkflowResult),
            typeof(ChildProbeActivity),
            typeof(ActivityUnit),
            typeof(JsonElement),
            typeof(IReadOnlyDictionary<string, JsonElement>)
        };
        foreach (var type in types)
            registry.RegisterType(type, TypeAliasConvention.CanonicalAlias(type));
    }

    private sealed class ChildProbeActivity(ChildExecutionProbe probe) : Activity<ActivityUnit>
    {
        [ActivityInput]
        [Required]
        public string Message { get; set; } = null!;

        [ActivityInput]
        [Required]
        public int Count { get; set; }

        [ActivityInput]
        public string? Tenant { get; set; }

        [ActivityInput]
        public string? Defaulted { get; set; }

        protected override ValueTask<ActivityTransition<ActivityUnit>> ExecuteAsync(ActivityExecutionContext context)
        {
            probe.Record(
                context.WorkflowExecutionId,
                context.InvocationId,
                new Dictionary<string, object?>
                {
                    ["message"] = Message,
                    ["count"] = Count,
                    ["tenant"] = Tenant,
                    ["defaulted"] = Defaulted
                });
            return ValueTask.FromResult(ActivityTransition.Complete(ActivityUnit.Value));
        }
    }

    private sealed class AdjustableTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        internal void Advance(TimeSpan duration) => _now += duration;
    }
}

internal sealed record ParentDispatchRun(
    WorkflowExecutionStartDispatchResult Start,
    ActivityExecutionState Activity,
    WorkflowDispatchIdentity Identity,
    WorkflowDispatchRecord Dispatch,
    RuntimeCheckpointCommit CompletionCommit);

internal sealed class RecordingDispatchBookmarkObserver : IBookmarkLifecycleObserver
{
    private readonly Lock _gate = new();
    private readonly List<BookmarkState> _created = [];

    internal IReadOnlyCollection<BookmarkState> Created
    {
        get { lock (_gate) return _created.ToArray(); }
    }

    public ValueTask OnBookmarkCreatedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default)
    {
        lock (_gate)
            _created.Add(bookmark);
        return ValueTask.CompletedTask;
    }

    public ValueTask OnBookmarkConsumedAsync(BookmarkState bookmark, CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}

internal sealed class CheckpointRecorder
{
    private readonly Lock _gate = new();
    private readonly List<RuntimeCheckpointCommit> _commits = [];

    internal void Record(RuntimeCheckpointCommit commit)
    {
        lock (_gate)
            _commits.Add(commit);
    }

    internal IReadOnlyCollection<RuntimeCheckpointCommit> Commits
    {
        get
        {
            lock (_gate)
                return _commits.ToArray();
        }
    }

    internal RuntimeCheckpointCommit SingleDispatchCommit(string dispatchId)
    {
        lock (_gate)
            return _commits.Single(commit => commit.StateChanges.WorkflowDispatches.Any(change => change.StateId == dispatchId));
    }

    internal RuntimeCheckpointCommit SingleIntentCommit(string intentKind)
    {
        lock (_gate)
            return _commits.Single(commit => commit.PostCommitIntents.Any(intent => intent.Kind == intentKind));
    }
}

internal sealed class RecordingRuntimeCheckpointCommitStore(
    InMemoryRuntimeCheckpointCommitStore inner,
    CheckpointRecorder recorder,
    bool failDispatchCommit = false) : IRuntimeCheckpointCommitStore
{
    public ValueTask<RuntimeCheckpointCommitStoreResult> CommitAsync(
        RuntimeCheckpointCommit commit,
        RuntimeCheckpointPersistenceDecision decision,
        CancellationToken cancellationToken = default)
    {
        recorder.Record(commit);
        if (failDispatchCommit && commit.StateChanges.WorkflowDispatches.Count > 0)
            throw new InvalidOperationException("Injected workflow-dispatch checkpoint failure.");
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
