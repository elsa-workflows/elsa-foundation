using Elsa.Workflows.Runtime.Core.Configuration;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Services.Checkpoints;
using Elsa.Workflows.Runtime.Services.Coalescing;
using Elsa.Workflows.Runtime.Services.Executables;
using Elsa.Workflows.Runtime.Services.Executions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class StimulusDeliveryStoreTests
{
    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Physical_cohort_retention_qualifies_actual_provider_boundaries_and_refuses_other_material(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var delivery = harness.Delivery; var artifacts = harness.Artifacts; var references = harness.References; var executions = harness.Executions;
        Assert.True(delivery.CanRetainExecutables(harness.Outbox, artifacts, references, executions));
        Assert.Empty(await delivery.ListPinnedExecutableArtifactIdsAsync(artifacts, references, executions));
        var other = new InMemoryWorkflowExecutableStore();
        Assert.False(delivery.CanRetainExecutables(harness.Outbox, other, references, executions));
        await Assert.ThrowsAsync<NotSupportedException>(() => delivery.ListPinnedExecutableArtifactIdsAsync(other, references, executions).AsTask());
        Assert.False(delivery.CanRetainExecutables(harness.Outbox, artifacts, new InMemoryWorkflowExecutableSourceReferenceStore(), executions));
        Assert.False(delivery.CanRetainExecutables(harness.Outbox, artifacts, references, new InMemoryWorkflowExecutionStateStore()));
        await using var another = await Harness.Create(ef);
        Assert.False(delivery.CanRetainExecutables(another.Outbox, artifacts, references, executions));
        if (ef)
        {
            Assert.False(delivery.CanRetainExecutables(harness.Outbox, another.Artifacts, references, executions));
            Assert.False(delivery.CanRetainExecutables(harness.Outbox, artifacts, another.References, executions));
            Assert.False(delivery.CanRetainExecutables(harness.Outbox, artifacts, references, another.Executions));
        }
    }

    [Fact]
    public async Task Physical_native_retention_keeps_every_source_scope_sharing_global_material_and_collects_unrelated_material()
    {
        await using var harness = await Harness.Create(false);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        var otherScope = new PersistenceScope("other-tenant");
        var otherPin = Pin with { ArtifactId = "other-artifact", ArtifactHash = "other-hash" };
        var otherPublisher = Publisher with { WorkflowExecutionId = "other-publisher", TenantId = otherScope.Value, PinnedExecutable = otherPin };
        await harness.Artifacts.SaveAsync(Material(otherPin)); await harness.Executions.SaveAsync(otherPublisher);
        var otherPending = new RuntimePostCommitOutboxItem("other-commit:send",
            new("send", otherPublisher.WorkflowExecutionId, "publish", Now, "activity", "other-key", System.Text.Json.JsonSerializer.SerializeToElement("payload")),
            RuntimePostCommitOutboxStatus.Pending, Now, Now, null, 0, null, null, null, null, null, 0, null, 1, otherScope, null);
        var otherDelivery = harness.Reopen(otherScope);
        await ((InMemoryRuntimeCheckpointCommitStore)otherDelivery).AddPendingForTestingAsync(otherPending);
        var otherClaim = Assert.Single(await ((IRuntimePostCommitOutboxClaimStore)otherDelivery).ClaimAsync(new("other-owner", Now, TimeSpan.FromMinutes(1), 1)));
        var otherSource = new PostCommitDeliveryContext(otherClaim, otherScope);
        var binding = new WorkflowTriggerBinding("other-binding", otherPin.ArtifactId, otherPin.DefinitionId, otherPin.ArtifactVersion,
            otherPin.ArtifactHash, "start", "event", "named", null, new Dictionary<string, string>(), Now);
        var otherSnapshot = new StimulusDeliverySnapshot(1, otherSource.SourceOutboxItemId, otherSource.IntentFingerprint,
            StimulusDeliveryScope.From(otherPublisher, otherScope), Now, "event", "named",
            [new StimulusStartRecipient(binding, otherPin, null)], [], StimulusDeliverySnapshot.CurrentRetentionAcceptanceVersion);
        await otherDelivery.InstallAsync(otherSource, otherSnapshot);
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        await ((IRuntimePostCommitOutboxClaimStore)otherDelivery).RecordDeliveryResultAsync(otherSource.Claim, new(otherSource.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher")); Assert.True(await harness.Executions.DeleteAsync(otherPublisher.WorkflowExecutionId));
        Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        Assert.Equal([Pin.ArtifactId], await harness.Delivery.ListPinnedExecutableArtifactIdsAsync()); // Delivery-scope API remains scoped.
        Assert.Equal(new[] { Pin.ArtifactId, otherPin.ArtifactId }.Order(StringComparer.Ordinal),
            await harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions));
        var orphan = Pin with { ArtifactId = "unrelated", ArtifactHash = "unrelated-hash" }; await harness.Artifacts.SaveAsync(Material(orphan));
        var collector = new WorkflowExecutableReferenceGarbageCollector(harness.Artifacts, harness.References,
            InMemoryExecutableActivityTemplateStore.CreateForMaintenance(harness.Artifacts, harness.References, harness.Clock), harness.Executions,
            null, Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }),
            harness.Clock, NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await harness.Artifacts.FindAsync(orphan.ArtifactId));
        Assert.NotNull(await harness.Artifacts.FindAsync(Pin.ArtifactId)); Assert.NotNull(await harness.Artifacts.FindAsync(otherPin.ArtifactId));
        foreach (var (key, publisher) in new[] { ("key", Publisher), ("other-key", otherPublisher) })
        {
            var target = KeyedWorkflowStartIdentity.For(key, publisher.PinnedExecutable.ArtifactId).WorkflowExecutionId;
            await harness.Executions.SaveAsync(publisher with { WorkflowExecutionId = target, Status = WorkflowExecutionStatus.Completed });
            Assert.True(await harness.Executions.DeleteAsync(target));
        }
        Assert.Empty(await harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions));
        Assert.Equal(2, (await collector.SweepAsync()).DeletedArtifactCount);
    }

    [Theory]
    [InlineData(false, false, false, true)] [InlineData(false, true, false, true)] [InlineData(true, false, false, true)] [InlineData(true, true, false, true)]
    [InlineData(false, false, true, true)] [InlineData(false, true, true, true)] [InlineData(true, false, true, true)] [InlineData(true, true, true, true)]
    [InlineData(true, false, false, false)] [InlineData(true, true, false, false)] [InlineData(true, false, true, false)] [InlineData(true, true, true, false)]
    public async Task Default_cohort_GC_uses_qualified_native_or_EF_tuple_and_keeps_template_collection_working(bool ef, bool repeat, bool coalescing, bool caching)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<TimeProvider>(new Microsoft.Extensions.Time.Testing.FakeTimeProvider(Now));
        services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor()); services.AddWorkflowRuntime();
        if (ef) services.AddRuntimeEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = "Data Source=:memory:", RecoveryContinuationSigningKey = new string('r', 64), HierarchyCursorSigningKey = new string('h', 64), CacheWorkflowExecutables = caching });
        if (repeat) services.AddWorkflowRuntime();
        if (coalescing) services.AddPersistenceModes();
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        if (ef) { var context = scope.ServiceProvider.GetRequiredService<RuntimeDbContext>(); await context.Database.OpenConnectionAsync(); await context.Database.EnsureCreatedAsync(); }
        var templates = scope.ServiceProvider.GetRequiredService<IExecutableActivityTemplateStore>();
        var material = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>();
        var references = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableSourceReferenceStore>();
        var executions = scope.ServiceProvider.GetRequiredService<IWorkflowExecutionStateStore>();
        var physical = executions.GetType() == typeof(CoalescingWorkflowExecutionStateStore)
            ? scope.ServiceProvider.GetRequiredService<CoalescingInner<IWorkflowExecutionStateStore>>().Value : executions;
        if (coalescing) Assert.IsType<CoalescingWorkflowExecutionStateStore>(executions);
        Assert.True(templates.CanMaintainReferences(material, references), $"Template boundary: {material.GetType().Name}/{references.GetType().Name}");
        Assert.True(references.CanWriteRootReferences(material), "Source boundary");
        var delivery = scope.ServiceProvider.GetRequiredService<IStimulusDeliveryStore>();
        var outbox = scope.ServiceProvider.GetRequiredService<IRuntimePostCommitOutboxStore>();
        var physicalOutbox = outbox.GetType() == typeof(CoalescingRuntimePostCommitOutboxStore)
            ? scope.ServiceProvider.GetRequiredService<CoalescingInner<IRuntimePostCommitOutboxStore>>().Value : outbox;
        Assert.True(delivery.CanRetainExecutables(physicalOutbox, material, references, physical),
            $"Outbox boundary: {delivery.GetType().Name}/{executions.GetType().Name}/{physical.GetType().Name}");
        var collector = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>(); Assert.True(collector.SupportsStimulusDeliveryRetention);
        var original = Material(Pin); var template = new ExecutableActivityTemplate("unrelated-template", "unrelated-template-hash", original.RootActivity,
            new Dictionary<string, WorkflowExecutableResumeTarget>(), [], [], [], "test/1", new Dictionary<string, string>(), Now.AddHours(-1));
        await templates.SaveAsync(template);
        Assert.Equal(1, (await collector.SweepAsync()).DeletedActivityTemplateCount); Assert.Null(await templates.FindAsync(template.TemplateId));
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Physical_retention_supports_the_real_cache_boundary_without_bypassing_fenced_writes_or_cache_invalidation(bool ef, bool caching)
    {
        await using var harness = await Harness.Create(ef);
        var material = harness.Artifacts; var references = harness.References;
        var shared = new WorkflowExecutableCache(new WorkflowExecutableCacheOptions { Enabled = true, Capacity = 8 });
        IWorkflowExecutableStore cache = caching ? new CachingWorkflowExecutableStore(material, shared, "physical", material.FindAsync)
            : new InvalidatingWorkflowExecutableStore(material, shared, "physical");
        IWorkflowExecutableStore cachedReader = new CachingWorkflowExecutableStore(material, shared, "physical", material.FindAsync);
        Assert.Same(material, cache.GetMaterialStore());
        Assert.True(references.CanWriteRootReferences(cache));
        Assert.True(harness.Delivery.CanRetainExecutables(harness.Outbox, cache, references, harness.Executions));
        var wrapper = Material(Pin); await cache.SaveAsync(wrapper); Assert.NotNull(await cachedReader.FindAsync(Pin.ArtifactId));
        var reference = new WorkflowExecutableSourceReference("cached-root", Pin.ArtifactId, "WorkflowDefinitionVersion", "source", "1",
            Pin.DefinitionId, Pin.DefinitionVersionId, Pin.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published);
        var manager = new WorkflowExecutableRootWriteLeaseManager(cache, Options.Create(new WorkflowExecutableGarbageCollectionOptions()), harness.Clock);
        await manager.ExecuteFencedAsync([Pin], "cached-invocation", async (grants, token) =>
        {
            Assert.Null(await cache.TryBeginDeletionAsync(Pin.ArtifactId, "cannot-delete-under-grants", Now.AddMinutes(1), Now, token));
            await references.SaveWithRootWriteLeasesAsync(reference, Pin, grants, token);
        });
        IExecutableActivityTemplateStore templates = ef ? new EfExecutableActivityTemplateStore(harness.Context!, new Accessor(), new Codec(), harness.Clock)
            : InMemoryExecutableActivityTemplateStore.CreateForMaintenance(cache, references, harness.Clock);
        Assert.True(templates.CanMaintainReferences(cache, references));
        var collector = new WorkflowExecutableReferenceGarbageCollector(cache, references, templates, harness.Executions, null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }), harness.Clock,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);
        Assert.False((await collector.SweepAsync()).DidWork); Assert.NotNull(await cache.FindAsync(Pin.ArtifactId));
        Assert.True(await references.RetireAsync(reference.SourceReferenceId, Now));
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount);
        Assert.Null(await material.FindAsync(Pin.ArtifactId)); Assert.Null(await cache.FindAsync(Pin.ArtifactId)); Assert.Null(await cachedReader.FindAsync(Pin.ArtifactId));
        IWorkflowExecutableStore opaque = new OpaqueMaterial(material);
        Assert.Same(opaque, opaque.GetMaterialStore()); Assert.False(references.CanWriteRootReferences(opaque));
        Assert.False(harness.Delivery.CanRetainExecutables(harness.Outbox, opaque, references, harness.Executions));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Legacy_physical_retention_defaults_refuse_without_narrow_scoped_query_fallback(bool cancel)
    {
        var legacy = new LegacyPhysicalRoots(); IStimulusDeliveryStore delivery = legacy;
        await using var harness = await Harness.Create(false);
        Assert.False(delivery.CanRetainExecutables(harness.Outbox, harness.Artifacts, harness.References, harness.Executions));
        var token = new CancellationToken(cancel);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions, token).AsTask());
        else await Assert.ThrowsAsync<NotSupportedException>(() => delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions, token).AsTask());
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions, token).AsTask());
        else await Assert.ThrowsAsync<NotSupportedException>(() => delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions, token).AsTask());
        Assert.Equal(0, legacy.ScopedReads);
    }

    [Theory]
    [InlineData("scope-hash")] [InlineData("scope-key")] [InlineData("logical-order")]
    public async Task Physical_EF_retention_refuses_contradictory_owned_source_projections_and_GC_retains_material(string defect)
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher")); Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        var row = await harness.Context!.RuntimePostCommitOutbox.SingleAsync();
        if (defect == "scope-hash") row.ScopeKeyHash = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Hash("other");
        else if (defect == "scope-key") row.ScopeKey = Elsa.Persistence.EntityFramework.EfRelationalIdentity.Encode("other");
        else row.OutboxItemIdOrderKey = "contradictory-order";
        await harness.Context.SaveChangesAsync(); harness.Context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidDataException>(() => harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions).AsTask());
        var templates = new EfExecutableActivityTemplateStore(harness.Context, new Accessor(), new Codec(), harness.Clock);
        var collector = new WorkflowExecutableReferenceGarbageCollector(harness.Artifacts, harness.References, templates, harness.Executions, null,
            Options.Create(new WorkflowExecutableGarbageCollectionOptions { ArtifactCreationGracePeriod = TimeSpan.Zero }), harness.Clock,
            NullLogger<WorkflowExecutableReferenceGarbageCollector>.Instance, harness.Delivery);
        Assert.False((await collector.SweepAsync()).DidWork); Assert.NotNull(await harness.Artifacts.FindAsync(Pin.ArtifactId));
    }

    [Fact]
    public async Task Physical_EF_retention_refuses_same_context_stores_with_another_scope()
    {
        await using var harness = await Harness.Create(true); var other = new Accessor(new("other"));
        Assert.False(harness.Delivery.CanRetainExecutables(harness.Outbox, new EfWorkflowExecutableStore(harness.Context!, other), harness.References, harness.Executions));
        Assert.False(harness.Delivery.CanRetainExecutables(harness.Outbox, harness.Artifacts, new EfWorkflowExecutableSourceReferenceStore(harness.Context!, other, new Codec()), harness.Executions));
        Assert.False(harness.Delivery.CanRetainExecutables(harness.Outbox, harness.Artifacts, harness.References, new EfWorkflowExecutionStateStore(harness.Context!, other, new Codec())));
    }

    [Theory]
    [InlineData(false, "io")] [InlineData(false, "cancel")] [InlineData(false, "requested")]
    [InlineData(true, "io")] [InlineData(true, "cancel")] [InlineData(true, "requested")]
    public async Task Physical_retention_preserves_access_failure_context_and_requested_cancellation(bool ef, string defect)
    {
        await using var harness = await Harness.Create(ef); using var cancellation = new CancellationTokenSource();
        Exception cause = defect == "io" ? new IOException("access unavailable") : new OperationCanceledException(cancellation.Token);
        var access = new RetentionAccessFailure(cause, () => { if (defect == "requested") cancellation.Cancel(); });
        var delivery = harness.WithRetentionAccess(access);
        if (defect == "requested")
            Assert.Same(cause, await Assert.ThrowsAnyAsync<OperationCanceledException>(() => delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions, cancellation.Token).AsTask()));
        else
            Assert.Same(cause, (await Assert.ThrowsAsync<StimulusDeliveryRetentionException>(() => delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions).AsTask())).InnerException);
    }

    [Fact]
    public async Task Physical_EF_retention_traverses_every_physical_page_before_releasing_an_owed_pin()
    {
        await using var harness = await Harness.Create(true);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await harness.Delivery.InstallAsync(source, Snapshot(source));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher"));
        var rootPhysicalId = (await harness.Context!.RuntimePostCommitOutbox.AsNoTracking().SingleAsync()).Id;
        var beforeRoot = 0;
        for (var candidate = 0; beforeRoot <= RuntimeStorePageRequest.MaximumLimit; candidate++)
        {
            var id = "noise-" + candidate;
            if (string.CompareOrdinal(EfRuntimePostCommitOutboxStore.RowId(Scope.Value, id), rootPhysicalId) >= 0) continue;
            var intent = new RuntimePostCommitIntent("send", "publisher", "unrelated", Now, null, null, System.Text.Json.JsonSerializer.SerializeToElement(0));
            await harness.SavePending(new(id, intent, RuntimePostCommitOutboxStatus.Pending, Now, Now)); beforeRoot++;
        }
        Assert.True(await harness.Context.RuntimePostCommitOutbox.CountAsync(row => row.Id.CompareTo(rootPhysicalId) < 0) > RuntimeStorePageRequest.MaximumLimit);
        Assert.Equal([Pin.ArtifactId], await harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Default_ordinary_collector_resolved_during_scope_failure_rechecks_data_after_scope_recovers(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        var access = new MutableRetentionAccess(); var delivery = harness.WithRetentionAccess(access);
        var material = ef ? new EfWorkflowExecutableStore(harness.Context!, access) : harness.Artifacts;
        var references = ef ? new EfWorkflowExecutableSourceReferenceStore(harness.Context!, access, new Codec(), harness.Clock) : harness.References;
        IExecutableActivityTemplateStore templates = ef ? new EfExecutableActivityTemplateStore(harness.Context!, access, new Codec(), harness.Clock)
            : InMemoryExecutableActivityTemplateStore.CreateForMaintenance(material, references, harness.Clock);
        var executions = ef ? new EfWorkflowExecutionStateStore(harness.Context!, access, new Codec(), harness.Clock) : harness.Executions;
        var services = new ServiceCollection(); services.AddLogging(); services.AddWorkflowRuntime();
        services.AddSingleton<TimeProvider>(harness.Clock); services.AddSingleton<IPersistenceAccessContextAccessor>(access);
        services.AddSingleton<IWorkflowExecutableStore>(material); services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(references);
        services.AddSingleton<IExecutableActivityTemplateStore>(templates); services.AddSingleton<IWorkflowExecutionStateStore>(executions);
        services.AddSingleton<IStimulusDeliveryStore>(delivery); services.AddSingleton<IRuntimePostCommitOutboxStore>((IRuntimePostCommitOutboxStore)delivery);
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        using var provider = services.BuildServiceProvider(); using var scope = provider.CreateScope();
        var collector = scope.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.False(collector.SupportsStimulusDeliveryRetention);
        access.Current = PersistenceAccessContext.Scoped(Scope);
        var orphan = Pin with { ArtifactId = "before-protocol", ArtifactHash = "before-protocol-hash" }; await material.SaveAsync(Material(orphan));
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount); // Ordinary cleanup still works while its source boundary proves absence.
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await delivery.InstallAsync(source, Snapshot(source));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await harness.Executions.DeleteAsync("publisher")); Assert.True(await references.RetireAsync("selected-reference", Now));
        Assert.False((await collector.SweepAsync()).DidWork); // The already-resolved ordinary instance must not forget newly accepted obligations.
        Assert.NotNull(await material.FindAsync(Pin.ArtifactId)); Assert.NotNull(await references.FindAsync("selected-reference"));
        using var recovered = provider.CreateScope();
        Assert.True(recovered.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SupportsStimulusDeliveryRetention);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Ordinary_collection_proof_refuses_marked_sources_even_before_snapshot_and_after_delivered_empty_set(bool ef)
    {
        await using var harness = await Harness.Create(ef);
        await harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions);
        var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
        await Assert.ThrowsAsync<StimulusDeliveryRetentionException>(() => harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions).AsTask());
        await harness.Delivery.InstallAsync(source, Snapshot(source, empty: true));
        await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        await Assert.ThrowsAsync<StimulusDeliveryRetentionException>(() => harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions).AsTask());
        await using var other = await Harness.Create(ef);
        await Assert.ThrowsAsync<NotSupportedException>(() => harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(other.Outbox, harness.Artifacts, harness.References, harness.Executions).AsTask());
    }

    [Theory]
    [InlineData("material")] [InlineData("references")] [InlineData("executions")] [InlineData("outbox")]
    public void Default_cohort_GC_refuses_mixed_native_selected_stores(string defect)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor()); services.AddWorkflowRuntime();
        if (defect == "material") services.AddScoped<IWorkflowExecutableStore, InMemoryWorkflowExecutableStore>();
        if (defect == "references") services.AddScoped<IWorkflowExecutableSourceReferenceStore, InMemoryWorkflowExecutableSourceReferenceStore>();
        if (defect == "executions") services.AddScoped<IWorkflowExecutionStateStore>(_ => new InMemoryWorkflowExecutionStateStore());
        if (defect == "outbox") services.AddScoped<IRuntimePostCommitOutboxStore>(_ => new InMemoryRuntimeCheckpointCommitStore());
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true }); using var scope = provider.CreateScope();
        Assert.False(scope.ServiceProvider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>().SupportsStimulusDeliveryRetention);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Default_ordinary_GC_refuses_empty_foreign_outbox_proof_for_material_with_owed_cohort(bool ef)
    {
        await using var materialBoundary = await Harness.Create(ef); await using var emptyOutboxBoundary = await Harness.Create(ef);
        var source = new PostCommitDeliveryContext(await materialBoundary.Seed(), Scope);
        await materialBoundary.Delivery.InstallAsync(source, Snapshot(source));
        await materialBoundary.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
        Assert.True(await materialBoundary.Executions.DeleteAsync("publisher"));
        Assert.True(await materialBoundary.References.RetireAsync("selected-reference", Now));
        IExecutableActivityTemplateStore templates = ef ? new EfExecutableActivityTemplateStore(materialBoundary.Context!, new Accessor(), new Codec(), materialBoundary.Clock)
            : InMemoryExecutableActivityTemplateStore.CreateForMaintenance(materialBoundary.Artifacts, materialBoundary.References, materialBoundary.Clock);
        using var provider = PhysicalCollectorProvider(materialBoundary, emptyOutboxBoundary.Delivery, emptyOutboxBoundary.Outbox, materialBoundary.Artifacts, templates);
        var collector = provider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.False(collector.SupportsStimulusDeliveryRetention); Assert.False((await collector.SweepAsync()).DidWork);
        Assert.NotNull(await materialBoundary.Artifacts.FindAsync(Pin.ArtifactId)); Assert.NotNull(await materialBoundary.References.FindAsync("selected-reference"));
        await Assert.ThrowsAsync<NotSupportedException>(() => emptyOutboxBoundary.Delivery.EnsureOrdinaryCollectionAllowedAsync(
            emptyOutboxBoundary.Outbox, materialBoundary.Artifacts, materialBoundary.References, materialBoundary.Executions).AsTask());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(false, true)] [InlineData(true, false)] [InlineData(true, true)]
    public async Task Default_ordinary_GC_final_proof_retains_cohort_installed_after_initial_discovery(bool ef, bool sourceReferenceCandidate)
    {
        await using var harness = await Harness.Create(ef); await harness.Artifacts.SaveAsync(Material(Pin));
        if (sourceReferenceCandidate)
        {
            await harness.References.SaveAsync(new("retired-before-discovery", Pin.ArtifactId, "WorkflowDefinitionVersion", "version", "1",
                Pin.DefinitionId, Pin.DefinitionVersionId, Pin.ArtifactVersion, Now, Now, WorkflowExecutableReferenceScope.Published));
            Assert.True(await harness.References.RetireAsync("retired-before-discovery", Now));
        }
        var observer = new AtReferenceGuard(harness.Artifacts, true, async () =>
        {
            var source = new PostCommitDeliveryContext(await harness.Seed(), Scope);
            await harness.Delivery.InstallAsync(source, Snapshot(source));
            await harness.Claims.RecordDeliveryResultAsync(source.Claim, new(source.SourceOutboxItemId, RuntimePostCommitOutboxStatus.Delivered, Now));
            Assert.True(await harness.Executions.DeleteAsync("publisher")); Assert.True(await harness.References.RetireAsync("selected-reference", Now));
        });
        // Opaque template maintenance forces the ordinary factory branch. The cooperating material observer
        // still exposes the actual physical boundary and forwards the real provider deletion guard.
        using var provider = PhysicalCollectorProvider(harness, harness.Delivery, harness.Outbox, observer, new InMemoryExecutableActivityTemplateStore());
        var collector = provider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.False(collector.SupportsStimulusDeliveryRetention); Assert.False((await collector.SweepAsync()).DidWork);
        Assert.Equal(1, observer.Interleavings); Assert.NotNull(await harness.Artifacts.FindAsync(Pin.ArtifactId));
        Assert.NotNull(await harness.References.FindAsync("selected-reference"));
        if (sourceReferenceCandidate) Assert.NotNull(await harness.References.FindAsync("retired-before-discovery"));
        var released = await harness.Artifacts.TryBeginDeletionAsync(Pin.ArtifactId, "assert-guard-released", Now.AddMinutes(1), Now);
        Assert.NotNull(released); await harness.Artifacts.CancelDeletionAsync(released);
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Default_EF_GC_preserves_dirty_caller_state_for_qualified_and_ordinary_composition(bool ordinary)
    {
        await using var harness = await Harness.Create(true); await harness.Artifacts.SaveAsync(Material(Pin));
        IExecutableActivityTemplateStore templates = ordinary ? new InMemoryExecutableActivityTemplateStore()
            : new EfExecutableActivityTemplateStore(harness.Context!, new Accessor(), new Codec(), harness.Clock);
        using var provider = PhysicalCollectorProvider(harness, harness.Delivery, harness.Outbox, harness.Artifacts, templates);
        var collector = provider.GetRequiredService<IWorkflowExecutableReferenceGarbageCollector>();
        Assert.Equal(!ordinary, collector.SupportsStimulusDeliveryRetention);
        var row = await harness.Context!.WorkflowExecutables.SingleAsync(); var original = row.ContentJson; row.ContentJson = "caller pending content";
        Assert.False((await collector.SweepAsync()).DidWork);
        Assert.Equal(EntityState.Modified, harness.Context.Entry(row).State); Assert.Equal("caller pending content", row.ContentJson);
        Assert.Equal(original, (await harness.Context.WorkflowExecutables.AsNoTracking().SingleAsync()).ContentJson);
        Assert.NotNull(await harness.Artifacts.FindAsync(Pin.ArtifactId));
        row.ContentJson = original; harness.Context.Entry(row).State = EntityState.Unchanged;
        Assert.Equal(1, (await collector.SweepAsync()).DeletedArtifactCount); Assert.Null(await harness.Artifacts.FindAsync(Pin.ArtifactId));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task Physical_EF_ordinary_proof_and_root_query_refuse_caller_transaction(bool ambient)
    {
        await using var harness = await Harness.Create(true);
        using var ambientTransaction = ambient ? new System.Transactions.TransactionScope(System.Transactions.TransactionScopeAsyncFlowOption.Enabled) : null;
        await using var localTransaction = ambient ? null : await harness.Context!.Database.BeginTransactionAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Delivery.EnsureOrdinaryCollectionAllowedAsync(harness.Outbox, harness.Artifacts, harness.References, harness.Executions).AsTask());
        await Assert.ThrowsAsync<InvalidOperationException>(() => harness.Delivery.ListPinnedExecutableArtifactIdsAsync(harness.Artifacts, harness.References, harness.Executions).AsTask());
        Assert.Same(localTransaction, harness.Context!.Database.CurrentTransaction); Assert.Empty(harness.Context.ChangeTracker.Entries());
    }

    private static ServiceProvider PhysicalCollectorProvider(Harness harness, IStimulusDeliveryStore delivery,
        IRuntimePostCommitOutboxStore outbox, IWorkflowExecutableStore material, IExecutableActivityTemplateStore templates)
    {
        var services = new ServiceCollection(); services.AddLogging(); services.AddWorkflowRuntime();
        services.AddSingleton<TimeProvider>(harness.Clock); services.AddSingleton<IPersistenceAccessContextAccessor>(new Accessor());
        services.AddSingleton(material); services.AddSingleton<IWorkflowExecutableSourceReferenceStore>(harness.References);
        services.AddSingleton(templates); services.AddSingleton<IWorkflowExecutionStateStore>(harness.Executions);
        services.AddSingleton(delivery); services.AddSingleton(outbox);
        services.Configure<WorkflowExecutableGarbageCollectionOptions>(options => options.ArtifactCreationGracePeriod = TimeSpan.Zero);
        return services.BuildServiceProvider();
    }

    private sealed class OpaqueMaterial(IWorkflowExecutableStore inner) : WorkflowExecutableStoreDecorator(inner);
    private sealed class RetentionAccessFailure(Exception cause, Action before) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get { before(); throw cause; } }
    }
    private sealed class MutableRetentionAccess : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; set; } = PersistenceAccessContext.Global;
    }
    private sealed class LegacyPhysicalRoots : IStimulusDeliveryStore
    {
        public int ScopedReads { get; private set; }
        public ValueTask<StimulusDeliverySnapshot> InstallAsync(PostCommitDeliveryContext source, StimulusDeliverySnapshot candidate, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<IReadOnlyCollection<string>> ListPinnedExecutableArtifactIdsAsync(CancellationToken cancellationToken = default)
        { ScopedReads++; return ValueTask.FromResult<IReadOnlyCollection<string>>([]); }
    }
}
