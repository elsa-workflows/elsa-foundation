using Elsa.Events.Core.Contracts;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Primitives.Models;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliverySchemaGateTests
{
    [Theory]
    [InlineData(RuntimeSchedulerWorkEfModule.SchemaFamily, "3.0.0")]
    [InlineData(RuntimeWorkflowExecutionEfModule.SchemaFamily, "2.0.0")]
    [InlineData(RuntimeModelStateEfModule.SchemaFamily, "1.0.0")]
    public async Task Independently_finalized_outbox_cannot_activate_or_accept_ingress_until_every_selected_writer_is_ready(
        string laggingFamily, string previousVersion)
    {
        await using var fixture = await Fixture.CreateIngressReadiness(laggingFamily, previousVersion);
        await using var context = fixture.Open();
        using var targetMetadata = new NativeTargetMetadataFixture();
        var prepared = await PrepareGatedIngressAsync(context, targetMetadata.Publisher);
        Assert.Equal(RuntimePostCommitOutboxEfModule.SchemaVersion, fixture.Gate.StateOf(Family)!.WriteVersion);
        Assert.Equal(previousVersion, fixture.Gate.StateOf(laggingFamily)!.WriteVersion);
        Assert.False(prepared.Ingress.SupportsAdmission);
        Assert.False(prepared.Source.SupportsActivatedResources);
        await Assert.ThrowsAsync<NotSupportedException>(() => prepared.Source.CommitAsync(prepared.Scope, prepared.Activation, []).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => prepared.Ingress.AdmitAsync(prepared.Request).AsTask());
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.WorkflowExecutableRootWriteLeases.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, await context.TriggerMutations.CountAsync());
        Assert.Equal(TriggerDesiredState.Paused, (await prepared.Resources.FindAsync(prepared.Scope, prepared.Activation.Trigger.Id))!.Trigger.DesiredState);

        // The same physical composition observes the last independently finalized family without replacing its stores.
        await fixture.Finalize();
        Assert.True(prepared.Ingress.SupportsAdmission);
        Assert.True(prepared.Source.SupportsActivatedResources);
        await prepared.Source.CommitAsync(prepared.Scope, prepared.Activation, []);
        var accepted = await prepared.Ingress.AdmitAsync(prepared.Request);
        Assert.False(accepted.Replayed);
        Assert.Equal(prepared.Request.Candidate.OutboxItemId, accepted.Item.OutboxItemId);
        Assert.Single(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
        Assert.Empty(await context.SchedulerWorkItems.AsNoTracking().ToArrayAsync());
        Assert.Equal(TriggerDesiredState.Enabled, (await prepared.Resources.FindAsync(prepared.Scope, prepared.Activation.Trigger.Id))!.Trigger.DesiredState);
    }

    private static async Task<GatedIngressPreparation> PrepareGatedIngressAsync(RuntimeDbContext context, IInlineEventPublisher publisher)
    {
        var access = new Accessor(); var clock = new FakeTimeProvider(Now); var scope = new TriggerScope(Scope, Scope.Value);
        var materials = new EfWorkflowExecutableStore(context, access);
        var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new NoTokens(), clock);
        var executions = new EfWorkflowExecutionStateStore(context, access, new NoTokens());
        var roots = new WorkflowExecutableRootWriteLeaseManager(materials, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), clock);
        var outbox = new EfRuntimePostCommitOutboxStore(context, access, clock, references, roots);
        var slots = new EfWorkflowActivationAuthority(context, access);
        var resources = new EfStandaloneTriggerStore(context, access, clock);
        var queue = new EfSchedulerWorkQueueStore(context, access, new NoTokens());
        var evidence = new NativeIngressTargetEvidence(queue, executions, clock, executables: materials);
        var ingress = new EfRuntimeIngressDeliveryStore(context, resources, outbox, slots, materials, references, executions,
            roots, access, clock, evidence);
        var material = new WorkflowExecutable(new("readiness-material", "material-owner", "material-version", "1", "hash"),
            new ExecutableNode("node", "authored", "test/activity", "1.0.0", new RuntimeActivityDescriptor("test", "1.0.0", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(), new(1, []), [],
            incidentStrategy: IncidentStrategyBuiltIns.FaultReference);
        await materials.SaveAsync(material);
        var slot = (await slots.TryActivateAsync(new("readiness-definition", "Published", "readiness-publication",
            WorkflowActivationSource.Publishing, 0, Now))).Slot;
        var reference = new WorkflowExecutableSourceReference(WorkflowActivationReferenceIdentity.Create(slot.ActiveActivationId!),
            material.Identity.ArtifactId, "definition", "readiness-definition", "1", "readiness-definition", "published-version", "1",
            Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: slot.ActiveActivationId, SlotId: slot.SlotId, TenantId: Scope.Value);
        await references.SaveAsync(reference);
        StandaloneTrigger Trigger(bool enabled) => new("readiness-trigger", "Readiness trigger", "event", 1, JsonSerializer.SerializeToElement(new { }),
            enabled ? 2 : 1, 1, 1, enabled ? TriggerDesiredState.Enabled : TriggerDesiredState.Paused,
            new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: enabled, tenantId: Scope.Value);
        var trigger = Trigger(true);
        var subscription = new TriggerSubscription("readiness-subscription", trigger.Id, "readiness-definition", "Published", "native", 1, 1,
            true, [], new(TriggerManagementKind.Studio), new("readiness-grant", trigger.Id, "readiness-subscription", "readiness-definition",
                "Published", "native", "event", 1, TriggerGrantStatus.Granted, "grant-author", Now));
        await resources.WriteAsync(scope, new("seed", "seed", "operator", null, Trigger(false), [subscription]));
        var resolver = new TriggerTargetResolver(slots, references, materials, access, clock, publisher);
        var target = await resolver.ResolveAsync(scope, subscription.DefinitionId, subscription.SlotName, CancellationToken.None);
        var activation = new TriggerStoreWrite("enable", "enable", "operator", 1, trigger, [subscription])
        { ObservedTargets = [TriggerManagementTargets.Observation(subscription.DefinitionId, subscription.SlotName, target)] };
        var bindings = new EfWorkflowTriggerBindingStore(context, access);
        var source = new EfTriggerSourceResourceCommitStore(context, resources, ingress,
            new EfWorkflowActivationSwitch(slots, bindings, references, access, clock), bindings, access, clock, [],
            targets: new TriggerManagementTargets(resolver, new TriggerInputMapper(new WorkflowExecutableInputValidator())));
        var actor = new WorkflowExecutionAuthoritySnapshot("source-module", "initiator");
        var schema = new ValueTypeDescriptor("Test.Payload", schemaVersion: 1);
        var payload = JsonSerializer.SerializeToElement(new { value = 42 });
        var metadata = JsonSerializer.SerializeToElement(new { });
        var submitted = RuntimeIngressAdmission.ComputeSubmittedIntent(payload, metadata);
        var envelope = JsonSerializer.SerializeToElement(new
        {
            payload, metadata, submittedIntent = submitted,
            source = new { family = "Readiness.Source", key = "occurrence", recordedAt = Now, actor },
            trigger = new { trigger.Id, trigger.Name, trigger.TypeKey, trigger.SchemaVersion, trigger.Revision,
                trigger.ConfigurationRevision, trigger.AssociationRevision, trigger.Configuration }
        }, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var origin = new RuntimeStandaloneIngressSource(1, "readiness-admission", "dispatch", Now, Scope, Scope.Value, new(Scope.Value),
            trigger.Id, trigger.Revision, trigger.AssociationRevision, "Readiness.Source", "occurrence",
            RuntimeStandaloneIngressSource.ComputeRequestFingerprint(Scope, Scope.Value, trigger.Id, "Readiness.Source", "occurrence", schema, submitted, actor),
            schema, envelope, RuntimeIngressAdmissionPolicy.Independent, actor);
        var identity = new RuntimeSubscriptionStartIdentity(1, Scope, origin.OutboxItemId, subscription.Id);
        var authority = new WorkflowExecutionAuthoritySnapshot("standalone-trigger:" + trigger.Id, actor.RootInitiator,
            new Dictionary<string, string> { ["triggerId"] = trigger.Id, ["subscriptionId"] = subscription.Id,
                ["grantId"] = subscription.Grant!.Id, ["sourceFamily"] = origin.SourceFamily });
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, subscription.Id, 1, 1, subscription.DefinitionId, "native",
            slot.SlotId, slot.Revision, slot.ActiveActivationId!, material.Identity, WorkflowExecutableSourceProvenance.From(reference),
            RuntimeIngressAdmission.ComputeInputContractFingerprint(material.InputContract!), 1, 1, new Dictionary<string, JsonElement>(), authority,
            Scope.Value, new(Scope.Value), identity);
        var selection = new RuntimeSubscriptionDelivery(1, origin.OutboxItemId, origin.Fingerprint, Scope, [recipient], [],
            [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)]);
        var request = new RuntimeIngressAdmissionRequest(scope, trigger.Revision, RuntimePostCommitOutboxItem.CreateStandaloneIngress(origin,
            selection, RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5))),
            [new(subscription.Id, subscription.DefinitionId, subscription.SlotName, slot, reference)]);
        return new(scope, resources, ingress, source, activation, request);
    }

    private sealed record GatedIngressPreparation(TriggerScope Scope, IStandaloneTriggerStore Resources,
        IRuntimeIngressDeliveryStore Ingress, ITriggerSourceResourceCommitStore Source, TriggerStoreWrite Activation,
        RuntimeIngressAdmissionRequest Request);

    private sealed partial class Fixture
    {
        internal static async Task<Fixture> CreateIngressReadiness(string laggingFamily, string previousVersion)
        {
            var fixture = new Fixture(laggingFamily); await fixture.connection.OpenAsync();
            await using (var context = fixture.Open())
            {
                await context.Database.EnsureCreatedAsync();
                var store = new EfSchemaFinalizationStore(context);
                foreach (var familyName in IngressFamilies)
                {
                    var chain = fixture.Gate.Families.Chains.Single(item => item.Family == familyName);
                    var initial = familyName == laggingFamily ? previousVersion : chain.ReadableVersions[0];
                    var record = await store.GetOrCreateAsync(familyName, initial, chain.ReadableVersions, SchemaFinalizationActor.OfOperator("test"));
                    await store.PlaceHoldAsync(familyName, record.Revision, null, "independent ingress rollout", "test", chain.ReadableVersions);
                }
                await fixture.Gate.ActivateAsync(context); fixture.gates.Register(typeof(RuntimeDbContext), fixture.Gate);
            }
            foreach (var familyName in IngressFamilies.Where(item => item != laggingFamily)) await fixture.Finalize(familyName);
            return fixture;
        }

        private static readonly string[] IngressFamilies = [RuntimePostCommitOutboxEfModule.SchemaFamily, RuntimeSchedulerWorkEfModule.SchemaFamily,
            RuntimeWorkflowExecutionEfModule.SchemaFamily, RuntimeModelStateEfModule.SchemaFamily, RuntimeArtifactEfModule.SchemaFamily,
            RuntimeOperationalStateEfModule.SchemaFamily];
    }
}
