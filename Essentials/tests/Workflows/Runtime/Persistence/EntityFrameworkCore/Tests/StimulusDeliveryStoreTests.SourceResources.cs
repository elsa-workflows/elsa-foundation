using System.Text.Json;
using Elsa.Activities.Scheduling.StandaloneTriggers;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Triggers;
using Elsa.Workflows.Runtime.Scheduling;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Resource_target_commit_and_unpublication_have_one_causal_winner(bool ef, bool resourceFirst)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness);
        var fixture = harness.Management(ingress);
        var initial = ManagedWrite("managed", 1, TriggerDesiredState.Paused, [ManagedSubscription(false)]);
        initial = await fixture.ObserveAsync(initial);
        await fixture.Writer.CommitAsync(fixture.Scope, initial, [Route("old-route")]);
        var enabled = await fixture.ObserveAsync(ManagedWrite("managed", 2, TriggerDesiredState.Enabled, [ManagedSubscription(true)]));
        TriggerMutationReceipt? receipt = null;
        if (resourceFirst) receipt = await fixture.Writer.CommitAsync(fixture.Scope, enabled, [Route("new-route")]);
        Assert.True((await ingress.Slots.TryDeactivateAsync("ingress-definition", "Published", WorkflowActivationSource.Publishing, 1, Now)).Succeeded);
        if (resourceFirst)
        {
            // The actual mutation receipt survives a later target change and reconciles before fresh eligibility.
            Assert.Equal(receipt, await fixture.Writer.CommitAsync(fixture.Scope, enabled, [Route("new-route")]));
            Assert.Equal(TriggerDesiredState.Enabled, (await fixture.Resources.FindAsync(fixture.Scope, "managed"))!.Trigger.DesiredState);
            Assert.Null(await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "old-route"));
            Assert.Equal("managed", (await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "new-route"))!.OwnerId);
        }
        else
        {
            var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Writer.CommitAsync(fixture.Scope, enabled, [Route("new-route")]).AsTask());
            Assert.Equal("conflict", conflict.Code);
            Assert.Equal(1, (await fixture.Resources.FindAsync(fixture.Scope, "managed"))!.Trigger.Revision);
            Assert.Null(await fixture.Resources.FindMutationAsync(fixture.Scope, enabled.ActorId, enabled.RequestId));
            Assert.Equal("managed", (await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "old-route"))!.OwnerId);
            Assert.Null(await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "new-route"));
        }
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task A_reviewed_absent_target_is_fenced_even_for_a_disabled_draft_association(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        var draft = ManagedWrite("managed", 1, TriggerDesiredState.Paused,
            [new("draft", "managed", "new-definition", "Published", "native", 1, 1, false, [], new(TriggerManagementKind.Studio))]);
        draft = await fixture.ObserveAsync(draft);
        await fixture.PublishAsync("new-definition", "Published", "new-publication", null);

        var conflict = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Writer.CommitAsync(fixture.Scope, draft, []).AsTask());

        Assert.Equal("conflict", conflict.Code);
        Assert.Null(await fixture.Resources.FindAsync(fixture.Scope, "managed"));
        Assert.Null(await fixture.Resources.FindMutationAsync(fixture.Scope, draft.ActorId, draft.RequestId));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public async Task Embedded_publication_and_standalone_resource_share_the_actual_exclusive_namespace(bool ef, bool publicationFirst)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        var write = ManagedWrite("managed", 1, TriggerDesiredState.Paused, []) with { ObservedTargets = [] };
        if (publicationFirst)
        {
            await fixture.PublishAsync("route-definition", "Published", "route-publication", "shared-route");
            var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Writer.CommitAsync(fixture.Scope, write, [Route("shared-route")]).AsTask());
            Assert.Equal("route-conflict", refusal.Code);
            Assert.Null(await fixture.Resources.FindAsync(fixture.Scope, "managed"));
        }
        else
        {
            await fixture.Writer.CommitAsync(fixture.Scope, write, [Route("shared-route")]);
            var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.PublishAsync("route-definition", "Published", "route-publication", "shared-route"));
            Assert.Equal("route-conflict", refusal.Code);
            Assert.Null(await ingress.Slots.FindAsync("route-definition", "Published"));
            Assert.Empty(await fixture.Bindings.ListAllByStimulusAsync("Test.Exclusive", "shared-route"));
        }
        Assert.NotNull(await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "shared-route"));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Named_slots_sharing_one_artifact_still_compete_for_an_exclusive_route(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        await fixture.PublishAsync("same-definition", "Blue", "blue", "shared-route");
        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.PublishAsync("same-definition", "Green", "green", "shared-route"));
        Assert.Equal("route-conflict", refusal.Code);
        Assert.Null(await ingress.Slots.FindAsync("same-definition", "Green"));
        Assert.Equal("blue", (await ingress.Slots.FindAsync("same-definition", "Blue"))!.ActiveActivationId);
        Assert.Equal(ingress.Material.Identity.ArtifactId, Assert.Single(await fixture.Bindings.ListAllByStimulusAsync("Test.Exclusive", "shared-route")).ArtifactId);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Paused_claims_remain_failed_route_swap_preserves_previous_source_and_delete_releases_it(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        var write = ManagedWrite("managed", 1, TriggerDesiredState.Paused, []) with { ObservedTargets = [] };
        await fixture.Writer.CommitAsync(fixture.Scope, write, [Route("old-route")]);
        await fixture.PublishAsync("other", "Published", "other-publication", "occupied-route");
        var swap = ManagedWrite("managed", 2, TriggerDesiredState.Paused, []) with { ObservedTargets = [] };
        Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() =>
            fixture.Writer.CommitAsync(fixture.Scope, swap, [Route("occupied-route")]).AsTask())).Code);
        Assert.Equal(1, (await fixture.Resources.FindAsync(fixture.Scope, "managed"))!.Trigger.Revision);
        Assert.Equal("managed", (await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "old-route"))!.OwnerId);
        Assert.Null(await fixture.Resources.FindMutationAsync(fixture.Scope, swap.ActorId, swap.RequestId));
        var deletion = ManagedWrite("managed", 2, TriggerDesiredState.Deleted, []) with { ObservedTargets = [] };
        await fixture.Writer.CommitAsync(fixture.Scope, deletion, []);
        Assert.Null(await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "old-route"));
        await fixture.PublishAsync("replacement", "Published", "replacement-publication", "old-route");
        Assert.Equal(RuntimeIngressReservation.PublicationSlotOwner,
            (await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "old-route"))!.OwnerKind);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_active_exclusive_binding_without_a_new_claim_refuses_a_standalone_collision(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        await fixture.Bindings.SaveAsync(new("legacy-binding", ingress.Material.Identity.ArtifactId, "legacy-definition", "1", "hash", "node",
            "Test.Exclusive", "legacy-route", null, new Dictionary<string, string>(), Now, Cardinality: TriggerCardinality.Exclusive));
        var write = ManagedWrite("managed", 1, TriggerDesiredState.Paused, []) with { ObservedTargets = [] };
        Assert.Equal("route-conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() =>
            fixture.Writer.CommitAsync(fixture.Scope, write, [Route("legacy-route")]).AsTask())).Code);
        Assert.Null(await fixture.Resources.FindAsync(fixture.Scope, "managed"));
        Assert.Null(await fixture.Reservations.FindAsync(fixture.Scope, "Test.Exclusive", "legacy-route"));
    }

    [Fact]
    public async Task A_split_native_authority_or_binding_store_cannot_claim_activated_source_support()
    {
        await using var harness = await Harness.Create(false);
        var ingress = await PrepareIngressAsync(harness); var fixture = harness.Management(ingress);
        var wrong = new InMemoryTriggerSourceResourceCommitStore(fixture.Resources, ingress.Port, new InMemoryWorkflowActivationAuthority(),
            fixture.Switch, fixture.Bindings, harness.MemoryState, new Accessor(), harness.Clock, [], targets: fixture.Targets);
        Assert.False(wrong.SupportsActivatedResources);
        var write = ManagedWrite("managed", 1, TriggerDesiredState.Paused, []) with { ObservedTargets = [] };
        await Assert.ThrowsAsync<NotSupportedException>(() => wrong.CommitAsync(fixture.Scope, write, [Route("route")]).AsTask());
        Assert.Null(await fixture.Resources.FindAsync(fixture.Scope, "managed"));
        wrong = new(fixture.Resources, ingress.Port, ingress.Slots, fixture.Switch, new InMemoryWorkflowTriggerBindingStore(), harness.MemoryState,
            new Accessor(), harness.Clock, [], targets: fixture.Targets);
        Assert.False(wrong.SupportsActivatedResources);
        await Assert.ThrowsAsync<NotSupportedException>(() => wrong.CommitAsync(fixture.Scope, write, [Route("route")]).AsTask());
        var splitReferences = new InMemoryWorkflowActivationSwitch(ingress.Slots, new InMemoryWorkflowExecutableSourceReferenceStore(harness.Artifacts),
            harness.Clock, harness.MemoryState, new Accessor(), fixture.Bindings);
        wrong = new(fixture.Resources, ingress.Port, ingress.Slots, splitReferences, fixture.Bindings, harness.MemoryState,
            new Accessor(), harness.Clock, [], targets: fixture.Targets);
        Assert.False(wrong.SupportsActivatedResources);
        await Assert.ThrowsAsync<NotSupportedException>(() => wrong.CommitAsync(fixture.Scope, write, [Route("route")]).AsTask());
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Opaque_scope_and_exact_tenant_are_independent_while_route_exclusivity_is_scope_wide(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var firstScope = new TriggerScope(new("physical-shell"), "tenant-a");
        var otherTenant = new TriggerScope(firstScope.Persistence, "tenant-b");
        var otherScope = new TriggerScope(new("another-shell"), "tenant-a");
        var first = harness.ManagementScoped(firstScope);
        var second = harness.ManagementScoped(otherTenant);
        var distinct = harness.ManagementScoped(otherScope);
        TriggerStoreWrite Write(TriggerScope scope, long revision = 1, TriggerDesiredState desired = TriggerDesiredState.Paused) =>
            ManagedWrite("same-provider-resource", revision, desired, [], scope) with { ObservedTargets = [] };
        await first.Writer.CommitAsync(firstScope, Write(firstScope), [Route("same-route")]);

        var refusal = await Assert.ThrowsAsync<TriggerOperationException>(() => second.Writer.CommitAsync(otherTenant,
            Write(otherTenant), [Route("same-route")]).AsTask());

        Assert.Equal("route-conflict", refusal.Code);
        Assert.Null(await second.Resources.FindAsync(otherTenant, "same-provider-resource"));
        Assert.Null(await second.Reservations.FindAsync(otherTenant, "Test.Exclusive", "same-route"));
        Assert.Equal("tenant-a", (await first.Resources.FindAsync(firstScope, "same-provider-resource"))!.Trigger.TenantId);
        Assert.Equal(firstScope, (await first.Reservations.FindAsync(firstScope, "Test.Exclusive", "same-route"))!.Scope);
        await second.Writer.CommitAsync(otherTenant, Write(otherTenant), [Route("tenant-b-route")]);
        Assert.Equal(otherTenant, (await second.Reservations.FindAsync(otherTenant, "Test.Exclusive", "tenant-b-route"))!.Scope);
        await second.Writer.CommitAsync(otherTenant, Write(otherTenant, 2), [Route("tenant-b-replacement")]);
        Assert.Null(await second.Reservations.FindAsync(otherTenant, "Test.Exclusive", "tenant-b-route"));
        Assert.Equal(firstScope, (await first.Reservations.FindAsync(firstScope, "Test.Exclusive", "same-route"))!.Scope);
        await second.Writer.CommitAsync(otherTenant, Write(otherTenant, 3, TriggerDesiredState.Deleted), []);
        Assert.Null(await second.Reservations.FindAsync(otherTenant, "Test.Exclusive", "tenant-b-replacement"));
        Assert.Equal(firstScope, (await first.Reservations.FindAsync(firstScope, "Test.Exclusive", "same-route"))!.Scope);
        Assert.Equal(1, (await first.Resources.FindAsync(firstScope, "same-provider-resource"))!.Trigger.Revision);
        await distinct.Writer.CommitAsync(otherScope, Write(otherScope), [Route("same-route")]);
        Assert.Equal(otherScope, (await distinct.Reservations.FindAsync(otherScope, "Test.Exclusive", "same-route"))!.Scope);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Resource_schedule_uses_the_installed_planner_and_stale_targets_preserve_the_previous_generation(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var ingress = await PrepareIngressAsync(harness);
        var services = new ServiceCollection(); services.AddSingleton<IRecurringScheduleCalculator, RecurringScheduleCalculator>();
        new StandaloneScheduleTriggersFeature().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();
        var planner = Assert.Single(provider.GetServices<IStandaloneSchedulePlanner>());
        IRecurringTriggerScheduleStore schedules = harness.Context is null ? new InMemoryRecurringTriggerScheduleStore(harness.Clock) :
            new EfRecurringTriggerScheduleStore(harness.Context, new Accessor(), new Codec(), harness.Clock);
        var fixture = harness.Management(ingress, [planner], schedules);
        TriggerStoreWrite Write(long revision) => new("schedule-write-" + revision, "schedule-write-" + revision, "operator", revision == 1 ? null : revision - 1,
            new("schedule-resource", "Timer", "timer", 1, JsonSerializer.SerializeToElement(new { interval = revision == 1 ? "PT5M" : "PT10M" }),
                revision, revision, 1, TriggerDesiredState.Enabled, new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: true, tenantId: Scope.Value),
            [new("schedule-subscription", "schedule-resource", "ingress-definition", "Published", "native", 1, 1, true, [], new(TriggerManagementKind.Studio),
                new("schedule-grant", "schedule-resource", "schedule-subscription", "ingress-definition", "Published", "native", "timer", 1, TriggerGrantStatus.Granted, "creator", Now))]);
        await fixture.Writer.CommitAsync(fixture.Scope, await fixture.ObserveAsync(Write(1)), []);
        var id = RecurringTriggerSchedule.BuildResourceId(fixture.Scope.Persistence, "schedule-resource", fixture.Scope.TenantId);
        var initial = (await schedules.FindAsync(id))!;
        Assert.Equal(Now.AddMinutes(5), initial.NextOccurrence);
        var stale = await fixture.ObserveAsync(Write(2));
        await ingress.Slots.TryDeactivateAsync("ingress-definition", "Published", WorkflowActivationSource.Publishing, 1, Now);

        Assert.Equal("conflict", (await Assert.ThrowsAsync<TriggerOperationException>(() => fixture.Writer.CommitAsync(fixture.Scope, stale, []).AsTask())).Code);

        Assert.Equal(1, (await fixture.Resources.FindAsync(fixture.Scope, "schedule-resource"))!.Trigger.ConfigurationRevision);
        Assert.Equal(initial, await schedules.FindAsync(id));
        Assert.Null(await fixture.Resources.FindMutationAsync(fixture.Scope, stale.ActorId, stale.RequestId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Concurrent_publication_and_resource_commits_cannot_both_own_one_exclusive_source(bool ef)
    {
        await using var database = new TemporarySqliteDatabase("concurrent-source-reservations");
        async Task<Harness> OpenAsync()
        {
            if (!ef) return await Harness.Create(false);
            return await Harness.OpenConcurrentAsync(database.ConnectionStringWith("Pooling=False"));
        }
        await using var first = await OpenAsync();
        if (ef) await first.Context!.Database.EnsureCreatedAsync();
        await using var second = ef ? await OpenAsync() : null;
        var ingress = await PrepareIngressAsync(first); var resource = first.Management(ingress);
        var publication = resource;
        if (ef)
        {
            var selected = second!.Ingress();
            publication = second.Management(new(selected.Port, selected.Resources, selected.Slots, ingress.Request, ingress.Material, ingress.Reference, selected.Queue));
        }
        await publication.PreparePublicationAsync("concurrent-definition", "Published", "concurrent-publication", "concurrent-route");
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        async Task<Exception?> Compete(Func<Task> action)
        {
            await start.Task;
            try { await action(); return null; } catch (TriggerOperationException exception) { return exception; }
        }
        var resourceTask = Task.Run(() => Compete(async () => await resource.Writer.CommitAsync(resource.Scope,
            ManagedWrite("managed", 1, TriggerDesiredState.Paused, []) with { ObservedTargets = [] }, [Route("concurrent-route")])));
        var publicationTask = Task.Run(() => Compete(async () => Assert.True((await publication.Switch.TryActivateAsync(
            new("concurrent-definition", "Published", "concurrent-publication", WorkflowActivationSource.Publishing, 0, Now))).Succeeded)));
        start.SetResult();
        var results = await Task.WhenAll(resourceTask, publicationTask);

        Assert.Single(results.Where(item => item is null));
        Assert.Equal("route-conflict", Assert.IsType<TriggerOperationException>(Assert.Single(results.Where(item => item is not null))).Code);
        var owner = (await resource.Reservations.FindAsync(resource.Scope, "Test.Exclusive", "concurrent-route"))!;
        Assert.Equal(owner.OwnerKind == RuntimeIngressReservation.StandaloneTriggerOwner,
            (await resource.Resources.FindAsync(resource.Scope, "managed")) is not null);
        Assert.Equal(owner.OwnerKind == RuntimeIngressReservation.PublicationSlotOwner,
            (await ingress.Slots.FindAsync("concurrent-definition", "Published"))?.ActiveActivationId == "concurrent-publication");
    }

    private static RuntimeIngressReservationDescriptor Route(string route) => new("Test.Exclusive", route, new Dictionary<string, string>());
    private static TriggerStoreWrite ManagedWrite(string id, long revision, TriggerDesiredState desired, IReadOnlyList<TriggerSubscription> subscriptions, TriggerScope? scope = null) =>
        new($"{id}-{desired}-{revision}", $"{id}-{desired}-{revision}", "operator", revision == 1 ? null : revision - 1,
            new(id, "Managed source", "event", 1, JsonSerializer.SerializeToElement(new { }), revision, 1, revision, desired,
                new(TriggerManagementKind.Studio), Now, Now, "creator", hasActivated: true, tenantId: scope is null ? Scope.Value : scope.TenantId), subscriptions);
    private static TriggerSubscription ManagedSubscription(bool enabled) => new("managed-subscription", "managed", "ingress-definition", "Published", "native",
        enabled ? 2 : 1, 1, enabled, [], new(TriggerManagementKind.Studio), new("managed-grant", "managed", "managed-subscription", "ingress-definition",
            "Published", "native", "event", 1, TriggerGrantStatus.Granted, "creator", Now));

    private sealed class ManagementFixture(Harness harness, IngressFixture ingress, IWorkflowTriggerBindingStore bindings,
        IWorkflowActivationSwitch activationSwitch, TriggerTargetResolver resolver, TriggerManagementTargets targets,
        ITriggerSourceResourceCommitStore writer, IRuntimeIngressReservationReader reservations, TriggerScope? scope = null)
    {
        public TriggerScope Scope { get; } = scope ?? new(StimulusDeliveryStoreTests.Scope, StimulusDeliveryStoreTests.Scope.Value);
        public IStandaloneTriggerStore Resources => ingress.Resources;
        public IRuntimeIngressDeliveryStore Ingress => ingress.Port;
        public IWorkflowTriggerBindingStore Bindings => bindings;
        public IWorkflowActivationSwitch Switch => activationSwitch;
        public TriggerManagementTargets Targets => targets;
        public ITriggerSourceResourceCommitStore Writer => writer;
        public IRuntimeIngressReservationReader Reservations => reservations;
        public async Task<TriggerStoreWrite> ObserveAsync(TriggerStoreWrite write)
        {
            var prior = await Resources.FindAsync(Scope, write.Trigger.Id);
            var observations = new List<TriggerTargetObservation>();
            foreach (var (definition, slot) in TriggerManagementTargets.Selectors(prior, write))
                observations.Add(TriggerManagementTargets.Observation(definition, slot, await resolver.ResolveAsync(Scope, definition, slot, default)));
            return write with { ObservedTargets = observations };
        }
        public async Task PublishAsync(string definition, string slotName, string activation, string? route)
        {
            await PreparePublicationAsync(definition, slotName, activation, route);
            Assert.True((await activationSwitch.TryActivateAsync(new(definition, slotName, activation, WorkflowActivationSource.Publishing, 0, Now))).Succeeded);
        }
        public async Task PreparePublicationAsync(string definition, string slotName, string activation, string? route)
        {
            var slotId = WorkflowActivationSlotIdentity.Create(definition, slotName);
            await harness.References.SaveAsync(new(WorkflowActivationReferenceIdentity.Create(activation), ingress.Material.Identity.ArtifactId,
                "definition", definition, "1", definition, "version-" + activation, "label", Now, Now, WorkflowExecutableReferenceScope.Published,
                ActivationId: activation, SlotId: slotId, TenantId: Scope.TenantId));
            WorkflowTriggerBinding[] rows = route is null ? [] : [new(WorkflowTriggerBinding.BuildId(activation, ingress.Material.Identity.ArtifactId, "node", route),
                ingress.Material.Identity.ArtifactId, definition, "1", "hash", "node", "Test.Exclusive", route, null, new Dictionary<string, string>(), Now,
                activation, slotId, TriggerCardinality.Exclusive, false)];
            await bindings.PrepareActivationAsync(activation, rows);
        }
    }
    private sealed class ManagementMapper : ITriggerInputMapper
    {
        public WorkflowExecutableInputValidation ValidateMappings(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings) =>
            contract.Inputs.Count == 0 && mappings.Count == 0 ? WorkflowExecutableInputValidation.Success([]) : throw new InvalidOperationException("Fixture only supports its actual empty stored contract.");
        public ValueTask<WorkflowExecutableInputValidation> MapAsync(WorkflowExecutableInputContract contract, IReadOnlyList<TriggerInputMapping> mappings,
            JsonElement source, CancellationToken cancellationToken = default) => ValueTask.FromResult(ValidateMappings(contract, mappings));
    }
    private sealed partial class Harness
    {
        private readonly InMemoryWorkflowTriggerBindingStore managementBindings = new();
        public Elsa.Workflows.Runtime.Services.Checkpoints.InMemoryRuntimeCheckpointStoreState MemoryState => memory;
        public static async Task<Harness> OpenConcurrentAsync(string connectionString)
        {
            var connection = new SqliteConnection(connectionString); await connection.OpenAsync();
            return new(connection, null, false);
        }
        public ManagementFixture Management(IngressFixture ingress, IEnumerable<IStandaloneSchedulePlanner>? planners = null, IRecurringTriggerScheduleStore? schedules = null)
        {
            var access = new Accessor();
            IWorkflowTriggerBindingStore bindings = Context is null ? managementBindings : new EfWorkflowTriggerBindingStore(Context, access);
            IWorkflowActivationSwitch activationSwitch = Context is null
                ? new InMemoryWorkflowActivationSwitch(ingress.Slots, References, Clock, memory, access, bindings, schedules)
                : new EfWorkflowActivationSwitch(ingress.Slots, bindings, References, access, Clock, schedules);
            var resolver = new TriggerTargetResolver(ingress.Slots, References, Artifacts, access, [new NativeTriggerTargetModelSource()], Clock);
            var targets = new TriggerManagementTargets(resolver, new ManagementMapper());
            ITriggerSourceResourceCommitStore writer = Context is null
                ? new InMemoryTriggerSourceResourceCommitStore(ingress.Resources, ingress.Port, ingress.Slots, activationSwitch, bindings, memory, access, Clock, planners ?? [], schedules, targets)
                : new EfTriggerSourceResourceCommitStore(Context, ingress.Resources, ingress.Port, activationSwitch, bindings, access, Clock, planners ?? [], schedules, targets);
            IRuntimeIngressReservationReader reservations = Context is null ? new InMemoryIngressReservationReader(memory, access) : new EfIngressReservationStore(Context, access);
            Assert.True(writer.SupportsActivatedResources);
            return new(this, ingress, bindings, activationSwitch, resolver, targets, writer, reservations);
        }

        public ManagementFixture ManagementScoped(TriggerScope scope)
        {
            var access = new Accessor(scope.Persistence);
            IWorkflowExecutableStore artifacts = Context is null ? memoryArtifacts : new EfWorkflowExecutableStore(Context, access);
            IWorkflowExecutableSourceReferenceStore references = Context is null ? memoryReferences : new EfWorkflowExecutableSourceReferenceStore(Context, access, new Codec(), Clock);
            IWorkflowExecutionStateStore executions = Context is null ? publishers : new EfWorkflowExecutionStateStore(Context, access, new Codec());
            IWorkflowActivationAuthority authority = Context is null ? ingressAuthority : new EfWorkflowActivationAuthority(Context, access);
            IStandaloneTriggerStore resources = Context is null ? new InMemoryStandaloneTriggerStore(memory, access, Clock) : new EfStandaloneTriggerStore(Context, access, Clock);
            IWorkflowSchedulerWorkQueue queue = Context is null ? ingressQueue : new EfSchedulerWorkQueueStore(Context, access, new Codec());
            var roots = new WorkflowExecutableRootWriteLeaseManager(artifacts, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), Clock);
            IRuntimePostCommitOutboxStore outbox = Context is null ? MemoryStore(scope.Persistence) : new EfRuntimePostCommitOutboxStore(Context, access, Clock, references, roots);
            var evidence = new NativeIngressTargetEvidence(queue, executions, Clock, executables: artifacts);
            IRuntimeIngressDeliveryStore ingress = Context is null
                ? new InMemoryRuntimeIngressDeliveryStore(resources, outbox, authority, artifacts, references, executions, roots, access, Clock, evidence)
                : new EfRuntimeIngressDeliveryStore(Context, resources, outbox, authority, artifacts, references, executions, roots, access, Clock, evidence);
            IWorkflowTriggerBindingStore bindings = Context is null ? managementBindings : new EfWorkflowTriggerBindingStore(Context, access);
            IWorkflowActivationSwitch activationSwitch = Context is null ? new InMemoryWorkflowActivationSwitch(authority, references, Clock, memory, access, bindings)
                : new EfWorkflowActivationSwitch(authority, bindings, references, access, Clock);
            var resolver = new TriggerTargetResolver(authority, references, artifacts, access, [new NativeTriggerTargetModelSource()], Clock);
            var targets = new TriggerManagementTargets(resolver, new ManagementMapper());
            ITriggerSourceResourceCommitStore writer = Context is null
                ? new InMemoryTriggerSourceResourceCommitStore(resources, ingress, authority, activationSwitch, bindings, memory, access, Clock, [], targets: targets)
                : new EfTriggerSourceResourceCommitStore(Context, resources, ingress, activationSwitch, bindings, access, Clock, [], targets: targets);
            IRuntimeIngressReservationReader reservations = Context is null ? new InMemoryIngressReservationReader(memory, access) : new EfIngressReservationStore(Context, access);
            Assert.True(writer.SupportsActivatedResources);
            return new(this, new(ingress, resources, authority, null!, Material(Pin), null!, queue), bindings, activationSwitch,
                resolver, targets, writer, reservations, scope);
        }
    }
}
