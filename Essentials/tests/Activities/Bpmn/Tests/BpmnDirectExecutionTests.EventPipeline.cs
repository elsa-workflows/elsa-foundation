using System.Text.Json;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Primitives.Activities;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Activities.Testing;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Readability;
using Elsa.Cluster.Core.Models;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Api;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Primitives;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    // These use fresh production providers over one real SQLite database, not recording actors or injected cohorts.
    // The constructed fleet and clock qualify component boundaries; they do not establish process-host acceptance.
    [Fact]
    public async Task A_real_workflow_Event_reaches_a_genuine_BPMN_root_and_its_worker_consumes_the_mapped_declared_input()
    {
        await using var host = await OpenAsync(configure: ConfigureEventPipeline,
            activityIds: Enumerable.Range(0, 32).Select(index => "event-pipeline-" + index).ToArray());
        var sender = PipelinePublisher("sender", "orders", LiteralPipelineInput(nameof(PublishEvent.Payload), "JsonElement", new { text = "from-actual-workflow" }));
        var worker = PipelinePublisher("worker", "worker-consumed", new(nameof(PublishEvent.Payload), PipelinePayloadType,
            ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.WorkflowRequest, workflowRequest: new("message")),
            new(1, [new("message", new("String"), true)]));
        var waiting = PipelineEvent("waiting", "orders", false);
        var consumed = PipelineEvent("consumed", "worker-consumed", false);
        var embedded = PipelineEvent("embedded", "orders", true);
        var senderReference = await host.PublishAsync(sender, "sender-reference");
        await host.PublishAsync(worker, "worker-reference");
        var waitingReference = await host.PublishAsync(waiting, "waiting-reference");
        var consumedReference = await host.PublishAsync(consumed, "consumed-reference");
        var embeddedReference = await host.PublishAsync(embedded, "embedded-reference");
        await host.StartPublishedAsync(waitingReference, "already-waiting", partition: new(PersistenceScope.DefaultValue));
        await host.StartPublishedAsync(consumedReference, "waiting-for-consumption", partition: new(PersistenceScope.DefaultValue));
        await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        await services.GetRequiredService<IWorkflowTriggerBindingStore>().SaveAsync(new("embedded-orders", embedded.Identity.ArtifactId,
            embedded.Identity.DefinitionId, embedded.Identity.ArtifactVersion, embedded.Identity.ArtifactHash,
            embedded.RootActivity.ExecutableNodeId, EventStimulus.StimulusType, EventStimulus.Hash("orders"), null,
            new Dictionary<string, string>(), DateTimeOffset.UtcNow, embeddedReference.ActivationId, embeddedReference.SlotId));
        var process = new BpmnProcessDefinition("event-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var model = await services.GetRequiredService<IBpmnModelRuntime>().PreparePublicationAsync(new(3,
                JsonSerializer.SerializeToElement(process, Web), [], [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]),
            new(1, [new("text", new("String"), true)]),
            [new("worker", new Dictionary<string, BpmnModelInputSource> { ["message"] = new("Input", "text") })], new("operator", null));
        var published = await PublishPipelineTargetAsync(services, "event-model", model, "model-v1", 0);
        await CreatePipelineAssociationAsync(services, "bpmn", published, [new("text", new TriggerSourceBinding(["payload", "text"]))]);

        await host.StartPublishedAsync(senderReference, "actual-event-publisher", partition: new(PersistenceScope.DefaultValue));
        var original = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(
            new(DateTimeOffset.UtcNow, 100, workflowExecutionId: "actual-event-publisher", intentKind: PublishStimulusConstants.PublishStimulusIntentKind)));
        Assert.Null(original.StandaloneIngress);
        Assert.Null(original.SubscriptionDelivery);
        await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100,
            workflowExecutionId: "actual-event-publisher", intentKind: PublishStimulusConstants.PublishStimulusIntentKind));
        var accepted = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(original.OutboxItemId))!;
        var recipient = Assert.Single(accepted.SubscriptionDelivery!.Recipients);
        Assert.Equal("bpmn", recipient.ModelKey);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, Assert.Single(accepted.SubscriptionDelivery.Progress).Phase);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, accepted.Status);
        Assert.Equal("actual-event-publisher", accepted.RequireWorkflowIntent().WorkflowExecutionId);
        Assert.Single(accepted.RecipientSnapshot!.Starts);
        Assert.Single(accepted.RecipientSnapshot.Resumes);
        Assert.Equal(WorkflowExecutionStatus.Completed, (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync("already-waiting"))!.Status);
        var modelState = (await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(recipient.StartIdentity.WorkflowExecutionId))!;
        var document = BpmnDirectExecutionDriver.Read(modelState);
        Assert.Null(document.NativeParent);
        Assert.Equal(BpmnDirectExecutionDriver.TriggerKind, document.Entry!.Kind);
        Assert.Equal("from-actual-workflow", document.Entry.Inputs["text"].GetString());
        var nativeWork = Assert.Single(document.Work).NativeWork!;
        Assert.Equal("from-actual-workflow", nativeWork.Inputs!["message"].GetString());
        Assert.Null(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(modelState.ExecutionId));

        // All progress uses the existing source processor and ordinary model/native continuation.
        for (var pass = 0; pass < 6; pass++)
        {
            await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100));
            await DrainModelAsync(host, modelState.ExecutionId);
        }
        var actualWorker = (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(nativeWork.WorkerExecutionId))!;
        Assert.Equal(WorkflowExecutionStatus.Completed, actualWorker.Status);
        Assert.True(nativeWork.IsEquivalentTo(actualWorker.ModelWorkOrigin));
        var workerActivity = Assert.Single(await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(nativeWork.WorkerExecutionId));
        Assert.Equal("from-actual-workflow", workerActivity.InputSnapshot!.Values[nameof(PublishEvent.Payload)].InlineValue!.Value.GetString());
        var consumedActivity = Assert.Single(await services.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync("waiting-for-consumption"));
        Assert.Equal("from-actual-workflow", consumedActivity.Completion!.Result.InlineValue!.Value.Deserialize<EventResult>(Web)!.Payload!.Value.GetString());
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, (await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(modelState.ExecutionId))!.Lifecycle);
        var embeddedExecution = KeyedWorkflowStartIdentity.For(original.RequireWorkflowIntent().IdempotencyKey!, embedded.Identity.ArtifactId).WorkflowExecutionId;
        Assert.Equal(WorkflowExecutionStatus.Completed, (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(embeddedExecution))!.Status);
        Assert.Empty(await services.GetRequiredService<IIncidentStateStore>().ListAsync(nativeWork.WorkerExecutionId));
        Assert.Empty(await services.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(nativeWork.WorkerExecutionId));
    }

    [Fact]
    public async Task A_physically_sealed_Event_recipient_survives_pause_replacement_unpublication_and_two_fresh_provider_workers()
    {
        var clock = new PipelineClock();
        string sourceId, executionId, sealFingerprint;
        WorkflowExecutableIdentity captured;
        await using (var first = await OpenAsync(configure: services =>
        {
            ConfigureEventPipeline(services);
            services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock));
            StopPipelineIngressBeforeEnqueue(services);
        }, activityIds: ["publisher-before-restart"]))
        {
            var target = PipelineEvent("retained", "later", false);
            var sender = PipelinePublisher("restart-sender", "orders", LiteralPipelineInput(nameof(PublishEvent.Payload), "JsonElement", new { text = "first-version" }));
            var senderReference = await first.PublishAsync(sender, "restart-sender-reference");
            await using var scope = await ScopeAsync(first);
            var services = scope.ServiceProvider;
            await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(target);
            var material = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(target.Identity.ArtifactId))!;
            var published = await PublishPipelineTargetAsync(services, "retained-definition", material, "retained-v1", 0);
            await CreatePipelineAssociationAsync(services, "native", published, []);
            await first.StartPublishedAsync(senderReference, "restart-publisher", partition: new(PersistenceScope.DefaultValue));
            var original = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(clock.GetUtcNow(), 100,
                workflowExecutionId: "restart-publisher", intentKind: PublishStimulusConstants.PublishStimulusIntentKind)));
            sourceId = original.OutboxItemId;
            await Assert.ThrowsAsync<RuntimePostCommitSourceBoundaryException>(() => services.GetRequiredService<IRuntimePostCommitOutboxProcessor>()
                .ProcessAsync(new(100, workflowExecutionId: "restart-publisher")).AsTask());
            var stopped = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(sourceId))!;
            var recipient = Assert.Single(stopped.SubscriptionDelivery!.Recipients);
            var progress = Assert.Single(stopped.SubscriptionDelivery.Progress);
            Assert.Equal(RuntimeRecipientPhase.DispatchCommitted, progress.Phase);
            Assert.Null(progress.Receipt);
            sealFingerprint = progress.Seal!.Fingerprint;
            executionId = recipient.StartIdentity.WorkflowExecutionId;
            captured = recipient.Material;
            Assert.Null(await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId));
            Assert.Empty((await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(executionId))).Items);
            Assert.Contains(captured.ArtifactId, await services.GetRequiredService<IStimulusDeliveryStore>().ListPinnedExecutableArtifactIdsAsync());
            var resources = services.GetRequiredService<IStandaloneTriggerStore>();
            var resourceScope = new TriggerScope(new(PersistenceScope.DefaultValue), null);
            var resource = (await resources.FindAsync(resourceScope, "pipeline-trigger"))!;
            var old = resource.Trigger;
            var paused = new StandaloneTrigger(old.Id, old.Name, old.TypeKey, old.SchemaVersion, old.Configuration, 2, old.ConfigurationRevision,
                old.AssociationRevision, TriggerDesiredState.Paused, old.Origin, old.CreatedAt, clock.GetUtcNow(), old.CreatedBy);
            await resources.WriteAsync(resourceScope, new("pause-pipeline", "pause", "operator", 1, paused, resource.Subscriptions));
            var replacement = PipelineEvent("replacement", "later", false);
            await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(replacement);
            var replacementMaterial = (await services.GetRequiredService<IWorkflowExecutableStore>().FindMaterialAsync(replacement.Identity.ArtifactId))!;
            var replaced = await PublishPipelineTargetAsync(services, "retained-definition", replacementMaterial, "retained-v2", published.Slot.Revision);
            Assert.True((await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateMaterialAsync(new("retained-definition",
                replacementMaterial, "default", WorkflowActivationSource.Publishing, replaced.Slot.Revision))).Succeeded);
            await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync(recipient.SourceProvenance.SourceReferenceId!, clock.GetUtcNow(), "unpublished-after-acceptance");
        }
        clock.Now = clock.Now.AddMinutes(3);
        await using var second = await OpenAsync(configure: services => { ConfigureEventPipeline(services); services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)); }, activityIds: ["recovered-target"]);
        await using var peer = await OpenAsync(configure: services => { ConfigureEventPipeline(services); services.Replace(ServiceDescriptor.Singleton<TimeProvider>(clock)); }, activityIds: ["peer-target"]);
        await using var secondScope = await ScopeAsync(second);
        await using var peerScope = await ScopeAsync(peer);
        var results = await Task.WhenAll(secondScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100)).AsTask(),
            peerScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100)).AsTask());
        Assert.Equal(1, results.Sum(result => result.DeliveredCount));
        var recovered = (await peerScope.ServiceProvider.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(sourceId))!;
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivered, recovered.Status);
        var recoveredProgress = Assert.Single(recovered.SubscriptionDelivery!.Progress);
        Assert.Equal(sealFingerprint, recoveredProgress.Seal!.Fingerprint);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, recoveredProgress.Phase);
        var execution = (await peerScope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(executionId))!;
        Assert.Equal(captured, execution.PinnedExecutable);
        Assert.NotNull(execution.IngressOrigin);
        Assert.True(execution.IngressOrigin!.IsEquivalentTo(recoveredProgress.Seal.Command.Deserialize<WorkflowExecutionCommandEnvelope>()!
            .Command.Payload!.Value.Deserialize<WorkflowExecutionStartCommandPayload>()!.StartAuthority!.StandaloneTrigger));
        Assert.Single(await peerScope.ServiceProvider.GetRequiredService<IActivityExecutionStateStore>().ListAllAsync(executionId));
        Assert.Single(await peerScope.ServiceProvider.GetRequiredService<IBookmarkStateStore>().ListAllBookmarkStatesAsync(executionId));
        Assert.Empty((await peerScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(executionId))).Items);
        Assert.NotNull(recoveredProgress.Receipt);
        Assert.Equal(RuntimeTargetAcceptanceKind.InitialCheckpoint, recoveredProgress.Receipt!.Kind);
        Assert.Equal(0, (await peerScope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100))).DeliveredCount);
        Assert.Empty(await peerScope.ServiceProvider.GetRequiredService<IIncidentStateStore>().ListAsync(executionId));
        Assert.Empty(await peerScope.ServiceProvider.GetRequiredService<IWorkflowSchedulerPoisonStore>().ListAsync(executionId));
    }

    private static void ConfigureEventPipeline(IServiceCollection services)
    {
        new WorkflowsRuntimeTriggersFeature().ConfigureServices(services);
        new WorkflowsRuntimeRecurringTriggersFeature().ConfigureServices(services);
        services.AddSingleton<IEfSchemaFleet, PipelineFleet>();
        services.AddEfSchemaDormancy();
    }

    private static WorkflowExecutable PipelinePublisher(string name, string eventName, RuntimeInputBinding payload, WorkflowExecutableInputContract? contract = null) =>
        PipelineExecutable(name, typeof(PublishEvent), new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(PublishEvent.EventName)] = LiteralPipelineInput(nameof(PublishEvent.EventName), "String", eventName),
            [nameof(PublishEvent.Payload)] = payload,
            [nameof(PublishEvent.IsLocalEvent)] = LiteralPipelineInput(nameof(PublishEvent.IsLocalEvent), "Boolean", false)
        }, contract);

    private static WorkflowExecutable PipelineEvent(string name, string eventName, bool canStart) => PipelineExecutable(name, typeof(Event),
        new Dictionary<string, RuntimeInputBinding>
        {
            [nameof(Event.EventName)] = LiteralPipelineInput(nameof(Event.EventName), "String", eventName),
            [nameof(Event.CanStartWorkflow)] = LiteralPipelineInput(nameof(Event.CanStartWorkflow), "Boolean", canStart)
        });

    private static WorkflowExecutable PipelineExecutable(string name, Type type, IReadOnlyDictionary<string, RuntimeInputBinding> bindings,
        WorkflowExecutableInputContract? inputs = null)
    {
        var contract = ClrActivityContractTestBuilder.BuildContract(type);
        var node = new ExecutableNode(name, "authored-" + name, type == typeof(Event) ? Event.ActivityType : PublishEvent.ActivityType, "1.0.0",
            WellKnownRuntimeActivityConsumers.ClrActivity, contract.DescriptorPayload,
            ClrActivityContractTestBuilder.CompleteInputBindings(contract, bindings), new Dictionary<string, string>(), activityContract: contract);
        var resumes = new Dictionary<string, WorkflowExecutableResumeTarget>();
        if (type == typeof(Event))
        {
            var id = WorkflowExecutableResumeTarget.ComposeScopedId(name, Event.ResumeTargetId);
            resumes.Add(id, new(id, name, "ResumeAsync", new Dictionary<string, string>(), Event.ResumeTargetId));
        }
        return new(new(name + "-artifact", name + "-definition", name + "-version", "1.0.0", name + "-hash"), node, resumes,
            DateTimeOffset.UtcNow, new Dictionary<string, string>(), inputs ?? new(1, []), null, IncidentStrategyBuiltIns.FaultReference);
    }

    private static ValueTypeDescriptor PipelinePayloadType =>
        ClrActivityContractTestBuilder.BuildContract(typeof(PublishEvent)).Inputs[nameof(PublishEvent.Payload)].Type;

    private static RuntimeInputBinding LiteralPipelineInput(string name, string type, object value)
    {
        var descriptor = name == nameof(PublishEvent.Payload) ? PipelinePayloadType : new ValueTypeDescriptor(type);
        return new(name, descriptor, ValueProtectionPolicy.InstanceInline, RuntimeInputBindingSource.Literal,
            literal: ValueEnvelope.Inline(descriptor, JsonSerializer.SerializeToElement(value), ValueProtectionPolicy.InstanceInline));
    }

    private static async Task<WorkflowActivationResult> PublishPipelineTargetAsync(IServiceProvider services, string definition,
        RuntimeExecutionMaterial material, string publication, long expectedRevision)
    {
        var now = DateTimeOffset.UtcNow;
        var reference = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(publication), material.Identity.ArtifactId,
            "WorkflowDefinition", publication, publication, definition, publication, publication, now, now, WorkflowExecutableReferenceScope.Published);
        var result = await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition, material, reference,
            "default", publication, WorkflowActivationSource.Publishing, expectedRevision));
        Assert.True(result.Succeeded, result.Outcome + ": " + result.Diagnostic);
        return result;
    }

    private static async Task CreatePipelineAssociationAsync(IServiceProvider services, string model, WorkflowActivationResult published,
        IReadOnlyList<TriggerInputMapping> mappings)
    {
        var now = DateTimeOffset.UtcNow;
        var definition = published.Slot.WorkflowDefinitionId;
        var trigger = new StandaloneTrigger("pipeline-trigger", "Actual workflow Event", "event", 1, JsonSerializer.SerializeToElement(new { eventName = "orders" }),
            1, 1, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), now, now, "operator");
        var subscription = new TriggerSubscription("pipeline-subscription", trigger.Id, definition, "default", model, 1, 1, true, mappings,
            new(TriggerManagementKind.Studio), new("pipeline-grant", trigger.Id, "pipeline-subscription", definition, "default", model, "event", 1,
                TriggerGrantStatus.Granted, "operator", now));
        await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(new(new(PersistenceScope.DefaultValue), null),
            new("create-pipeline", "create", "operator", null, trigger, [subscription]));
    }

    private static void StopPipelineIngressBeforeEnqueue(IServiceCollection services)
    {
        var original = services.Last(item => item.ServiceType == typeof(IWorkflowExecutionActorProvider));
        services.Remove(original);
        services.AddSingleton<IWorkflowExecutionActorProvider>(provider => new PipelineStoppedActors(
            (IWorkflowExecutionActorProvider)(original.ImplementationInstance ?? original.ImplementationFactory?.Invoke(provider)
                ?? ActivatorUtilities.CreateInstance(provider, original.ImplementationType!))));
    }

    private sealed class PipelineStoppedActors(IWorkflowExecutionActorProvider inner) : IWorkflowExecutionActorProvider, IDisposable
    {
        public WorkflowExecutionActorCapabilities Capabilities => inner.Capabilities;
        public ValueTask<IWorkflowExecutionActor> GetAgentAsync(WorkflowExecutionActorActivationRequest request, CancellationToken cancellationToken = default) =>
            request.RequestedBy.StartsWith("standalone-trigger:", StringComparison.Ordinal)
                ? ValueTask.FromException<IWorkflowExecutionActor>(new IOException("Constructed worker stop after physical seal and before ordinary actor enqueue."))
                : inner.GetAgentAsync(request, cancellationToken);
        public ValueTask PassivateAsync(WorkflowExecutionActorPassivationRequest request, CancellationToken cancellationToken = default) => inner.PassivateAsync(request, cancellationToken);
        public void Dispose() => (inner as IDisposable)?.Dispose();
    }

    private sealed class PipelineClock : TimeProvider
    {
        internal DateTimeOffset Now = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class PipelineFleet : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("event-pipeline", "component-incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
}
