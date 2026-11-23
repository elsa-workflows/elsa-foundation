using System.Text.Json;
using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Exceptions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

// Prepared source only. No execution/qualification is claimed by this extraction.
public sealed class RuntimeArtifactSchemaExtractionTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 8, 0, 0, 0, TimeSpan.Zero);
    private static readonly PersistenceScope Scope = new("physical-shell");
    [Fact]
    public async Task Finalized_artifact_family_accepts_legacy_sources_new_material_and_root_lease_writes()
    {
        await using var fixture = await Fixture.Create(RuntimeArtifactEfModule.SchemaFamily);
        var original = RuntimeEventExecutableTestFixture.Create("family-original");
        var reference = new WorkflowExecutableSourceReference("family-source", original.Identity.ArtifactId, "definition",
            "source", "1", original.Identity.DefinitionId, original.Identity.DefinitionVersionId, "1", Now, Now,
            WorkflowExecutableReferenceScope.Published, TenantId: Scope.Value);
        string sourceContent;
        WorkflowExecutableRootWriteLease existingLease;
        await using (var seed = fixture.Open())
        {
            await new EfWorkflowExecutableStore(seed, new Accessor()).SaveAsync(original);
            await new EfWorkflowExecutableSourceReferenceStore(seed, new Accessor(), new NoTokens()).SaveAsync(reference);
            sourceContent = (await seed.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).ContentJson;
            existingLease = Assert.IsType<WorkflowExecutableRootWriteLease>(await new EfWorkflowExecutableStore(seed, new Accessor())
                .TryAcquireRootWriteLeaseAsync(original.Identity.ArtifactId, "family-old-lease", Now.AddMinutes(2), Now));
        }
        await fixture.Finalize();
        await using var context = fixture.Open();
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens());
        var retired = reference with { DeletedAt = Now.AddSeconds(1) };
        Assert.True(await sources.TryRetireAsync(reference, retired));
        Assert.True(await sources.TryRestoreAsync(retired, reference));
        var savedSource = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, savedSource.SchemaVersion);
        Assert.Equal(sourceContent, savedSource.ContentJson);
        Assert.Null((await sources.FindAsync(reference.SourceReferenceId))!.PublicationScope);

        var materials = new EfWorkflowExecutableStore(context, new Accessor());
        Assert.True(await materials.RenewRootWriteLeaseAsync(existingLease, Now.AddMinutes(3), Now.AddSeconds(1)));
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, (await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync()).SchemaVersion);
        await materials.ReleaseRootWriteLeaseAsync(existingLease);
        await materials.SaveAsync(RuntimeEventExecutableTestFixture.Create("family-new"));
        Assert.Contains(await context.WorkflowExecutables.AsNoTracking().ToArrayAsync(),
            row => row.SchemaVersion == RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        Assert.Contains(await context.WorkflowExecutableCoordinations.AsNoTracking().ToArrayAsync(),
            row => row.SchemaVersion == RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        var template = new ExecutableActivityTemplate("family-template", "family-template-hash", original.RootActivity,
            original.ResumeTargets, [], [], [], "test/1", new Dictionary<string, string>(), Now);
        await new EfExecutableActivityTemplateStore(context, new Accessor(), new NoTokens()).SaveAsync(template);
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync()).SchemaVersion);
        var modelPin = new WorkflowExecutableIdentity("family-new-model", "model-definition", "model-version", "1.0.0", "pending");
        RuntimeModelMaterial Model(WorkflowExecutableIdentity pin) => new(1, pin, new("test.family", 1), "test/1",
            JsonSerializer.SerializeToElement(new { start = "wait" }), new(1, []), [], [], []);
        var modelSeed = Model(modelPin);
        await ((IWorkflowExecutableStore)materials).SaveModelAsync(Model(modelPin with { ArtifactHash = modelSeed.ComputeContentHash() }));
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, (await context.WorkflowExecutables.AsNoTracking().SingleAsync(row => row.IsModelMaterial)).SchemaVersion);
        var lease = await materials.TryAcquireRootWriteLeaseAsync(original.Identity.ArtifactId, "family-lease", Now.AddMinutes(2), Now);
        Assert.NotNull(lease);
        Assert.True(await materials.RenewRootWriteLeaseAsync(lease, Now.AddMinutes(3), Now.AddSeconds(1)));
        var savedLease = await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, savedLease.SchemaVersion);
        await materials.ReleaseRootWriteLeaseAsync(lease);
        Assert.Empty(await context.WorkflowExecutableRootWriteLeases.AsNoTracking().ToArrayAsync());
    }


    [Fact]
    public void Scope_upcaster_preserves_predecessor_bytes_and_does_not_mint_authority()
    {
        const string legacy = """{ "reference": { "sourceReferenceId": "legacy" }, "opaque": 7 }""";
        var row = new EfSchemaRowContent(typeof(WorkflowExecutableSourceReferenceEntity),
            (nameof(WorkflowExecutableSourceReferenceEntity.ContentJson), legacy));
        Assert.Equal(legacy, new RuntimeScopedPublicationV1Upcaster().Upcast(row)
            [nameof(WorkflowExecutableSourceReferenceEntity.ContentJson)]);
        Assert.DoesNotContain("publicationScope", JsonSerializer.Serialize(Reference("legacy")), StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("{\"publicationScope\":null}")]
    [InlineData("{\"PublicationScope\":{\"value\":\"forged\"}}")]
    [InlineData("{\"reference\":{\"publicationScope\":null}}")]
    [InlineData("{\"REFERENCE\":{\"PUBLICATIONSCOPE\":{\"value\":\"forged\"}}}")]
    public void Scope_upcaster_refuses_scope_authority_under_a_predecessor_stamp(string json)
    {
        var row = new EfSchemaRowContent(typeof(WorkflowExecutableSourceReferenceEntity),
            (nameof(WorkflowExecutableSourceReferenceEntity.ContentJson), json));
        Assert.Throws<InvalidDataException>(() => new RuntimeScopedPublicationV1Upcaster().Upcast(row));
    }

    [Fact]
    public async Task Real_gate_refuses_scope_before_finalization_and_same_host_accepts_it_afterward()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var access = new Accessor();
        var materials = new EfWorkflowExecutableStore(context, access);
        var executable = RuntimeEventExecutableTestFixture.Create("scope-source");
        await materials.SaveAsync(executable);
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await materials.TryAcquireRootWriteLeaseAsync(
            executable.Identity.ArtifactId, "scope-source-lease", Now.AddMinutes(3), Now));
        var references = new EfWorkflowExecutableSourceReferenceStore(context, access, new NoTokens(), new FakeTimeProvider(Now));
        var reference = Reference("protected") with
        {
            ArtifactId = executable.Identity.ArtifactId,
            DefinitionId = executable.Identity.DefinitionId,
            DefinitionVersionId = executable.Identity.DefinitionVersionId,
            SlotId = $"activation-slot:{executable.Identity.DefinitionId.Length}:{executable.Identity.DefinitionId}:default",
            TenantId = "logical-tenant",
            Layout = [new("layout-node", 1, 2)]
        };
        var before = await context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => references.SaveScopedPublicationAsync(
            reference with { PublicationScope = new PersistenceScope("unrelated-scope") }, executable.Identity, [lease]).AsTask());
        await Assert.ThrowsAsync<ArgumentException>(() => references.SaveScopedPublicationAsync(
            reference with { SlotId = null }, executable.Identity, [lease]).AsTask());
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => references.SaveScopedPublicationAsync(
            reference, executable.Identity, [lease]).AsTask());
        Assert.Empty(await context.WorkflowExecutableSourceReferences.AsNoTracking().ToArrayAsync());
        Assert.Equal(before.Revision, (await context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync()).Revision);

        await fixture.Finalize();
        var saved = await references.SaveScopedPublicationAsync(reference, executable.Identity, [lease]);
        Assert.Equal(Scope, saved.PublicationScope);
        Assert.Equal("logical-tenant", saved.TenantId);
        Assert.Equal("1.1.0", (await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync()).SchemaVersion);
        var found = Assert.IsType<WorkflowExecutableSourceReference>(await references.FindAsync(saved.SourceReferenceId));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(saved, found));
        await Assert.ThrowsAsync<NotSupportedException>(() => references.SaveAsync(saved).AsTask());
        await Assert.ThrowsAsync<NotSupportedException>(() => references.SaveWithRootWriteLeasesAsync(saved, executable.Identity, [lease]).AsTask());
        var retired = saved.Retire(Now.AddSeconds(1), "retention predecessor witness");
        Assert.True(await references.TryRetireAsync(saved, retired));
        Assert.True(await references.TryRestoreAsync(retired, saved));
        Assert.Equal(Scope, (await references.FindAsync(saved.SourceReferenceId))!.PublicationScope);
        Assert.Equal("logical-tenant", (await references.FindAsync(saved.SourceReferenceId))!.TenantId);
        await materials.ReleaseRootWriteLeaseAsync(lease);
    }

    [Fact]
    public async Task A_stale_adopted_gate_cannot_downgrade_a_successor_row_stamp()
    {
        await using var fixture = await Fixture.Create();
        await fixture.Finalize();
        await using var context = fixture.Open();
        Assert.Throws<EfSchemaWriteRefusedException>(() => RuntimeSchemaWriteVersion.Artifact(context, "1.2.0"));
        Assert.Throws<InvalidOperationException>(() => RuntimeSchemaWriteVersion.For(context, "UnknownArtifactFamily", "1.0.0"));
    }

    [Fact]
    public async Task Legacy_source_adoption_after_finalization_preserves_payload_and_root_fences()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var materials = new EfWorkflowExecutableStore(context, new Accessor());
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens(), new FakeTimeProvider(Now));
        var executable = RuntimeEventExecutableTestFixture.Create("legacy-adoption");
        await materials.SaveAsync(executable);
        var lease = Assert.IsType<WorkflowExecutableRootWriteLease>(await materials.TryAcquireRootWriteLeaseAsync(
            executable.Identity.ArtifactId, "legacy-adoption-lease", Now.AddMinutes(3), Now));
        var reference = Reference("legacy-adoption") with { ArtifactId = executable.Identity.ArtifactId, TenantId = Scope.Value };
        await sources.SaveWithRootWriteLeasesAsync(reference, executable.Identity, [lease]);
        var before = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        var coordination = await context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync();
        var grant = await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync();
        Assert.Equal("1.0.0", before.SchemaVersion);

        await fixture.Finalize(); // No backfill is run by this fixture.
        var adopted = await sources.SaveWithRootWriteLeasesAsync(reference with { CreatedAt = Now.AddSeconds(1) }, executable.Identity, [lease]);
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(reference, adopted));
        var after = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal("1.1.0", after.SchemaVersion);
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Null(adopted.PublicationScope);
        Assert.Equal(coordination.Revision + 1, (await context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync()).Revision);
        Assert.Equal(grant.Revision + 1, (await context.WorkflowExecutableRootWriteLeases.AsNoTracking().SingleAsync()).Revision);

        // A failed physical lease fence must roll back the source adoption and coordination together.
        await Assert.ThrowsAsync<WorkflowExecutableRootWriteLeaseLostException>(() => sources.SaveWithRootWriteLeasesAsync(
            reference, executable.Identity, [lease with { ConcurrencyToken = "lost-token" }]).AsTask());
        var unchanged = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(after.Revision, unchanged.Revision);
        Assert.Equal(after.ContentJson, unchanged.ContentJson);
        Assert.Equal(after.SchemaVersion, unchanged.SchemaVersion);
        Assert.Equal(coordination.Revision + 1, (await context.WorkflowExecutableCoordinations.AsNoTracking().SingleAsync()).Revision);
        await materials.ReleaseRootWriteLeaseAsync(lease);
    }

    [Theory]
    [InlineData("workflow")]
    [InlineData("parent")]
    [InlineData("source")]
    [InlineData("publication")]
    public async Task Legacy_template_fences_after_finalization_preserve_content_and_advance_revision(string route)
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var template = Template("legacy-fence");
        var templates = new EfExecutableActivityTemplateStore(context, new Accessor(), new NoTokens(), new FakeTimeProvider(Now));
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens());
        var reference = Reference("legacy-fence") with { ArtifactId = template.TemplateId, TenantId = Scope.Value };
        var publication = new EfActivityPublicationRuntimeCommit(templates, sources);
        if (route == "publication") Assert.True(await publication.CommitAsync(template, reference));
        else await templates.SaveAsync(template);
        var before = await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var claim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
        var source = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleOrDefaultAsync();
        Assert.Equal("1.0.0", before.SchemaVersion);
        Assert.Equal("1.0.0", claim.SchemaVersion);
        await fixture.Finalize();

        switch (route)
        {
            case "workflow":
                var basis = RuntimeEventExecutableTestFixture.Create("new-referencer");
                await new EfWorkflowExecutableStore(context, new Accessor()).SaveAsync(new WorkflowExecutable(
                    basis.Identity, basis.RootActivity, basis.ResumeTargets, basis.CreatedAt,
                    new Dictionary<string, string> { ["activity.templateHash"] = template.TemplateHash }, basis.IncidentStrategy));
                Assert.Single(await context.WorkflowExecutables.AsNoTracking().ToArrayAsync());
                break;
            case "parent":
                await templates.SaveAsync(new ExecutableActivityTemplate("new-parent", "new-parent-hash", template.Root, template.ResumeTargets,
                    [], [new(template.TemplateId, template.TemplateHash)], [], "test/1", new Dictionary<string, string>(), Now));
                Assert.NotNull(await templates.FindAsync("new-parent"));
                break;
            case "source":
                await sources.SaveAsync(reference);
                Assert.NotNull(await sources.FindAsync(reference.SourceReferenceId));
                break;
            case "publication":
                Assert.False(await publication.CommitAsync(template, reference with { CreatedAt = Now.AddSeconds(1) }));
                var adopted = await context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
                Assert.Equal("1.1.0", adopted.SchemaVersion);
                Assert.Equal(source!.ContentJson, adopted.ContentJson);
                Assert.Equal(source.IncarnationId, adopted.IncarnationId);
                Assert.Equal(source.Revision + 1, adopted.Revision);
                break;
        }

        var after = await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync(row => row.Id == before.Id);
        Assert.Equal("1.1.0", after.SchemaVersion);
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(before.Revision + 1, after.Revision);
        var untouchedClaim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync(row => row.Id == claim.Id);
        Assert.Equal(claim.SchemaVersion, untouchedClaim.SchemaVersion);
        Assert.Equal(claim.ContentJson, untouchedClaim.ContentJson);
        Assert.Equal(claim.Revision, untouchedClaim.Revision);
    }

    [Fact]
    public async Task Legacy_template_maintenance_after_finalization_preserves_material_and_removes_only_doomed_source()
    {
        await using var fixture = await Fixture.Create();
        await using var context = fixture.Open();
        var templates = new EfExecutableActivityTemplateStore(context, new Accessor(), new NoTokens());
        var sources = new EfWorkflowExecutableSourceReferenceStore(context, new Accessor(), new NoTokens());
        var template = Template("legacy-maintenance");
        await templates.SaveAsync(template);
        var retired = (Reference("legacy-maintenance") with { ArtifactId = template.TemplateId, TenantId = Scope.Value })
            .Retire(Now, "doomed");
        await sources.SaveAsync(retired);
        var live = Reference("still-live") with { ArtifactId = template.TemplateId, TenantId = Scope.Value };
        await sources.SaveAsync(live);
        var before = await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var claim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
        Assert.Equal("1.0.0", before.SchemaVersion);
        Assert.Equal("1.0.0", claim.SchemaVersion);
        await fixture.Finalize();

        Assert.True(await templates.TryDeleteDoomedReferenceAsync(retired, Now.AddSeconds(1)));
        Assert.Null(await sources.FindAsync(retired.SourceReferenceId));
        var retained = Assert.IsType<WorkflowExecutableSourceReference>(await sources.FindAsync(live.SourceReferenceId));
        Assert.True(WorkflowExecutableSourceReferenceComparer.SameSnapshot(live, retained));
        var after = await context.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var afterClaim = await context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
        Assert.Equal("1.1.0", after.SchemaVersion);
        Assert.Equal("1.1.0", afterClaim.SchemaVersion);
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(claim.ContentJson, afterClaim.ContentJson);
        Assert.Equal(before.IncarnationId, after.IncarnationId);
        Assert.Equal(claim.IncarnationId, afterClaim.IncarnationId);
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(claim.Revision + 1, afterClaim.Revision);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Failed_finalized_template_unit_rolls_back_stamps_revisions_and_reference_effects(bool maintenance)
    {
        await using var fixture = await Fixture.Create();
        var template = Template("rollback");
        var reference = Reference("rollback") with { ArtifactId = template.TemplateId, TenantId = Scope.Value };
        if (maintenance) reference = reference.Retire(Now, "doomed");
        ExecutableActivityTemplateEntity before;
        ExecutableActivityTemplateHashClaimEntity claim;
        WorkflowExecutableSourceReferenceEntity source;
        await using (var seed = fixture.Open())
        {
            await new EfExecutableActivityTemplateStore(seed, new Accessor(), new NoTokens()).SaveAsync(template);
            await new EfWorkflowExecutableSourceReferenceStore(seed, new Accessor(), new NoTokens()).SaveAsync(reference);
            before = await seed.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
            claim = await seed.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
            source = await seed.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        }
        await fixture.Finalize();
        var failure = new FailAfterArtifactSave();
        await using (var failing = fixture.Open(failure: failure))
        {
            var templates = new EfExecutableActivityTemplateStore(failing, new Accessor(), new NoTokens(), new FakeTimeProvider(Now));
            if (maintenance)
                await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
                    templates.TryDeleteDoomedReferenceAsync(reference, Now.AddSeconds(1)).AsTask());
            else
                await Assert.ThrowsAsync<RuntimeArtifactEntityFrameworkPersistenceException>(() =>
                    templates.SaveWithReferenceAsync(template, reference).AsTask());
        }
        Assert.Equal(1, failure.Saves);
        await using var fresh = fixture.Open();
        var after = await fresh.ExecutableActivityTemplates.AsNoTracking().SingleAsync();
        var afterClaim = await fresh.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync();
        var afterSource = await fresh.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(before.SchemaVersion, after.SchemaVersion);
        Assert.Equal(claim.SchemaVersion, afterClaim.SchemaVersion);
        Assert.Equal(source.SchemaVersion, afterSource.SchemaVersion);
        Assert.Equal(before.Revision, after.Revision);
        Assert.Equal(claim.Revision, afterClaim.Revision);
        Assert.Equal(source.Revision, afterSource.Revision);
        Assert.Equal(before.ContentJson, after.ContentJson);
        Assert.Equal(claim.ContentJson, afterClaim.ContentJson);
        Assert.Equal(source.ContentJson, afterSource.ContentJson);
    }

    private static ExecutableActivityTemplate Template(string id)
    {
        var executable = RuntimeEventExecutableTestFixture.Create(id);
        return new(id, id + "-hash", executable.RootActivity, executable.ResumeTargets, [], [], [], "test/1", new Dictionary<string, string>(), Now);
    }

    private sealed class FailAfterArtifactSave : SaveChangesInterceptor
    {
        public int Saves { get; private set; }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
        {
            Saves++;
            throw new IOException("Controlled failure after artifact SQL, before transaction commit.");
        }
    }

    private static WorkflowExecutableSourceReference Reference(string activation) => new(
        WorkflowActivationReferenceIdentity.Create(activation), "artifact", "definition", "source", "1", "definition", "version", "1",
        Now, Now, WorkflowExecutableReferenceScope.Published, ActivationId: activation,
        SlotId: "activation-slot:10:definition:default");

    private sealed class Accessor : IPersistenceAccessContextAccessor
    { public PersistenceAccessContext Current => PersistenceAccessContext.Scoped(Scope); }
    private sealed class NoTokens : IRuntimeRecoveryContinuationCodec
    {
        public string Encode(string purpose, ReadOnlySpan<byte> payload) => throw new NotSupportedException();
        public byte[] Decode(string purpose, string token) => throw new NotSupportedException();
    }
    private sealed class SingleCurrentMember : IEfSchemaFleet
    {
        public EfSchemaFleetStanding GetLocalStanding() => new(new("host", "incarnation"), false);
        public ValueTask PublishAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;
        public ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) => ValueTask.FromResult(new EfSchemaFleetAnswer(true, []));
        public TimeSpan SettleMargin => TimeSpan.Zero;
        public IChangeToken GetChangeToken() => new CancellationChangeToken(CancellationToken.None);
    }
    private sealed partial class Fixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection = new("Data Source=:memory:");
        private readonly EfSchemaFinalizationGates gates = new();
        private readonly ServiceProvider services;
        public IEfSchemaFleet Fleet { get; } = new SingleCurrentMember();
        public EfSchemaModuleGate Gate { get; }
        private readonly string family;
        private Fixture(string family)
        {
            this.family = family;
            services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            Gate = new(EfSchemaModuleFamilies.ForContext(typeof(RuntimeDbContext))!, Fleet,
                new EfSchemaFinalizationObservations(), new());
        }
        public static async Task<Fixture> Create(string family = RuntimeArtifactEfModule.SchemaFamily, string writeVersion = "1.0.0")
        {
            var fixture = new Fixture(family); await fixture.connection.OpenAsync();
            await using var context = fixture.Open(); await context.Database.EnsureCreatedAsync();
            var store = new EfSchemaFinalizationStore(context);
            var readable = fixture.Gate.Families.Chains.Single(chain => chain.Family == family).ReadableVersions;
            var record = await store.GetOrCreateAsync(family, writeVersion, readable, SchemaFinalizationActor.OfOperator("test"));
            await store.PlaceHoldAsync(family, record.Revision, null, "controlled v1 rollout", "test", readable);
            await fixture.Gate.ActivateAsync(context); fixture.gates.Register(typeof(RuntimeDbContext), fixture.Gate);
            return fixture;
        }
        public RuntimeSqliteDbContext Open(bool ungated = false, SaveChangesInterceptor? failure = null)
        {
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection);
            if (!ungated) { builder.UseApplicationServiceProvider(services); EfSchemaWriteGateInterceptor.EnsureAdded(builder); }
            if (failure is not null) builder.AddInterceptors(failure);
            return new(builder.Options);
        }
        public async Task Finalize(string? selectedFamily = null)
        {
            var targetFamily = selectedFamily ?? family;
            await using var context = Open(); var store = new EfSchemaFinalizationStore(context);
            var record = (await store.FindAsync(targetFamily))!;
            await store.ReleaseHoldAsync(targetFamily, record.Revision, null, "test");
            await Gate.EvaluateAsync(context); await Gate.RefreshAsync(context);
            Assert.Equal(Gate.Families.Chains.Single(chain => chain.Family == targetFamily).CurrentVersion, Gate.StateOf(targetFamily)!.WriteVersion);
        }
        public async ValueTask DisposeAsync() { await services.DisposeAsync(); await connection.DisposeAsync(); }
    }
}
