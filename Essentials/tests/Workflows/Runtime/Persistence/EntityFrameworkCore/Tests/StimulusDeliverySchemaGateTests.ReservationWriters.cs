using System.Data;
using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliverySchemaGateTests
{
    [Theory]
    [InlineData(StandaloneTriggersEfModule.SchemaFamily)] [InlineData(RuntimeTriggerBindingEfModule.SchemaFamily)]
    public async Task Semantic_source_activation_waits_for_both_admitted_writer_families(string lagging)
    {
        await using var fixture = await Fixture.CreateIngressReadiness(lagging, "1.0.0");
        await using var context = fixture.Open(); using var metadata = new NativeTargetMetadataFixture();
        var prepared = await PrepareGatedIngressAsync(context, metadata.Publisher, [new ReadinessPolicy()]);
        Assert.False(prepared.Source.SupportsActivatedResources);
        await Assert.ThrowsAsync<NotSupportedException>(() => prepared.Source.CommitAsync(prepared.Scope, prepared.Activation, []).AsTask());
        Assert.Empty(await context.RuntimePostCommitOutbox.AsNoTracking().ToArrayAsync());
        Assert.Equal(TriggerDesiredState.Paused, (await prepared.Resources.FindAsync(prepared.Scope, prepared.Activation.Trigger.Id))!.Trigger.DesiredState);
        await fixture.Finalize();
        Assert.True(prepared.Source.SupportsActivatedResources);
        await prepared.Source.CommitAsync(prepared.Scope, prepared.Activation, []);
        Assert.False((await prepared.Ingress.AdmitAsync(prepared.Request)).Replayed);
        Assert.Equal("1.1.0", (await context.StandaloneTriggers.AsNoTracking().SingleAsync()).SchemaVersion);
    }

    [Fact]
    public async Task Reservation_family_adoption_preserves_opaque_projection_content_and_stamps_every_changed_row()
    {
        await using var fixture = await Fixture.CreateReservationHolds(); var access = new Accessor();
        var scope = new TriggerScope(Scope, Scope.Value); var clock = new FakeTimeProvider(Now);
        string fingerprint; string originalReceipt;
        await using (var seed = fixture.Open())
        {
            await new EfStandaloneTriggerStore(seed, access, clock).WriteAsync(scope,
                new("seed-resource", "seed-resource", "operator", null, WriterResource(1), [WriterSubscription("kept", 1), WriterSubscription("removed", 1)]));
            var bindings = new EfWorkflowTriggerBindingStore(seed, access);
            await bindings.PrepareActivationAsync("schema-activation", [WriterBinding() with { ActivationId = "schema-activation", SlotId = "schema-slot" }]);
            fingerprint = (await seed.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleAsync()).ContentJson;
            originalReceipt = (await seed.TriggerMutations.AsNoTracking().SingleAsync()).ContentJson;
            await using var transaction = await seed.Database.BeginTransactionAsync(IsolationLevel.Serializable);
            await new EfIngressReservationStore(seed, access).StageOwnerAsync(scope, RuntimeIngressReservation.PublicationSlotOwner, "schema-slot",
                [WriterReservation(scope, "original", 1)], CancellationToken.None);
            await seed.SaveChangesAsync(); await transaction.CommitAsync(); seed.ChangeTracker.Clear();
            Assert.Equal("1.0.0", (await seed.StandaloneTriggers.AsNoTracking().SingleAsync()).SchemaVersion);
            Assert.All(await seed.TriggerSubscriptions.AsNoTracking().ToArrayAsync(), row => Assert.Equal("1.0.0", row.SchemaVersion));
            Assert.Equal("1.0.0", (await seed.IngressReservations.AsNoTracking().SingleAsync()).SchemaVersion);
            Assert.Equal("1.0.0", (await seed.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleAsync()).SchemaVersion);
        }
        await fixture.Finalize(StandaloneTriggersEfModule.SchemaFamily); await fixture.Finalize(RuntimeTriggerBindingEfModule.SchemaFamily);
        await using var context = fixture.Open(); var resources = new EfStandaloneTriggerStore(context, access, clock);
        await resources.WriteAsync(scope, new("update-resource", "update-resource", "operator", 1, WriterResource(2), [WriterSubscription("kept", 2)]));
        Assert.Equal("1.1.0", (await context.StandaloneTriggers.AsNoTracking().SingleAsync()).SchemaVersion);
        var subscriptions = await context.TriggerSubscriptions.AsNoTracking().ToArrayAsync(); Assert.Equal(2, subscriptions.Length);
        Assert.All(subscriptions, row => Assert.Equal("1.1.0", row.SchemaVersion)); Assert.Single(subscriptions, row => row.IsDeleted);
        var receipts = await context.TriggerMutations.AsNoTracking().ToArrayAsync();
        Assert.Contains(receipts, row => row.SchemaVersion == "1.0.0" && row.ContentJson == originalReceipt);
        Assert.Single(receipts, row => row.SchemaVersion == "1.1.0");
        Assert.NotNull(await resources.FindMutationAsync(scope, "operator", "seed-resource"));
        Assert.Null(await resources.FindSubscriptionAsync(scope, "removed"));
        await new EfWorkflowTriggerBindingStore(context, access).ActivateAsync("schema-activation", null);
        Assert.Equal("1.1.0", (await context.WorkflowTriggerBindings.AsNoTracking().SingleAsync()).SchemaVersion);
        var state = await context.WorkflowTriggerBindingProjectionStates.AsNoTracking().SingleAsync();
        Assert.Equal("1.1.0", state.SchemaVersion); Assert.True(state.IsActive); Assert.Equal(fingerprint, state.ContentJson);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await new EfIngressReservationStore(context, access).StageOwnerAsync(scope, RuntimeIngressReservation.PublicationSlotOwner, "schema-slot",
                [WriterReservation(scope, "original", 2), WriterReservation(scope, "new", 2)], CancellationToken.None);
            await context.SaveChangesAsync(); await transaction.CommitAsync(); context.ChangeTracker.Clear();
        }
        Assert.All(await context.IngressReservations.AsNoTracking().ToArrayAsync(), row => Assert.Equal("1.1.0", row.SchemaVersion));
        var reader = new EfIngressReservationStore(context, access);
        Assert.Equal("2", (await reader.FindAsync(scope, "test/schema", "original"))!.Metadata["revision"]);
        await using (var transaction = await context.Database.BeginTransactionAsync(IsolationLevel.Serializable))
        {
            await reader.StageOwnerAsync(scope, RuntimeIngressReservation.PublicationSlotOwner, "schema-slot", [WriterReservation(scope, "new", 3)], CancellationToken.None);
            await context.SaveChangesAsync(); await transaction.CommitAsync(); context.ChangeTracker.Clear();
        }
        Assert.Single(await context.IngressReservations.AsNoTracking().ToArrayAsync());
        Assert.Null(await reader.FindAsync(scope, "test/schema", "original"));
    }

    [Theory]
    [InlineData(StandaloneTriggersEfModule.SchemaFamily)] [InlineData(RuntimeTriggerBindingEfModule.SchemaFamily)]
    public async Task Actually_admitted_v1_writer_refuses_updates_and_deletes_after_quiescent_finalization_and_refresh(string family)
    {
        await using var fixture = await Fixture.CreateReservationHolds();
        var current = fixture.Gate.Families;
        var declarations = current.Chains.Select(chain => current.DeclarationOf(chain.Family)).Select(declaration =>
            declaration.Name is StandaloneTriggersEfModule.SchemaFamily or RuntimeTriggerBindingEfModule.SchemaFamily
                ? declaration with { CurrentVersion = "1.0.0", Upcasters = [] } : declaration);
        var oldGate = new EfSchemaModuleGate(EfSchemaModuleFamilies.FromDeclarations(current.Module, declarations), fixture.Fleet,
            new EfSchemaFinalizationObservations(), new());
        var oldGates = new EfSchemaFinalizationGates(); using var oldServices = new ServiceCollection().AddSingleton(oldGates).BuildServiceProvider();
        await using var old = fixture.OpenWith(oldServices);
        await oldGate.ActivateAsync(old); oldGates.Register(typeof(RuntimeDbContext), oldGate);
        Assert.NotNull(oldGate.DatabaseIdentity); Assert.Equal("1.0.0", oldGate.StateOf(family)!.WriteVersion);
        Assert.Equal(["1.0.0"], oldGate.StateOf(family)!.ReadableVersions);
        var access = new Accessor(); var scope = new TriggerScope(Scope, Scope.Value);
        await new EfStandaloneTriggerStore(old, access, new FakeTimeProvider(Now)).WriteAsync(scope,
            new("old-seed", "old-seed", "operator", null, WriterResource(1), []));
        await new EfWorkflowTriggerBindingStore(old, access).SaveAsync(WriterBinding());
        Assert.Equal("1.0.0", (await old.StandaloneTriggers.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal("1.0.0", (await old.WorkflowTriggerBindings.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.False(old.ChangeTracker.HasChanges());
        // The v1 writer is quiescent here. This uses actual admission/refresh/interception, with deterministic fleet answers;
        // it does not claim a live-fleet or old-release-process witness.
        await fixture.Finalize(StandaloneTriggersEfModule.SchemaFamily); await fixture.Finalize(RuntimeTriggerBindingEfModule.SchemaFamily);
        await oldGate.RefreshAsync(old); Assert.True(oldGate.StateOf(family)!.WritesRefused);
        if (family == StandaloneTriggersEfModule.SchemaFamily)
        {
            var row = await old.StandaloneTriggers.SingleAsync(); row.ContentJson = JsonSerializer.Serialize(WriterResource(2), new JsonSerializerOptions(JsonSerializerDefaults.Web));
            row.Revision = 2; // The name/projections remain unchanged; this is valid predecessor content.
            await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => old.SaveChangesAsync()); old.ChangeTracker.Clear();
            old.StandaloneTriggers.Remove(await old.StandaloneTriggers.SingleAsync());
        }
        else
        {
            var row = await old.WorkflowTriggerBindings.SingleAsync();
            row.ContentJson = RuntimeArtifactJson.Serialize(WriterBinding() with { Metadata = new Dictionary<string, string> { ["audit"] = "old-writer" } });
            row.Revision = 2;
            await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => old.SaveChangesAsync()); old.ChangeTracker.Clear();
            old.WorkflowTriggerBindings.Remove(await old.WorkflowTriggerBindings.SingleAsync());
        }
        await Assert.ThrowsAsync<EfSchemaFamilyWritesRefusedException>(() => old.SaveChangesAsync()); old.ChangeTracker.Clear();
        await using var verify = fixture.Open();
        Assert.Single(await verify.StandaloneTriggers.AsNoTracking().ToArrayAsync()); Assert.Single(await verify.WorkflowTriggerBindings.AsNoTracking().ToArrayAsync());
        Assert.Equal(1, (await verify.StandaloneTriggers.AsNoTracking().SingleAsync()).Revision);
        Assert.Equal(1, (await verify.WorkflowTriggerBindings.AsNoTracking().SingleAsync()).Revision);
    }

    private static StandaloneTrigger WriterResource(long revision) => new("schema-resource", "Schema resource", "event", 1,
        JsonSerializer.SerializeToElement(new { eventName = "orders" }), revision, 1, revision, TriggerDesiredState.Paused,
        new(TriggerManagementKind.Studio), Now, Now, "creator", tenantId: Scope.Value);
    private static TriggerSubscription WriterSubscription(string id, long revision) => new(id, "schema-resource", "definition-" + id,
        "Published", "native", revision, 1, false, [], new(TriggerManagementKind.Studio));
    private static WorkflowTriggerBinding WriterBinding() => new("schema-binding", "schema-artifact", "definition", "1", "hash", "root",
        "test/schema", "schema-hash", null, new Dictionary<string, string>(), Now);
    private static RuntimeIngressReservation WriterReservation(TriggerScope scope, string hash, long revision) =>
        new(RuntimeIngressReservation.CreateIdentity(scope.Persistence, "test/schema", hash), scope, "test/schema", hash,
            RuntimeIngressReservation.PublicationSlotOwner, "schema-slot", revision, revision, new Dictionary<string, string> { ["revision"] = revision.ToString() });
    private sealed class ReadinessPolicy : IIngressReservationPolicy
    {
        public string StimulusType => "test/readiness";
        public bool Conflicts(RuntimeIngressReservationDescriptor first, RuntimeIngressReservationDescriptor second) =>
            throw new InvalidOperationException("Readiness discovery must not execute a source policy.");
    }
    private sealed partial class Fixture
    {
        internal static async Task<Fixture> CreateReservationHolds()
        {
            var fixture = new Fixture(StandaloneTriggersEfModule.SchemaFamily); await fixture.connection.OpenAsync();
            await using var context = fixture.Open(); await context.Database.EnsureCreatedAsync(); var store = new EfSchemaFinalizationStore(context);
            foreach (var family in new[] { StandaloneTriggersEfModule.SchemaFamily, RuntimeTriggerBindingEfModule.SchemaFamily })
            {
                var readable = fixture.Gate.Families.Chains.Single(chain => chain.Family == family).ReadableVersions;
                var record = await store.GetOrCreateAsync(family, "1.0.0", readable, SchemaFinalizationActor.OfOperator("test"));
                await store.PlaceHoldAsync(family, record.Revision, null, "controlled semantic rollout", "test", readable);
            }
            await fixture.Gate.ActivateAsync(context); fixture.gates.Register(typeof(RuntimeDbContext), fixture.Gate); return fixture;
        }
        internal RuntimeSqliteDbContext OpenWith(IServiceProvider provider)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).UseApplicationServiceProvider(provider);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder); return new(builder.Options);
        }
    }
}
