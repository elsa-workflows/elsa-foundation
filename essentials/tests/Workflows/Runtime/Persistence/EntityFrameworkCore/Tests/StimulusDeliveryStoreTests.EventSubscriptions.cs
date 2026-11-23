using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.Activities;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Services.Triggers;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Combined_Event_selection_preserves_original_workflow_source_and_installs_both_arms_once(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareWorkflowEventAsync(harness);
        var installed = await fixture.Port.InstallWorkflowCohortAsync(fixture.Source, fixture.Ordinary, fixture.Independent);
        var row = (await harness.Lookup.FindDurableAsync(fixture.Source.SourceOutboxItemId))!;
        Assert.Null(row.StandaloneIngress);
        Assert.Equal("publisher", row.RequireWorkflowIntent().WorkflowExecutionId);
        Assert.Equal(fixture.Source.IntentFingerprint, row.SourceFingerprint);
        Assert.Equal("workflow-broadcast:send", row.OutboxItemId);
        var ordinary = Assert.Single(row.RecipientSnapshot!.Starts);
        Assert.Equal(EventStimulus.StimulusType, ordinary.Binding.StimulusType);
        Assert.Equal(EventStimulus.Hash("orders"), ordinary.Binding.StimulusHash);
        Assert.True(fixture.Independent.Delivery.IsEquivalentTo(row.SubscriptionDelivery));
        Assert.Equal(["accepted", "refused"], Assert.Single(row.SubscriptionDelivery!.ResourceCaptures!).SubscriptionIds);
        Assert.Equal(2, (await harness.Delivery.ListPinnedExecutableArtifactIdsAsync()).Count);

        var current = (await fixture.Resources.FindAsync(fixture.Independent.Scope, "ingress-trigger"))!;
        await fixture.Resources.WriteAsync(fixture.Independent.Scope,
            new("pause-after-selection", "pause-after-selection", "operator", 2, EventResource(current.Trigger, 3, false), []));
        await AddMatchingEventResourceAsync(fixture.Resources, fixture.Independent.Scope);
        var losingEmpty = fixture.Independent with
        {
            Delivery = new(1, row.OutboxItemId, row.SourceFingerprint, Scope, [], [], [], resourceCaptures: []), Triggers = []
        };
        var replay = await fixture.Port.InstallWorkflowCohortAsync(installed, fixture.Ordinary, losingEmpty);
        Assert.True(row.SubscriptionDelivery.IsEquivalentTo(replay.Claim.Item.SubscriptionDelivery));
        Assert.Single(replay.Claim.Item.SubscriptionDelivery!.Recipients);
        Assert.Equal(2, replay.Claim.Item.SubscriptionDelivery.ResourceCaptures![0].TriggerRevision);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Newly_matching_Event_resource_before_acceptance_refuses_the_whole_pair_without_effects(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareWorkflowEventAsync(harness);
        harness.BeforeAcceptance = () => AddMatchingEventResourceAsync(fixture.Resources, fixture.Independent.Scope);
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => fixture.Port.InstallWorkflowCohortAsync(
            fixture.Source, fixture.Ordinary, fixture.Independent).AsTask());
        Assert.IsType<TriggerOperationException>(refusal.InnerException);
        var current = (await harness.Lookup.FindDurableAsync(fixture.Source.SourceOutboxItemId))!;
        Assert.Null(current.RecipientSnapshot);
        Assert.Null(current.SubscriptionDelivery);
        Assert.Equal(RuntimePostCommitOutboxStatus.Delivering, current.Status);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_only_installer_cannot_create_a_partial_new_Event_pair(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareWorkflowEventAsync(harness);
        var refusal = await Assert.ThrowsAsync<StimulusDeliveryBoundaryException>(() => harness.Delivery.InstallAsync(fixture.Source, fixture.Ordinary).AsTask());
        Assert.IsType<NotSupportedException>(refusal.InnerException);
        var current = (await harness.Lookup.FindDurableAsync(fixture.Source.SourceOutboxItemId))!;
        Assert.Null(current.RecipientSnapshot);
        Assert.Null(current.SubscriptionDelivery);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Local_Event_persists_empty_independent_arm_and_never_selects_matching_resources(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var fixture = await PrepareWorkflowEventAsync(harness, local: true);
        var installed = await fixture.Port.InstallWorkflowCohortAsync(fixture.Source, fixture.Ordinary, fixture.Independent);
        Assert.NotNull(installed.Claim.Item.RecipientSnapshot);
        Assert.Empty(installed.Claim.Item.RecipientSnapshot!.Starts);
        Assert.Empty(installed.Claim.Item.SubscriptionDelivery!.Recipients);
        Assert.Empty(installed.Claim.Item.SubscriptionDelivery.Refusals);
        Assert.Empty(installed.Claim.Item.SubscriptionDelivery.ResourceCaptures!);
    }

    private sealed record WorkflowEventFixture(IRuntimeIngressDeliveryStore Port, IStandaloneTriggerStore Resources,
        PostCommitDeliveryContext Source, StimulusDeliverySnapshot Ordinary, RuntimeWorkflowSubscriptionCandidate Independent);

    private static async Task<WorkflowEventFixture> PrepareWorkflowEventAsync(Harness harness, bool local = false)
    {
        // Real selected provider fixture: publication setup is explicit; selection acceptance uses the production checked writer.
        var prepared = await PrepareIngressAsync(harness);
        var scope = prepared.Request.Scope;
        var resource = (await prepared.Resources.FindAsync(scope, "ingress-trigger"))!;
        var configured = EventResource(resource.Trigger, 2, true);
        await prepared.Resources.WriteAsync(scope, new("configure-event", "configure-event", "operator", 1, configured, resource.Subscriptions));
        resource = (await prepared.Resources.FindAsync(scope, "ingress-trigger"))!;
        await harness.EnsureMaterial();
        var publisher = Publisher with { Authority = new("publisher-system", "original-initiator"), Partition = new(Scope.Value) };
        await harness.SavePublisher(publisher);
        using var adapterProvider = EventAdapterProvider();
        var adapters = adapterProvider.GetServices<IStimulusDeliveryIntentAdapter>().ToArray();
        var composition = harness.Ingress(adapters);
        var payload = JsonSerializer.SerializeToElement(new PublishStimulusIntentPayload(EventStimulus.StimulusType, EventStimulus.Hash("orders"),
            "orders", null, JsonSerializer.SerializeToElement(new { value = 42 }), local), new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var intent = new RuntimePostCommitIntent("send", "publisher", PublishStimulusConstants.PublishStimulusIntentKind, Now,
            "publisher-activity", "workflow-broadcast-key", payload);
        var pending = new RuntimePostCommitOutboxItem("workflow-broadcast:send", intent, RuntimePostCommitOutboxStatus.Pending, Now, Now,
            RuntimePostCommitRetryPolicy.UntilAcknowledged(TimeSpan.FromSeconds(5)), 0, null, null, null, null, null, 0, null, 1, Scope, null, null, 1, null);
        await harness.SavePending(pending);
        var source = new PostCommitDeliveryContext(Assert.Single(await harness.Claims.ClaimAsync(new("event-owner", Now, TimeSpan.FromMinutes(1), 10))), Scope);
        var ordinary = new StimulusDeliverySnapshot(1, source.SourceOutboxItemId, source.IntentFingerprint, StimulusDeliveryScope.From(publisher, Scope), Now,
            EventStimulus.StimulusType, EventStimulus.Hash("orders"), local ? [] : [Start("ordinary-binding", EventStimulus.StimulusType, EventStimulus.Hash("orders"))], []);
        if (local) return new(composition.Port, composition.Resources, source, ordinary,
            new(scope, null, WorkflowStimulusSubscriptionSelector.Empty(source), []));
        var descriptor = Assert.Single(adapters).CreateSubscriptionSource(intent)!;
        var old = Assert.Single(prepared.Request.Candidate.SubscriptionDelivery!.Recipients);
        var identity = new RuntimeSubscriptionStartIdentity(1, Scope, source.SourceOutboxItemId, old.SubscriptionId);
        var recipient = new RuntimeSubscriptionRecipient(identity.Digest, old.SubscriptionId, old.SubscriptionRevision, old.GrantRevision,
            old.LogicalDefinitionId, old.ModelKey, old.SlotId, old.SlotRevision, old.ActivationId, old.Material, old.SourceProvenance,
            old.InputContractFingerprint, old.MappingRevision, old.MappingVersion, old.Inputs,
            new("standalone-trigger:ingress-trigger", publisher.Authority!.RootInitiator, new Dictionary<string, string>
            { ["triggerId"] = "ingress-trigger", ["subscriptionId"] = "accepted", ["grantId"] = "grant-accepted", ["sourceFamily"] = descriptor.SourceFamily }),
            scope.TenantId, publisher.Partition!, identity);
        var delivery = new RuntimeSubscriptionDelivery(1, source.SourceOutboxItemId, source.IntentFingerprint, Scope, [recipient],
            [new("refused", 1, "Admission", "target-unpublished")], [new(recipient.RecipientId, RuntimeRecipientPhase.Pending)], resourceCaptures:
            [new("ingress-trigger", 2, 2, 2, ["accepted", "refused"], WorkflowStimulusSubscriptionSelector.TriggerEnvelope(configured))]);
        var independent = new RuntimeWorkflowSubscriptionCandidate(scope, descriptor, delivery,
            [new(resource, prepared.Request.Targets)]);
        return new(composition.Port, composition.Resources, source, ordinary, independent);
    }

    private static StandaloneTrigger EventResource(StandaloneTrigger current, long revision, bool enabled) =>
        new(current.Id, current.Name, "event", 1, JsonSerializer.SerializeToElement(new { eventName = "orders" }), revision, revision, revision,
            enabled ? TriggerDesiredState.Enabled : TriggerDesiredState.Paused, current.Origin, current.CreatedAt, Now,
            current.CreatedBy, current.Description, current.HasActivated, current.TenantId);

    private static async Task AddMatchingEventResourceAsync(IStandaloneTriggerStore resources, TriggerScope scope)
    {
        var trigger = new StandaloneTrigger("late-resource", "Late Event", "event", 1, JsonSerializer.SerializeToElement(new { eventName = "orders" }),
            1, 1, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: scope.TenantId);
        var subscription = new TriggerSubscription("late-association", trigger.Id, "ingress-definition", "Published", "hint", 1, 1, true, [],
            new(TriggerManagementKind.Studio), new("late-grant", trigger.Id, "late-association", "ingress-definition", "Published", "hint", "event", 1,
                TriggerGrantStatus.Granted, "grant-author", Now));
        await resources.WriteAsync(scope, new("late-create", "late-create", "operator", null, trigger, [subscription]));
    }

    private static ServiceProvider EventAdapterProvider()
    {
        var services = new ServiceCollection(); new ActivitiesPrimitivesFeature().ConfigureServices(services);
        return services.BuildServiceProvider();
    }
}
