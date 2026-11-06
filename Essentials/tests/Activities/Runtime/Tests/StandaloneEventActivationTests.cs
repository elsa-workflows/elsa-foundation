using System.Text.Json;
using Elsa.Activities.Primitives;
using Elsa.Activities.Primitives.StandaloneTriggers;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Activities.Runtime.Tests;

public sealed class StandaloneEventActivationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("event-namespace"), "actual-event-tenant");

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task Only_actual_workflow_Event_intake_with_its_coherent_provider_can_activate_through_normal_management(bool runtimeSource, bool unsupported)
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now)); services.AddWorkflowRuntime();
        new StandaloneEventTriggerAuthoringFeature().ConfigureServices(services);
        if (runtimeSource) new ActivitiesPrimitivesFeature().ConfigureServices(services);
        if (unsupported) { services.RemoveAll<IRuntimeIngressDeliveryStore>(); services.AddScoped<IRuntimeIngressDeliveryStore, Unsupported>(); }
        using var provider = services.BuildServiceProvider(); using var operation = provider.CreateScope(); var sp = operation.ServiceProvider;
        sp.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(Scope.Persistence));
        var management = sp.GetRequiredService<ITriggerManagementCommitter>();
        Assert.Equal(runtimeSource && !unsupported, management.CanActivateType("event", 1));
        Assert.Empty(sp.GetServices<StandaloneTriggerSourceDescriptor>()); // Canonical workflow source; no fake external producer port.
        var material = new WorkflowExecutable(new("event-material", "event-producer", "event-version", "1", "hash"),
            new("root", "authored", "test/activity", "1", new("test", "1", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()), new Dictionary<string, WorkflowExecutableResumeTarget>(),
            Now, new Dictionary<string, string>(), new(1, []), [], IncidentStrategyBuiltIns.FaultReference);
        await sp.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(material);
        var slot = (await sp.GetRequiredService<IWorkflowActivationAuthority>().TryActivateAsync(new("event-target", "Published", "activation",
            WorkflowActivationSource.Publishing, 0, Now))).Slot;
        await sp.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new(WorkflowActivationReferenceIdentity.Create("activation"),
            material.Identity.ArtifactId, "definition", "event-target", "1", "event-target", "publication", "1", Now, Now,
            WorkflowExecutableReferenceScope.Published, ActivationId: "activation", SlotId: slot.SlotId, TenantId: Scope.TenantId));
        var trigger = new StandaloneTrigger("event-resource", "Orders", "event", 1, JsonSerializer.SerializeToElement(new { eventName = "orders" }),
            1, 1, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: Scope.TenantId);
        var subscription = new TriggerSubscription("event-subscription", trigger.Id, "event-target", "Published", "native", 1, 1, true, [],
            new(TriggerManagementKind.Studio), new("event-grant", trigger.Id, "event-subscription", "event-target", "Published", "native", "event", 1,
                TriggerGrantStatus.Granted, "grant-author", Now));
        var write = new TriggerStoreWrite("enable", "enable-event", "operator", null, trigger, [subscription]);
        if (!runtimeSource || unsupported)
        {
            await Assert.ThrowsAsync<TriggerOperationException>(() => management.CommitAsync(Scope, write).AsTask());
            Assert.Null(await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, trigger.Id));
        }
        else
        {
            await management.CommitAsync(Scope, write);
            var stored = (await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, trigger.Id))!;
            Assert.True(stored.Trigger.HasActivated);
            Assert.Equal(TriggerDesiredState.Enabled, stored.Trigger.DesiredState);
            Assert.Equal(subscription.Id, Assert.Single((await sp.GetRequiredService<IStandaloneTriggerStore>()
                .ListSubscriptionsAsync(Scope, new(DefinitionId: "event-target"))).Items).Id);
        }
        Assert.Empty(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
    }

    private sealed class Unsupported : IRuntimeIngressDeliveryStore;
}
