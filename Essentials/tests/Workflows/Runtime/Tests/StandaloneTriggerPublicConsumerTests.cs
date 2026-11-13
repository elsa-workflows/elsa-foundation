using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Activities.Runtime.Core.Models;
using Elsa.Events;
using Elsa.Serialization.Core;
using Elsa.Serialization.SystemText;
using Elsa.Tasks.Core;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Services.Triggers.Management;
using IndependentTriggers.Consumer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Tests;

public sealed class StandaloneTriggerPublicConsumerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 6, 10, 0, 0, TimeSpan.Zero);
    private static readonly TriggerScope Scope = new(new("consumer-namespace"), "independent-tenant");
    private static readonly StandaloneTriggerInvocationContext Trusted = new(Scope, new(Scope.Persistence.Value), new("consumer-producer", "initiator"));

    [Fact]
    public void Consumer_is_compiled_against_Core_without_Runtime_implementation_or_friend_access()
    {
        var fixture = typeof(ConsumerModule).Assembly;
        Assert.Contains(fixture.GetReferencedAssemblies(), assembly => assembly.Name == "Elsa.Workflows.Runtime.Core");
        Assert.DoesNotContain(fixture.GetReferencedAssemblies(), assembly => assembly.Name == "Elsa.Workflows.Runtime");
        Assert.DoesNotContain(typeof(IRuntimeIngressDeliveryStore).Assembly.GetCustomAttributes<InternalsVisibleToAttribute>(),
            attribute => new AssemblyName(attribute.AssemblyName).Name == fixture.GetName().Name);
    }

    [Fact]
    public async Task Public_consumer_source_and_adapter_use_the_actual_physical_admission_and_keep_captured_authority_on_replay()
    {
        var services = RuntimeServices();
        var selected = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStandaloneTriggerRuntimeTarget));
        services.Remove(selected);
        ConsumerModule.AddTargetDecorator(services, provider => (IStandaloneTriggerRuntimeTarget)(selected.ImplementationFactory?.Invoke(provider) ??
            ActivatorUtilities.CreateInstance(provider, selected.ImplementationType!)));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope(); var sp = scope.ServiceProvider;
        Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        // These are detached data fields, not source context, grants, material or target authority.
        var payload = JsonSerializer.SerializeToElement(new { message = "hello", tenantId = "forged", grantId = "forged" });
        var accepted = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        Assert.False(accepted.Replayed);
        Assert.Equal(1, accepted.AcceptedCount);
        Assert.Equal(StandaloneTriggerIngressStatus.Accepted, accepted.Status);
        var publicJson = JsonSerializer.Serialize(accepted);
        foreach (var privateField in new[] { "SourceFingerprint", "SelectionFingerprint", "Authority", "Material", "Seal", "CanonicalPayload" })
            Assert.DoesNotContain(privateField, publicJson, StringComparison.OrdinalIgnoreCase);
        var acceptedItem = await ReadAcceptedAsync(sp, accepted, "first");
        Assert.Null(acceptedItem.Intent);
        Assert.Equal(RuntimeDeliverySourceKind.StandaloneIngress, acceptedItem.SourceKind);
        Assert.Equal(Scope.Persistence, acceptedItem.SourceScope);
        Assert.Equal(Scope.TenantId, acceptedItem.StandaloneIngress!.TenantId);
        var recipient = Assert.Single(acceptedItem.SubscriptionDelivery!.Recipients);
        Assert.Equal("hello", recipient.Inputs["message"].GetString());
        Assert.Equal("standalone-trigger:consumer-trigger", recipient.Authority.SystemIdentity);
        Assert.Equal(Trusted.Actor.RootInitiator, recipient.Authority.RootInitiator);
        Assert.Equal("consumer-grant", recipient.Authority.Metadata["grantId"]);
        Assert.Equal(1, sp.GetRequiredService<ConsumerProbe>().CapabilityChecks);
        Assert.True(acceptedItem.IsEquivalentTo((await sp.GetRequiredService<IPostCommitOutboxLookupStore>().FindDurableAsync(acceptedItem.OutboxItemId))!));

        var target = Assert.IsType<ConsumerTarget>(Assert.Single(sp.GetServices<IStandaloneTriggerRuntimeTarget>()));
        var seal = await target.PrepareDispatchAsync(acceptedItem, recipient);
        var claims = sp.GetRequiredService<IRuntimePostCommitOutboxClaimStore>();
        var claim = Assert.Single(await claims.ClaimAsync(new("consumer", Now, TimeSpan.FromMinutes(1), 10)));
        var committed = await sp.GetRequiredService<IRuntimeIngressDeliveryStore>().CommitDispatchAsync(new(claim, Scope.Persistence), seal);
        Assert.Equal(RuntimeRecipientPhase.DispatchCommitted, Assert.Single(committed.SubscriptionDelivery!.Progress).Phase);
        Assert.Equal(1, sp.GetRequiredService<ConsumerProbe>().PreparedCommands);

        var resource = (await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "consumer-trigger"))!;
        await sp.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(Scope, new("pause", "pause", "operator", 1,
            Resource(2, false), resource.Subscriptions));
        await sp.GetRequiredService<IWorkflowActivationAuthority>().TryDeactivateAsync("consumer-definition", "Published", WorkflowActivationSource.Publishing, 1, Now);
        var replay = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        Assert.True(replay.Replayed);
        var replayItem = await ReadAcceptedAsync(sp, replay, "first");
        Assert.Equal(recipient.Material, Assert.Single(replayItem.SubscriptionDelivery!.Recipients).Material);
        Assert.Equal(seal.Fingerprint, Assert.Single(replayItem.SubscriptionDelivery.Progress).Seal!.Fingerprint);
        Assert.Equal(1, sp.GetRequiredService<ConsumerProbe>().CapabilityChecks);
        await Assert.ThrowsAsync<TriggerOperationException>(() => ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first",
            JsonSerializer.SerializeToElement(new { message = "changed" })).AsTask());
    }

    [Theory]
    [InlineData("denied")]
    [InlineData("missing")]
    [InlineData("expired")]
    [InlineData("wrong-resource")]
    [InlineData("schema")]
    public async Task Current_source_policy_and_registered_schema_are_checked_before_an_existing_receipt_can_be_exposed(string defect)
    {
        var services = RuntimeServices();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); var sp = scope.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        var payload = JsonSerializer.SerializeToElement(new { message = "hello" });
        var first = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        var policy = sp.GetRequiredService<ConsumerAuthorization>();
        if (defect == "denied") policy.Allowed = false;
        if (defect == "missing") policy.HasCurrentContext = false;
        if (defect == "expired") policy.ExpiresAt = Now;
        if (defect == "wrong-resource") policy.TriggerId = "another-owned-resource";
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => sp.GetRequiredService<IStandaloneTriggerIngress<ConsumerSource>>().AdmitAsync(
            new("consumer-trigger", "first", defect == "schema" ? new("Different.Payload", schemaVersion: 1) : ConsumerModule.Schema, payload)).AsTask());
        Assert.Equal(defect is "missing" or "expired" ? "unauthorized" : defect == "schema" ? "source-type-mismatch" : "forbidden", error.Code);
        var actual = await ReadAcceptedAsync(sp, first, "first");
        Assert.Single(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
        Assert.Equal(RuntimeRecipientPhase.Pending, Assert.Single(actual.SubscriptionDelivery!.Progress).Phase);
    }

    [Fact]
    public void Factory_rejects_unregistered_producers_and_missing_or_default_resource_policy_without_activating_the_type()
    {
        var services = RuntimeServices();
        services.RemoveAll<IStandaloneTriggerSourceContext<ConsumerSource>>();
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            Bind(scope.ServiceProvider);
            Assert.False(Assert.Single(scope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>()).SupportsResourceAuthorization);
            Assert.Throws<NotSupportedException>(() => scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngressFactory>().Create<ConsumerSource>());
            Assert.Throws<NotSupportedException>(() => scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngressFactory>().Create<UnregisteredProducer>());
        }
        services.AddScoped<IStandaloneTriggerSourceContext<ConsumerSource>, DefaultPolicyContext>();
        using var fallback = services.BuildServiceProvider(); using var fallbackScope = fallback.CreateScope(); Bind(fallbackScope.ServiceProvider);
        Assert.False(Assert.Single(fallbackScope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>()).SupportsResourceAuthorization);
        Assert.Throws<NotSupportedException>(() => fallbackScope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngressFactory>().Create<ConsumerSource>());
        services.AddScoped<IStandaloneTriggerSourceContext<ConsumerSource>, ConsumerContext>();
        using var ambiguous = services.BuildServiceProvider(); using var ambiguousScope = ambiguous.CreateScope(); Bind(ambiguousScope.ServiceProvider);
        Assert.False(Assert.Single(ambiguousScope.ServiceProvider.GetServices<StandaloneTriggerSourceCapability>()).SupportsResourceAuthorization);
        Assert.Throws<NotSupportedException>(() => ambiguousScope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngressFactory>().Create<ConsumerSource>());
    }

    [Fact]
    public async Task A_retained_port_refuses_after_its_operation_scope_has_been_disposed()
    {
        using var provider = RuntimeServices().BuildServiceProvider();
        var scope = provider.CreateScope(); Bind(scope.ServiceProvider);
        var port = scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<ConsumerSource>>();
        scope.Dispose();
        var error = await Assert.ThrowsAsync<TriggerOperationException>(() => port.AdmitAsync(new("consumer-trigger", "key", ConsumerModule.Schema,
            JsonSerializer.SerializeToElement(new { message = "hello" }))).AsTask());
        Assert.Equal("unavailable", error.Code);
        Assert.IsType<ObjectDisposedException>(error.InnerException);
    }

    [Fact]
    public async Task Authorized_receipt_replay_survives_a_deleted_resource_tombstone()
    {
        using var provider = RuntimeServices().BuildServiceProvider(); using var scope = provider.CreateScope(); var sp = scope.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        var payload = JsonSerializer.SerializeToElement(new { message = "hello" });
        var first = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        var resource = (await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "consumer-trigger"))!;
        var old = resource.Trigger;
        var deleted = new StandaloneTrigger(old.Id, old.Name, old.TypeKey, old.SchemaVersion, old.Configuration, 2, 1, 1,
            TriggerDesiredState.Deleted, old.Origin, old.CreatedAt, Now, old.CreatedBy, tenantId: old.TenantId);
        await sp.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(Scope, new("delete", "delete", "operator", 1, deleted, resource.Subscriptions));
        var replay = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        Assert.True(replay.Replayed);
        Assert.Equal(first.AdmissionId, replay.AdmissionId);
        Assert.Equal(2, sp.GetRequiredService<ConsumerAuthorization>().AuthorizationChecks);
    }

    [Fact]
    public async Task Independent_consumer_producer_IDs_are_unique_and_a_different_family_cannot_submit_to_another_producers_resource()
    {
        var services = RuntimeServices();
        Assert.Throws<InvalidOperationException>(() => ConsumerModule.AddOtherSource(services, conflict: true));
        // The failed startup registration adds no producer port; remove its context before trying the valid independent family.
        services.RemoveAll<IStandaloneTriggerSourceContext<OtherConsumerSource>>();
        ConsumerModule.AddOtherSource(services);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); var sp = scope.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        var payload = JsonSerializer.SerializeToElement(new { message = "hello" });
        var first = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "first", payload);
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => sp.GetRequiredService<IStandaloneTriggerIngress<OtherConsumerSource>>().AdmitAsync(
            new("consumer-trigger", "first", ConsumerModule.Schema, payload)).AsTask());
        Assert.Equal("forbidden", refusal.Code);
        Assert.Equal(2, sp.GetServices<StandaloneTriggerSourceCapability>().Count());
        Assert.Single(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
        Assert.Equal(first.AdmissionId, (await ReadAcceptedAsync(sp, first, "first")).StandaloneIngress!.AdmissionId);
    }

    private sealed class UnregisteredProducer;
    private sealed class DefaultPolicyContext : IStandaloneTriggerSourceContext<ConsumerSource>
    {
        public ValueTask<StandaloneTriggerInvocationContext> GetAsync(CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("An unsupported context must not be invoked.");
    }

    [Fact]
    public async Task Older_Core_only_provider_defaults_to_unsupported_before_source_or_target_effects()
    {
        var services = RuntimeServices();
        services.RemoveAll<IRuntimeIngressDeliveryStore>(); services.AddScoped<IRuntimeIngressDeliveryStore, UnsupportedDeliveryStore>();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); var sp = scope.ServiceProvider; Bind(sp);
        Assert.False(sp.GetRequiredService<IRuntimeIngressDeliveryStore>().SupportsAdmission);
        await Assert.ThrowsAsync<NotSupportedException>(() => ConsumerModule.SubmitAsync(sp, "not-even-configured", "key",
            JsonSerializer.SerializeToElement(new { message = "hello" })).AsTask());
        Assert.Empty(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
    }

    [Theory]
    [InlineData("processor")]
    [InlineData("dispatcher")]
    public async Task A_modern_physical_provider_with_a_selected_legacy_pipeline_refuses_activation_and_fresh_admission(string legacyService)
    {
        var services = RuntimeServices();
        var selectedTarget = Assert.Single(services, item => item.ServiceType == typeof(IStandaloneTriggerRuntimeTarget));
        services.Remove(selectedTarget);
        ConsumerModule.AddTargetDecorator(services, provider => (IStandaloneTriggerRuntimeTarget)(selectedTarget.ImplementationFactory?.Invoke(provider) ??
            ActivatorUtilities.CreateInstance(provider, selectedTarget.ImplementationType!)));
        if (legacyService == "processor")
        { services.RemoveAll<IRuntimePostCommitOutboxProcessor>(); services.AddScoped<IRuntimePostCommitOutboxProcessor, LegacyProcessor>(); }
        else
        { services.RemoveAll<IRuntimePostCommitIntentDispatcher>(); services.AddScoped<IRuntimePostCommitIntentDispatcher, LegacyDispatcher>(); }
        using var provider = services.BuildServiceProvider(); using var operation = provider.CreateScope(); var sp = operation.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        Assert.True(sp.GetRequiredService<IRuntimeIngressDeliveryStore>().SupportsAdmission);
        Assert.True(sp.GetRequiredService<ITriggerSourceResourceCommitStore>().SupportsActivatedResources);
        var management = sp.GetRequiredService<ITriggerManagementCommitter>();
        Assert.False(management.CanActivateType(ConsumerModule.TypeKey, 1));
        var existing = (await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "consumer-trigger"))!;
        var unavailable = await Assert.ThrowsAsync<TriggerOperationException>(() => management.CommitAsync(Scope,
            new("enable-with-legacy-pipeline", "enable-with-legacy-pipeline", "operator", 1, Resource(2), existing.Subscriptions)).AsTask());
        Assert.Equal("unavailable", unavailable.Code);

        await Assert.ThrowsAsync<NotSupportedException>(() => ConsumerModule.SubmitAsync(sp, "consumer-trigger", "fresh",
            JsonSerializer.SerializeToElement(new { message = "hello" })).AsTask());

        Assert.Equal(1, (await sp.GetRequiredService<IStandaloneTriggerStore>().FindAsync(Scope, "consumer-trigger"))!.Trigger.Revision);
        Assert.Empty(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
        Assert.Empty(await sp.GetRequiredService<IWorkflowSchedulerWorkQueue>().ListPendingWorkflowExecutionIdsAsync(10));
        Assert.Equal(0, sp.GetRequiredService<ConsumerProbe>().CapabilityChecks);
        Assert.Equal(0, sp.GetRequiredService<ConsumerProbe>().PreparedCommands);
    }

    [Fact]
    public async Task Accepted_receipt_replay_survives_loss_of_current_processor_capability_while_fresh_signals_refuse()
    {
        var services = RuntimeServices();
        var selected = Assert.Single(services, item => item.ServiceType == typeof(IRuntimePostCommitOutboxProcessor));
        services.Remove(selected);
        services.AddScoped(provider => new SwitchableProcessor((IRuntimePostCommitOutboxProcessor)(selected.ImplementationFactory?.Invoke(provider) ??
            ActivatorUtilities.CreateInstance(provider, selected.ImplementationType!))));
        services.AddScoped<IRuntimePostCommitOutboxProcessor>(provider => provider.GetRequiredService<SwitchableProcessor>());
        using var provider = services.BuildServiceProvider(); using var operation = provider.CreateScope(); var sp = operation.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        var payload = JsonSerializer.SerializeToElement(new { message = "hello" });
        var first = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "accepted", payload);
        var original = await ReadAcceptedAsync(sp, first, "accepted");
        sp.GetRequiredService<SwitchableProcessor>().Available = false;

        var replay = await ConsumerModule.SubmitAsync(sp, "consumer-trigger", "accepted", payload);

        Assert.True(replay.Replayed); Assert.Equal(first.AdmissionId, replay.AdmissionId);
        Assert.False(sp.GetRequiredService<ITriggerManagementCommitter>().CanActivateType(ConsumerModule.TypeKey, 1));
        await Assert.ThrowsAsync<NotSupportedException>(() => ConsumerModule.SubmitAsync(sp, "consumer-trigger", "fresh", payload).AsTask());
        var retained = await ReadAcceptedAsync(sp, replay, "accepted");
        Assert.True(original.SubscriptionDelivery!.IsEquivalentTo(retained.SubscriptionDelivery));
        Assert.Single(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
    }

    private sealed class LegacyProcessor : IRuntimePostCommitOutboxProcessor
    {
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) => throw new InvalidOperationException("Fresh intake must refuse before processing effects.");
    }
    private sealed class LegacyDispatcher : IRuntimePostCommitIntentDispatcher
    {
        public ValueTask DispatchAsync(RuntimePostCommitIntent intent, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Fresh intake must refuse before dispatch effects.");
    }
    private sealed class SwitchableProcessor(IRuntimePostCommitOutboxProcessor actual) : IRuntimePostCommitOutboxProcessor
    {
        internal bool Available { get; set; } = true;
        public bool SupportsSourceKind(RuntimeDeliverySourceKind sourceKind) => Available && actual.SupportsSourceKind(sourceKind);
        public bool SupportsSubscriptionDelivery(RuntimeDeliverySourceKind sourceKind, string deliveryKind, int protocolVersion) =>
            Available && actual.SupportsSubscriptionDelivery(sourceKind, deliveryKind, protocolVersion);
        public ValueTask<RuntimePostCommitOutboxProcessResult> ProcessAsync(RuntimePostCommitOutboxProcessRequest request,
            CancellationToken cancellationToken = default) => actual.ProcessAsync(request, cancellationToken);
    }

    [Fact]
    public async Task Missing_runtime_adapter_is_a_known_refusal_before_physical_admission()
    {
        var services = RuntimeServices(); services.RemoveAll<IStandaloneTriggerRuntimeTarget>();
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); var sp = scope.ServiceProvider; Bind(sp);
        await SeedActualPublicationAndResourceAsync(sp);
        var refused = await Assert.ThrowsAsync<StandaloneTriggerAdmissionRefusedException>(() => ConsumerModule.SubmitAsync(sp,
            "consumer-trigger", "key", JsonSerializer.SerializeToElement(new { message = "hello" })).AsTask());
        Assert.Equal("target-runtime-unavailable", Assert.Single(refused.Refusals).ReasonCode);
        Assert.Empty(await sp.GetRequiredService<IRuntimePostCommitOutboxStore>().GetDeliverableAsync(new(Now, 10)));
    }

    [Fact]
    public void Conflicting_source_registration_and_duplicate_target_ownership_are_diagnosed_before_intake()
    {
        var services = RuntimeServices();
        Assert.Throws<InvalidOperationException>(() => Elsa.Workflows.Runtime.Core.Extensions.StandaloneTriggerServiceCollectionExtensions
            .AddStandaloneTriggerIngress<ConsumerSource>(services, ConsumerModule.Descriptor with { SourceFamily = "other" }));
        var selected = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IStandaloneTriggerRuntimeTarget));
        ConsumerModule.AddTargetDecorator(services, provider => (IStandaloneTriggerRuntimeTarget)ActivatorUtilities.CreateInstance(provider, selected.ImplementationType!));
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope(); Bind(scope.ServiceProvider);
        Assert.Throws<ArgumentException>(() => scope.ServiceProvider.GetRequiredService<IStandaloneTriggerIngress<ConsumerSource>>());
    }

    [Fact]
    public async Task Independently_compiled_authoring_type_uses_normal_aggregation_and_duplicate_owners_fail_initialization()
    {
        var services = new ServiceCollection(); new EventsFeature().ConfigureServices(services);
        ConsumerModule.AddAuthoring(services); TriggerAuthoringRegistration.AddServices(services);
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
        {
            await InitializeTypesAsync(scope.ServiceProvider);
            Assert.Equal(ConsumerModule.TypeKey, Assert.Single(scope.ServiceProvider.GetRequiredService<IStandaloneTriggerTypeCatalog>().List()).TypeKey);
            Assert.Null(scope.ServiceProvider.GetService<IStandaloneTriggerIngressFactory>());
            Assert.Null(scope.ServiceProvider.GetService<IRuntimeIngressDeliveryStore>());
        }
        ConsumerModule.AddAuthoring(services);
        using var duplicateProvider = services.BuildServiceProvider(); using var duplicateScope = duplicateProvider.CreateScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() => InitializeTypesAsync(duplicateScope.ServiceProvider));
    }

    private static async Task<RuntimePostCommitOutboxItem> ReadAcceptedAsync(IServiceProvider services, StandaloneTriggerIngressResult result, string key)
    {
        var id = RuntimeStandaloneIngressSource.BuildOutboxItemId(Scope.Persistence, Scope.TenantId, "consumer-trigger", ConsumerModule.Descriptor.SourceFamily, key);
        var item = await services.GetRequiredService<IRuntimeIngressDeliveryStore>().FindDeliveryAsync(Scope, id);
        Assert.NotNull(item); Assert.Equal(result.AdmissionId, item.StandaloneIngress!.AdmissionId);
        return item;
    }

    private static ServiceCollection RuntimeServices()
    {
        var services = new ServiceCollection(); services.AddSingleton<TimeProvider>(new FakeTimeProvider(Now));
        new SerializationFeature().ConfigureServices(services);
        services.AddWorkflowRuntime();
        services.AddWorkflowStimulusRouter();
        ConsumerModule.AddSource(services, Trusted);
        services.AddPersistenceModes();
        return services;
    }
    private static void Bind(IServiceProvider services)
    {
        services.GetRequiredService<IPersistenceAccessContextBinder>().Bind(PersistenceAccessContext.Scoped(Scope.Persistence));
        services.GetRequiredService<IWellKnownTypeRegistry>().RegisterType(typeof(string), "String");
    }
    private static Task InitializeTypesAsync(IServiceProvider services) => services.GetServices<IStartupTask>()
        .Single(task => task.GetType().Name == "InitializeTriggerTypes").ExecuteAsync(CancellationToken.None);
    private static StandaloneTrigger Resource(long revision = 1, bool enabled = true) => new("consumer-trigger", "Independent consumer",
        ConsumerModule.TypeKey, 1, JsonSerializer.SerializeToElement(new { }), revision, revision, revision,
        enabled ? TriggerDesiredState.Enabled : TriggerDesiredState.Paused, new(TriggerManagementKind.Provider, "consumer-owner"), Now, Now,
        "creator", tenantId: Scope.TenantId);
    private static async Task SeedActualPublicationAndResourceAsync(IServiceProvider services)
    {
        // Publication/resource setup is explicit fixture data; signal acceptance uses the real registered checked writer.
        var material = new WorkflowExecutable(new("consumer-material", "consumer-producer", "consumer-version", "1", "hash"),
            new("root", "authored", "consumer/activity", "1", new("consumer", "1", JsonSerializer.SerializeToElement(new { })),
                new Dictionary<string, RuntimeInputBinding>(), new Dictionary<string, string>()),
            new Dictionary<string, WorkflowExecutableResumeTarget>(), Now, new Dictionary<string, string>(),
            new(1, [new("message", new("String"), true)]), [], IncidentStrategyBuiltIns.FaultReference);
        await services.GetRequiredService<IWorkflowExecutableStore>().SaveAsync(material);
        var slot = (await services.GetRequiredService<IWorkflowActivationAuthority>().TryActivateAsync(new("consumer-definition", "Published",
            "consumer-activation", WorkflowActivationSource.Publishing, 0, Now))).Slot;
        await services.GetRequiredService<IWorkflowExecutableSourceReferenceStore>().SaveAsync(new(
            WorkflowActivationReferenceIdentity.Create("consumer-activation"), material.Identity.ArtifactId, "definition", "consumer-definition", "1",
            "consumer-definition", "consumer-publication", "1", Now, Now, WorkflowExecutableReferenceScope.Published,
            ActivationId: slot.ActiveActivationId, SlotId: slot.SlotId, TenantId: Scope.TenantId));
        var subscription = new TriggerSubscription("consumer-subscription", "consumer-trigger", "consumer-definition", "Published", "display-hint", 1, 1, true,
            [new("message", new TriggerSourceBinding(["payload", "message"]))], new(TriggerManagementKind.Provider, "consumer-owner"),
            new("consumer-grant", "consumer-trigger", "consumer-subscription", "consumer-definition", "Published", "display-hint", ConsumerModule.TypeKey, 1,
                TriggerGrantStatus.Granted, "grant-author", Now));
        await services.GetRequiredService<IStandaloneTriggerStore>().WriteAsync(Scope, new("create", "create", "creator", null, Resource(), [subscription]));
    }
}
