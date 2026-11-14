using System.Text.Json;
using System.Text.Json.Nodes;
using Bpmn.Model;
using Elsa.Activities.Bpmn.Core;
using Elsa.Activities.Bpmn.Internal;
using Elsa.Activities.Testing;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Activities.Bpmn.Tests;

public sealed partial class BpmnDirectExecutionTests
{
    [Fact]
    public async Task Workflow_owned_cohort_prepares_model_dispatch_but_needs_its_actual_source_seal_and_exact_tenant()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var fixture = await PrepareModelIngressAsync(services);
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var recipient = fixture.Recipient;
        var intent = new RuntimePostCommitIntent("publish-event", "publisher", "Elsa.Activities.Primitives.PublishStimulus", fixture.CreatedAt,
            null, null, JsonSerializer.SerializeToElement(new { eventName = "orders" }));
        var fingerprint = RuntimeCheckpointCommitFingerprint.ComputeIntent(intent);
        RuntimePostCommitOutboxItem WorkflowSource(string? tenant)
        {
            var ordinary = new StimulusDeliverySnapshot(1, fixture.Source.OutboxItemId, fingerprint,
                new(fixture.Scope.Persistence, tenant, recipient.Partition, WorkflowRunKind.PublishedRun, null), fixture.CreatedAt,
                "Event", "orders", [], []);
            var independent = new RuntimeSubscriptionDelivery(1, fixture.Source.OutboxItemId, fingerprint, fixture.Scope.Persistence,
                [recipient], [], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
            return new(fixture.Source.OutboxItemId, intent, RuntimePostCommitOutboxStatus.Pending, fixture.CreatedAt, fixture.CreatedAt,
                RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1)), 0, null, null, null, null, null, 0, null,
                1, fixture.Scope.Persistence, ordinary, null, 1, independent);
        }
        var source = WorkflowSource(fixture.Scope.TenantId);

        var seal = await target.PrepareDispatchAsync(source, recipient);

        Assert.Null(source.StandaloneIngress);
        Assert.Equal(source.SourceFingerprint, seal.SourceFingerprint);
        Assert.Equal(recipient.StartIdentity.InputId, seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web)!.InputId);
        // Preparing data cannot replace the differently captured physical source or authorize enqueue.
        await Assert.ThrowsAsync<InvalidDataException>(() => target.DispatchAsync(source, recipient, seal).AsTask());
        await Assert.ThrowsAsync<BpmnModelValidationException>(() => target.PrepareDispatchAsync(WorkflowSource("foreign-tenant"), recipient).AsTask());
        Assert.Null(await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(recipient.StartIdentity.WorkflowExecutionId));
        await AssertNativeEmptyAsync(services);
    }

    [Fact]
    public async Task An_opaque_scope_model_root_keeps_its_exact_tenant_and_finishes_from_retained_state_after_source_retirement()
    {
        await using var host = await OpenAsync();
        await using var scope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new("physical-shell"));
        var services = scope.ServiceProvider;
        var fixture = await PrepareModelIngressAsync(services, "actual-tenant");
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var seal = await target.PrepareDispatchAsync(fixture.Source, fixture.Recipient);
        var claim = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
            .ClaimAsync(new("opaque-model", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)));
        var source = await fixture.Port.CommitDispatchAsync(new(claim, fixture.Scope.Persistence), seal);
        await target.DispatchAsync(source, fixture.Recipient, seal);
        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var caller = new BpmnModelCaller("operator", "actual-tenant");
        var state = (await runtime.FindExecutionAsync(fixture.Recipient.StartIdentity.WorkflowExecutionId, caller))!;
        Assert.NotNull(state);
        Assert.Equal("physical-shell", state.Scope.Value);
        Assert.Equal("actual-tenant", state.TenantId);
        Assert.Equal(fixture.Recipient.Partition, state.Partition);
        Assert.True(state.IngressOrigin!.IsEquivalentTo(seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web)!.IngressOrigin));
        Assert.Equal("1.1.0", (await services.GetRequiredService<RuntimeDbContext>().Database.SqlQueryRaw<string>("SELECT SchemaVersion AS Value FROM elsa_runtime_model_execution").SingleAsync()));
        var receipt = (await target.ReconcileAsync(source, fixture.Recipient, seal))!;
        await fixture.Port.RecordReceiptAsync(new(claim, fixture.Scope.Persistence), receipt);
        await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(fixture.Scope,
            new("pause-opaque-trigger", "pause", "operator", 1, fixture.Trigger(2, TriggerDesiredState.Paused), [fixture.Subscription]));
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync(fixture.Recipient.SourceProvenance.SourceReferenceId,
            DateTimeOffset.UtcNow, "retired-after-acceptance");
        var work = Assert.Single(BpmnDirectExecutionDriver.Read(state).Work);

        await runtime.CompleteHumanAsync(state.ExecutionId, "complete-opaque-root", work.InvocationId, work.AttemptId, null, caller);

        var completed = (await runtime.FindExecutionAsync(state.ExecutionId, caller))!;
        Assert.Equal(RuntimeModelLifecycle.TerminalSettled, completed.Lifecycle);
        Assert.Equal(state.TenantId, completed.TenantId);
        Assert.Equal(state.Partition, completed.Partition);
        Assert.True(state.IngressOrigin.IsEquivalentTo(completed.IngressOrigin));
        Assert.Null(await runtime.FindExecutionAsync(state.ExecutionId, new("operator", "other-tenant")));
        await AssertNativeEmptyAsync(services);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ingress_model_workers_keep_the_physical_scope_and_exact_tenant_in_both_closed_and_input_profiles(bool mappedInput)
    {
        await using var host = await OpenAsync(configure: ConfigureEventPipeline, activityIds: ["input-worker"]);
        await using var scope = await host.Services.GetRequiredService<IPersistenceOperationScopeFactory>().CreateAsync(new("physical-shell"));
        var services = scope.ServiceProvider;
        var store = services.GetRequiredService<IWorkflowExecutableStore>();
        var worker = new WorkflowExecutable(WorkflowExecutionHarness.Identity,
            WorkflowExecutionHarness.NewFaultingNode("input-worker", "Expected worker fault"),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), WorkflowExecutionHarness.Timestamp, new Dictionary<string, string>(),
            inputContract: new(1, mappedInput ? [new("message", new("String"), true)] : []), dependencies: null,
            incidentStrategy: Elsa.Workflows.Runtime.Core.Models.IncidentStrategyBuiltIns.FaultReference);
        await store.SaveAsync(worker);
        var workerMaterial = (await store.FindMaterialAsync(worker.Identity.ArtifactId))!;
        var now = DateTimeOffset.UtcNow;
        var reference = new WorkflowExecutableSourceReference("unused", worker.Identity.ArtifactId, "WorkflowDefinition", "worker-version", "1",
            "worker-logical-definition", "worker-version", "1", now, now, WorkflowExecutableReferenceScope.Published, TenantId: "actual-tenant");
        Assert.True((await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new("worker-logical-definition",
            workerMaterial, reference, "default", "worker-publication", WorkflowActivationSource.Publishing, 0))).Succeeded);
        var process = new BpmnProcessDefinition("ingress-service", Elements:
            [new("start", BpmnElementTypes.StartEvent), new("service", BpmnElementTypes.ServiceTask, bindingRef: "worker"), new("end", BpmnElementTypes.EndEvent)],
            SequenceFlows: [new("to-service", "start", "service"), new("to-end", "service", "end")]);
        var model = await services.GetRequiredService<IBpmnModelRuntime>().PreparePublicationAsync(new(3, JsonSerializer.SerializeToElement(process, Web), [],
                [new("worker", worker.Identity, "business-error", new(1, BpmnServiceFaultDisposition.FinalFault, 1))]),
            new(1, mappedInput ? [new("text", new("String"), true)] : []),
            mappedInput ? [new("worker", new Dictionary<string, BpmnModelInputSource> { ["message"] = new("Input", "text") })] : [], new("operator", "actual-tenant"));
        var fixture = await PrepareModelIngressAsync(services, "actual-tenant", model,
            mappedInput ? new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("trigger-value") } : new Dictionary<string, JsonElement>());
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var seal = await target.PrepareDispatchAsync(fixture.Source, fixture.Recipient);
        var claim = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
            .ClaimAsync(new("opaque-worker-model", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)));
        var source = await fixture.Port.CommitDispatchAsync(new(claim, fixture.Scope.Persistence), seal);
        await target.DispatchAsync(source, fixture.Recipient, seal);
        var modelState = (await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(fixture.Recipient.StartIdentity.WorkflowExecutionId))!;
        var origin = Assert.Single(BpmnDirectExecutionDriver.Read(modelState).Work).NativeWork!;
        Assert.Equal(mappedInput ? 2 : 1, origin.FormatVersion);
        Assert.True(modelState.IngressOrigin!.IsEquivalentTo(origin.IngressOrigin));
        Assert.Equal(modelState.Partition, origin.Partition);
        var nativeSource = (await services.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(origin.SourceOutboxItemId))!;
        Assert.Equal(BpmnNativeWorkBoundary.IngressStartKind, nativeSource.Intent.Kind);

        await services.GetRequiredService<IRuntimePostCommitOutboxProcessor>().ProcessAsync(new(100,
            workflowExecutionId: modelState.ExecutionId, intentKind: BpmnNativeWorkBoundary.IngressStartKind));

        var actual = (await services.GetRequiredService<IWorkflowExecutionStateStore>().FindAsync(origin.WorkerExecutionId))!;
        Assert.NotNull(actual);
        Assert.Null(actual.ParentWorkflowExecutionId);
        Assert.Null(actual.IngressOrigin);
        Assert.Equal("actual-tenant", actual.TenantId);
        Assert.Equal(modelState.Partition, actual.Partition);
        Assert.True(origin.IsEquivalentTo(actual.ModelWorkOrigin));
        if (mappedInput)
        {
            var value = Assert.Single((await services.GetRequiredService<IDurableValueStateStore>().ListPageAsync(new(origin.WorkerExecutionId))).Items,
                item => item.Metadata.GetValueOrDefault(Elsa.Workflows.Runtime.Core.Constants.RuntimeMetadataKeys.InputName) == "message");
            Assert.Equal("trigger-value", value.InlineValue!.Value.GetString());
        }
    }

    [Fact]
    public async Task A_physically_sealed_subscription_enters_the_ordinary_model_root_after_pause_and_unpublication()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var fixture = await PrepareModelIngressAsync(services);
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var seal = await target.PrepareDispatchAsync(fixture.Source, fixture.Recipient);
        var claims = services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claim = Assert.Single(await claims.ClaimAsync(new("model-delivery", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)));
        var source = await fixture.Port.CommitDispatchAsync(new(claim, fixture.Scope.Persistence), seal);
        await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(fixture.Scope,
            new("pause-model-trigger", "pause", "operator", 1, fixture.Trigger(2, TriggerDesiredState.Paused), [fixture.Subscription]));
        Assert.True((await services.GetRequiredService<IWorkflowActivationCoordinator>().DeactivateMaterialAsync(new(
            fixture.Recipient.LogicalDefinitionId, fixture.Material, "default", WorkflowActivationSource.Publishing, fixture.Recipient.SlotRevision))).Succeeded);
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().RetireAsync(fixture.Recipient.SourceProvenance.SourceReferenceId,
            DateTimeOffset.UtcNow, "publication-unpublished");

        await target.DispatchAsync(source, fixture.Recipient, seal);

        var runtime = services.GetRequiredService<IBpmnModelRuntime>();
        var state = (await runtime.FindExecutionAsync(fixture.Recipient.StartIdentity.WorkflowExecutionId, new("operator", null)))!;
        Assert.NotNull(state);
        Assert.Equal(fixture.Material.Identity, state.Executable);
        Assert.True(fixture.Recipient.Partition == state.Partition);
        Assert.True(state.IngressOrigin!.IsEquivalentTo(seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web)!.IngressOrigin));
        var document = BpmnDirectExecutionDriver.Read(state);
        Assert.Null(document.NativeParent);
        Assert.Equal(BpmnDirectExecutionDriver.TriggerKind, document.Entry!.Kind);
        Assert.Equal("trigger-value", document.Entry.Inputs["text"].GetString());
        Assert.Single(document.Work);
        await AssertNativeEmptyAsync(services);
        var receipt = await target.ReconcileAsync(source, fixture.Recipient, seal);
        Assert.Equal(RuntimeTargetAcceptanceKind.ModelInput, receipt!.Kind);
        Assert.Equal(fixture.Recipient.StartIdentity.InputId, receipt.EvidenceId);
        var acknowledged = await fixture.Port.RecordReceiptAsync(new(claim, fixture.Scope.Persistence), receipt);
        Assert.Equal(RuntimeRecipientPhase.TargetAccepted, Assert.Single(acknowledged.SubscriptionDelivery!.Progress).Phase);
        await target.DispatchAsync(acknowledged, fixture.Recipient, seal);
        Assert.Equal(state.Revision, (await runtime.FindExecutionAsync(state.ExecutionId, new("operator", null)))!.Revision);
    }

    [Fact]
    public async Task A_prepared_seal_is_no_authority_and_changed_sealed_model_inputs_cannot_enqueue()
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var fixture = await PrepareModelIngressAsync(services);
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var seal = await target.PrepareDispatchAsync(fixture.Source, fixture.Recipient);
        var uncommitted = await Assert.ThrowsAsync<TriggerOperationException>(() => target.DispatchAsync(fixture.Source, fixture.Recipient, seal).AsTask());
        Assert.IsType<InvalidDataException>(uncommitted.InnerException);
        var claim = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
            .ClaimAsync(new("model-delivery", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)));
        var source = await fixture.Port.CommitDispatchAsync(new(claim, fixture.Scope.Persistence), seal);
        var command = seal.Command.Deserialize<RuntimeModelExecutionCommand>(Web)!;
        var payload = JsonNode.Parse(command.Payload.GetRawText())!;
        payload["inputs"]!["text"] = "forged";
        var changed = new RuntimeModelExecutionCommand(command.Executable, command.MaterialIncarnationId, command.InputId, command.InputKind,
            JsonSerializer.SerializeToElement(payload, Web), command.Authority, command.TenantId) { IngressOrigin = command.IngressOrigin };
        var identity = fixture.Recipient.StartIdentity;
        var now = DateTimeOffset.UtcNow;
        var actor = await services.GetRequiredService<IWorkflowExecutionActorProvider>().GetAgentAsync(new(identity.WorkflowExecutionId,
            WorkflowExecutionActorActivationReason.Start, now, "test", WorkflowExecutionActorCapabilities.InProcessMailbox, partition: fixture.Recipient.Partition));
        await Assert.ThrowsAsync<InvalidDataException>(() => actor.EnqueueAsync(new(identity.EnvelopeId, identity.WorkflowExecutionId,
            new(changed.InputId, identity.WorkflowExecutionId, WorkflowExecutionCommandKind.ExecuteModel, now, JsonSerializer.SerializeToElement(changed, Web),
                new Dictionary<string, string>()), identity.IdempotencyKey, WorkflowExecutionCommandDeliveryMode.AtLeastOnce, now,
            partition: fixture.Recipient.Partition)).AsTask());
        Assert.Null(await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(identity.WorkflowExecutionId));
        Assert.Empty((await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListAsync(new(identity.WorkflowExecutionId))).Items);
        Assert.Equal(RuntimeRecipientPhase.DispatchCommitted, Assert.Single(source.SubscriptionDelivery!.Progress).Phase);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Malformed_retained_model_commands_preserve_cause_and_delivery_context(bool queued, bool invalidObject)
    {
        await using var host = await OpenAsync(); await using var scope = await ScopeAsync(host);
        var services = scope.ServiceProvider;
        var fixture = await PrepareModelIngressAsync(services);
        var target = services.GetServices<IStandaloneTriggerRuntimeTarget>().Single(item => item.ModelKey == "bpmn");
        var seal = await target.PrepareDispatchAsync(fixture.Source, fixture.Recipient);
        var malformed = invalidObject ? JsonSerializer.SerializeToElement(new { }) :
            JsonSerializer.SerializeToElement(new { executable = "malformed-pin" });
        var claim = Assert.Single(await services.GetRequiredService<IRuntimePostCommitOutboxClaimStore>()
            .ClaimAsync(new("malformed-model-delivery", DateTimeOffset.UtcNow, TimeSpan.FromMinutes(1), 10)));
        var source = await fixture.Port.CommitDispatchAsync(new(claim, fixture.Scope.Persistence), seal);
        if (!queued)
        {
            // The actual first-party store rejects corrupt seals before this boundary. Exercise the
            // target adapter against malformed bytes returned by a replacement provider instead.
            seal = new(seal.Version, seal.SourceOutboxItemId, seal.SourceFingerprint, seal.SelectionFingerprint,
                seal.RecipientId, seal.StartIdentity, seal.CommittedAt, seal.CommandProtocol, seal.CommandProtocolVersion, malformed);
            var replacement = DeliveryFake<IRuntimeIngressDeliveryStore>((method, _) =>
            {
                Assert.Equal(nameof(IRuntimeIngressDeliveryStore.ReadCommittedRecipientAsync), method.Name);
                return ValueTask.FromResult(new RuntimeCommittedRecipient(source, fixture.Recipient, seal));
            });
            target = ActivatorUtilities.CreateInstance<BpmnTriggerTarget>(services, replacement);
        }
        if (queued)
        {
            var identity = fixture.Recipient.StartIdentity;
            await services.GetRequiredService<IWorkflowSchedulerWorkQueue>().EnqueueAsync(new(
                RuntimeModelExecutionCommand.CreateSchedulerWorkItemId(identity.InputId), identity.WorkflowExecutionId,
                identity.InputId, WorkflowExecutionCommandKind.ExecuteModel, identity.EnvelopeId, identity.IdempotencyKey,
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, payload: malformed));
        }
        var error = await Assert.ThrowsAsync<BpmnModelValidationException>(() =>
            target.ReconcileAsync(source, fixture.Recipient, seal).AsTask());
        if (invalidObject) Assert.IsAssignableFrom<ArgumentException>(error.InnerException);
        else Assert.IsAssignableFrom<JsonException>(error.InnerException);
        Assert.Contains(source.OutboxItemId, error.Message);
        Assert.Contains(fixture.Recipient.RecipientId, error.Message);
        Assert.Contains(fixture.Scope.Persistence.Value, error.Message);
        Assert.Contains(queued ? "queued" : "sealed", error.Message);
        Assert.Null(await services.GetRequiredService<IRuntimeModelExecutionReader>().FindAsync(fixture.Recipient.StartIdentity.WorkflowExecutionId));
        await AssertNativeEmptyAsync(services);
    }

    private sealed record ModelIngressFixture(IRuntimeIngressDeliveryStore Port, TriggerScope Scope, RuntimePostCommitOutboxItem Source,
        RuntimeSubscriptionRecipient Recipient, RuntimeExecutionMaterial Material, TriggerSubscription Subscription, DateTimeOffset CreatedAt)
    {
        public StandaloneTrigger Trigger(long revision = 1, TriggerDesiredState state = TriggerDesiredState.Enabled) => new("model-trigger", "Model trigger", "event", 1,
            JsonSerializer.SerializeToElement(new { }), revision, 1, 1, state, new(TriggerManagementKind.Studio), CreatedAt, CreatedAt, "creator", tenantId: Scope.TenantId);
    }

    private static async Task<ModelIngressFixture> PrepareModelIngressAsync(IServiceProvider services, string? tenantId = null,
        RuntimeExecutionMaterial? preparedMaterial = null, IReadOnlyDictionary<string, JsonElement>? preparedInputs = null)
    {
        var port = services.GetRequiredService<IRuntimeIngressDeliveryStore>();
        Assert.True(port.SupportsAdmission);
        var scope = new TriggerScope(services.GetRequiredService<IPersistenceAccessContextAccessor>().Current.RequireScope(), tenantId);
        var material = preparedMaterial ?? await services.GetRequiredService<IBpmnModelRuntime>().PreparePublicationAsync(
            Material().Definition.Deserialize<BpmnModelDefinition>(Web)!, new(1, [new("text", new("String"), true)]), [], new("operator", tenantId));
        var now = DateTimeOffset.UtcNow;
        const string definition = "triggered-model-definition", activation = "triggered-model-publication";
        var reference = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(activation), material.Identity.ArtifactId,
            "BpmnModel", "triggered-model-version", "1", definition, "triggered-model-version", "1", now, now, WorkflowExecutableReferenceScope.Published, TenantId: tenantId);
        var published = await services.GetRequiredService<IWorkflowActivationCoordinator>().ActivateMaterialAsync(new(definition, material, reference,
            "default", activation, WorkflowActivationSource.Publishing, 0));
        Assert.True(published.Succeeded);
        reference = (await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().FindAsync(reference.SourceReferenceId))!;
        var subscription = new TriggerSubscription("model-subscription", "model-trigger", definition, "default", "bpmn", 1, 1, true, [], new(TriggerManagementKind.Studio),
            new("model-grant", "model-trigger", "model-subscription", definition, "default", "bpmn", "event", 1, TriggerGrantStatus.Granted, "creator", now));
        var fixture = new ModelIngressFixture(port, scope, null!, null!, material, subscription, now);
        var trigger = fixture.Trigger();
        await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(scope, new("create-model-trigger", "create", "creator", null, trigger, [subscription]));
        var actor = new WorkflowExecutionAuthoritySnapshot("test-event-source", "source-actor");
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { text = "trigger-value" });
        var metadata = JsonSerializer.SerializeToElement(new { });
        var envelope = JsonSerializer.SerializeToElement(new
        {
            payload, metadata, submittedIntent = RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata),
            source = new { family = "Test.Event", key = "model-event", recordedAt = now, actor },
            trigger = new { trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Revision,
                trigger.ConfigurationRevision, trigger.AssociationRevision, trigger.Configuration }
        }, Web);
        var source = new RuntimeStandaloneIngressSource(1, "model-admission", "model-event", now, scope.Persistence, tenantId, new(scope.Persistence.Value), trigger.Id, 1, 1,
            "Test.Event", "model-event", RuntimeStandaloneIngressSource.ComputeRequestFingerprint(scope.Persistence, tenantId, trigger.Id, "Test.Event", "model-event", schema,
                RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata), actor),
            schema, envelope, RuntimeIngressAdmissionPolicy.Independent, actor);
        var identity = new RuntimeSubscriptionStartIdentity(1, scope.Persistence, source.OutboxItemId, subscription.Id);
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, subscription.Id, 1, 1, definition, "bpmn", published.Slot.SlotId, published.Slot.Revision, activation,
            material.Identity, WorkflowExecutableSourceProvenance.From(reference), RuntimeIngressAdmission.ComputeInputContractFingerprint(material.Model!.InputContract), 1, 1,
            preparedInputs ?? new Dictionary<string, JsonElement> { ["text"] = JsonSerializer.SerializeToElement("trigger-value") },
            new("standalone-trigger:" + trigger.Id, actor.RootInitiator, new Dictionary<string, string>
            { ["triggerId"] = trigger.Id, ["subscriptionId"] = subscription.Id, ["grantId"] = subscription.Grant!.Id, ["sourceFamily"] = source.SourceFamily }),
            tenantId, new(scope.Persistence.Value), identity, material.Model.Protocol, material.Model.Profile, material.IncarnationId);
        var delivery = new RuntimeSubscriptionDelivery(1, source.OutboxItemId, source.Fingerprint, scope.Persistence, [recipient], [],
            [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        var accepted = await port.AdmitAsync(new(scope, 1, RuntimePostCommitOutboxItem.CreateStandaloneIngress(source, delivery,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(1))), [new(subscription.Id, definition, "default", published.Slot, reference)]));
        return fixture with { Source = accepted.Item, Recipient = recipient };
    }
}
