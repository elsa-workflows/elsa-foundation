using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.SchemaFinalization;
using Elsa.Persistence.EntityFramework.Tests;
using Elsa.Persistence.Schema;
using Elsa.Persistence.Schema.SchemaFinalization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Stores;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

public sealed partial class EfRuntimeArtifactScopeTests
{
    [Fact]
    public async Task Preparation_format_selection_is_not_admission_and_pre_finalization_metadata_writes_refuse()
    {
        await using (var database = await Database.CreateAsync())
        await using (var ungated = database.Open("tenant-a"))
        {
            Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, RuntimeSchemaWriteVersion.Artifact(ungated.Context));
            // The extracted helper deliberately returns the required format without a gate. It grants no
            // preparation authority; native participant admission is a separate, still unimplemented boundary.
            Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion, PreparationWriteVersion(ungated.Context));
        }
        await using var fixture = await PreparationGateFixture.CreateAsync();
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, RuntimeSchemaWriteVersion.Artifact(fixture.Context));
        Assert.Throws<EfSchemaWriteRefusedException>(() => PreparationWriteVersion(fixture.Context));
        fixture.Context.WorkflowPreparationRetentions.Add(PreparationRow(PreparationSnapshot()));
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Context.SaveChangesAsync());
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
        await fixture.FinalizeArtifactAsync(RuntimeArtifactEfModule.ScopedPublicationSchemaVersion);
        Assert.Throws<EfSchemaWriteRefusedException>(() => PreparationWriteVersion(fixture.Context));
        fixture.Context.WorkflowPreparationRetentions.Add(PreparationRow(PreparationSnapshot()));
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Context.SaveChangesAsync());
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
    }

    [Fact]
    public async Task Ordinary_writes_follow_actual_family_finalization_without_rewriting_existing_material_payloads()
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        var reference = Reference("legacy-reference", "legacy-artifact");
        await fixture.Executables.SaveAsync(Executable("legacy-artifact"));
        await fixture.Sources.SaveAsync(reference);
        var original = await fixture.Context.WorkflowExecutables.AsNoTracking().SingleAsync();
        var originalReference = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, original.SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, originalReference.SchemaVersion);
        await fixture.FinalizePreparationAsync();
        Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion, PreparationWriteVersion(fixture.Context));
        await fixture.Executables.SaveAsync(Executable("new-artifact"));
        await fixture.Templates.SaveAsync(Template("new-template", "sha256:new-template"));
        var newRow = await fixture.Context.WorkflowExecutables.AsNoTracking()
            .SingleAsync(row => row.Id != original.Id);
        Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion, newRow.SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion,
            (await fixture.Context.ExecutableActivityTemplates.AsNoTracking().SingleAsync()).SchemaVersion);
        Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion,
            (await fixture.Context.ExecutableActivityTemplateHashClaims.AsNoTracking().SingleAsync()).SchemaVersion);
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            var adopted = await fixture.Sources.StageCreateOrAdoptAsync(reference, CancellationToken.None);
            Assert.False(adopted.Created);
            await fixture.Context.SaveChangesAsync();
            await transaction.CommitAsync();
        }
        fixture.Context.ChangeTracker.Clear();
        var adoptedRow = await fixture.Context.WorkflowExecutableSourceReferences.AsNoTracking().SingleAsync();
        Assert.Equal(originalReference.ContentJson, adoptedRow.ContentJson);
        Assert.Equal(originalReference.IncarnationId, adoptedRow.IncarnationId);
        Assert.Equal(originalReference.Revision + 1, adoptedRow.Revision);
        Assert.Equal(RuntimeArtifactEfModule.PreparationSchemaVersion, adoptedRow.SchemaVersion);
        var untouched = await fixture.Context.WorkflowExecutables.AsNoTracking().SingleAsync(row => row.Id == original.Id);
        Assert.Equal(original.ContentJson, untouched.ContentJson);
        Assert.Equal(original.SchemaVersion, untouched.SchemaVersion);
        Assert.Equal(original.IncarnationId, untouched.IncarnationId);
    }

    [Fact]
    public async Task Metadata_save_is_atomic_with_rollback_and_stale_stamps_cannot_bypass_the_current_gate()
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizePreparationAsync();
        var snapshot = PreparationSnapshot(templateOnly: true);
        await using (var transaction = await fixture.Context.Database.BeginTransactionAsync())
        {
            fixture.Context.WorkflowPreparationRetentions.Add(WorkflowPreparationRetentionCodec.Create(snapshot,
                PreparationWriteVersion(fixture.Context)));
            await fixture.Context.SaveChangesAsync();
            Assert.Single(await fixture.Context.WorkflowPreparationRetentions.AsNoTracking().ToArrayAsync());
            await transaction.RollbackAsync();
        }
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
        var stale = PreparationRow(snapshot);
        stale.SchemaVersion = RuntimeArtifactEfModule.SchemaVersion;
        fixture.Context.WorkflowPreparationRetentions.Add(stale);
        await Assert.ThrowsAsync<EfSchemaWriteRefusedException>(() => fixture.Context.SaveChangesAsync());
        fixture.Context.ChangeTracker.Clear();
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
    }

    [Fact]
    public async Task A_predecessor_readability_declaration_cannot_activate_after_preparation_finalization()
    {
        await using var fixture = await PreparationGateFixture.CreateAsync();
        await fixture.FinalizePreparationAsync();
        var current = fixture.Gate.Families.DeclarationOf(RuntimeArtifactEfModule.SchemaFamily);
        // This is the actual module gate with a predecessor declaration, not an old binary/provider qualification.
        var predecessor = current with { CurrentVersion = RuntimeArtifactEfModule.SchemaVersion, Upcasters = [],
            Entities = current.Entities.Where(entity => entity != typeof(WorkflowPreparationRetentionEntity)).ToArray(),
            ContentColumns = current.ContentColumns.Where(column => column.Entity != typeof(WorkflowPreparationRetentionEntity)).ToArray() };
        var oldGate = new EfSchemaModuleGate(EfSchemaModuleFamilies.FromDeclarations("Workflows.Runtime", [predecessor]),
            null, new EfSchemaFinalizationObservations(), new EfSchemaFinalizationOptions());
        await Assert.ThrowsAsync<EfSchemaActivationRefusedException>(() => oldGate.ActivateAsync(fixture.Context));
        Assert.Empty(await fixture.Context.WorkflowPreparationRetentions.ToArrayAsync());
    }

    [Fact]
    public void Identity_upcast_preserves_legacy_bytes_and_cannot_create_preparation_documents_from_legacy_rows()
    {
        var original = new EfSchemaRowContent(typeof(WorkflowExecutableSourceReferenceEntity), ("ContentJson", "unchanged bytes"));
        var upcaster = new RuntimePreparationV11Upcaster();
        Assert.Same(original, upcaster.Upcast(original));
        Assert.Throws<InvalidDataException>(() => upcaster.Upcast(new EfSchemaRowContent(
            typeof(WorkflowPreparationRetentionEntity), ("ContentJson", "{}"))));
    }

    private static string PreparationWriteVersion(RuntimeDbContext context) => RuntimeSchemaWriteVersion.For(context,
        RuntimeArtifactEfModule.SchemaFamily, RuntimeArtifactEfModule.PreparationSchemaVersion);

    private sealed class PreparationGateFixture : IAsyncDisposable
    {
        private readonly SqliteConnection connection;
        private readonly ServiceProvider services;
        private PreparationGateFixture(SqliteConnection connection, ServiceProvider services, RuntimeSqliteDbContext context, EfSchemaModuleGate gate)
        {
            this.connection = connection;
            this.services = services;
            Context = context;
            Gate = gate;
            var access = new Accessor(PersistenceAccessContext.Scoped(new("tenant-a")));
            Executables = new(context, access);
            Sources = new(context, access, FenceCodec());
            Templates = new(context, access, FenceCodec());
        }
        public RuntimeSqliteDbContext Context { get; }
        public EfSchemaModuleGate Gate { get; }
        public EfWorkflowExecutableStore Executables { get; }
        public EfWorkflowExecutableSourceReferenceStore Sources { get; }
        public EfExecutableActivityTemplateStore Templates { get; }

        public static async Task<PreparationGateFixture> CreateAsync(params IInterceptor[] interceptors)
        {
            var connection = new SqliteConnection("Data Source=:memory:");
            await connection.OpenAsync();
            var gates = new EfSchemaFinalizationGates();
            var services = new ServiceCollection().AddSingleton(gates).BuildServiceProvider();
            var builder = new DbContextOptionsBuilder<RuntimeSqliteDbContext>().UseSqlite(connection).UseApplicationServiceProvider(services);
            EfSchemaWriteGateInterceptor.EnsureAdded(builder);
            if (interceptors.Length > 0) builder.AddInterceptors(interceptors);
            var context = new RuntimeSqliteDbContext(builder.Options);
            try
            {
                await context.Database.EnsureCreatedAsync();
                var gate = new EfSchemaModuleGate(EfSchemaModuleFamilies.ForContext(typeof(RuntimeSqliteDbContext))!,
                    null, new EfSchemaFinalizationObservations(), new EfSchemaFinalizationOptions());
                await gate.ActivateAsync(context);
                gates.Register(typeof(RuntimeDbContext), gate);
                return new(connection, services, context, gate);
            }
            catch
            {
                await context.DisposeAsync();
                await services.DisposeAsync();
                await connection.DisposeAsync();
                throw;
            }
        }

        public Task FinalizePreparationAsync() => FinalizeArtifactAsync(RuntimeArtifactEfModule.PreparationSchemaVersion);

        public async Task FinalizeArtifactAsync(string version)
        {
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(Context), RuntimeArtifactEfModule.SchemaFamily,
                [RuntimeArtifactEfModule.SchemaVersion, RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, RuntimeArtifactEfModule.PreparationSchemaVersion], version);
            await Gate.RefreshAsync(Context);
        }

        public async Task<EfSchemaModuleGate> FinalizeElsewhereWithoutRefreshingWriterAsync(string version)
        {
            var peer = new EfSchemaModuleGate(EfSchemaModuleFamilies.ForContext(typeof(RuntimeSqliteDbContext))!,
                null, new EfSchemaFinalizationObservations(), new EfSchemaFinalizationOptions());
            await peer.ActivateAsync(Context);
            await EfSchemaFinalizationTestSupport.FinalizeAsync(new EfSchemaFinalizationStore(Context), RuntimeArtifactEfModule.SchemaFamily,
                [RuntimeArtifactEfModule.SchemaVersion, RuntimeArtifactEfModule.ScopedPublicationSchemaVersion, RuntimeArtifactEfModule.PreparationSchemaVersion], version);
            await peer.RefreshAsync(Context);
            Assert.Equal(version, peer.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            Assert.Equal(RuntimeArtifactEfModule.SchemaVersion, Gate.StateOf(RuntimeArtifactEfModule.SchemaFamily)!.WriteVersion);
            return peer;
        }

        public async ValueTask DisposeAsync()
        {
            await Context.DisposeAsync();
            await services.DisposeAsync();
            await connection.DisposeAsync();
        }
    }
}
