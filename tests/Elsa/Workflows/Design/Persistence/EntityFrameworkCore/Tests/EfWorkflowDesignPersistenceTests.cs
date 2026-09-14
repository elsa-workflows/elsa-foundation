using System.Data.Common;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Events.Core.Contracts;
using Elsa.Primitives.Contracts;
using Elsa.Primitives.Exceptions;
using Elsa.Workflows.Design.Core.Events;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Design.Persistence.Core.Atomic;
using Elsa.Workflows.Design.Persistence.Core.Contracts;
using Elsa.Workflows.Design.Persistence.Core.Constants;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Filters;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Stores;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Commands;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Entities;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Serialization.Core;
using Elsa.Locking.Core;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Elsa.Workflows.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class EfWorkflowDesignPersistenceTests
{
    [Fact]
    public async Task Definition_ids_follow_groundwork_identity_folding_and_tenants_remain_ordinal()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "Alpha", TenantId = "TenantA", Name = "first" },
            new WorkflowDefinition { Id = "Alpha", TenantId = "tenanta", Name = "second" });
        await db.SaveChangesAsync();

        var tenantA = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("TenantA")));
        var tenanta = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenanta")));
        Assert.Equal("first", (await new EfWorkflowDefinitionStore(db, tenantA).FindByIdAsync("aLPHA"))!.Name);
        Assert.Equal("second", (await new EfWorkflowDefinitionStore(db, tenanta).FindByIdAsync("ALPHA"))!.Name);
        Assert.Equal(2, await db.Definitions.CountAsync());

        db.Definitions.Add(new WorkflowDefinition { Id = "alpha", TenantId = "TenantA", Name = "duplicate" });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Exact_name_query_is_not_subject_to_the_free_text_candidate_cap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(Enumerable.Range(0, 1_001).Select(index => new WorkflowDefinition
        {
            Id = $"exact-{index:D4}", TenantId = "tenant-a", Name = "same-name"
        }));
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(1_001, (await store.ListAsync(new WorkflowDefinitionFilter { Name = "same-name" })).Count);
    }

    [Fact]
    public async Task Search_term_preserves_leading_and_trailing_spaces()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.AddRange(
            new WorkflowDefinition { Id = "with-spaces", TenantId = "tenant-a", Name = "  Order  " },
            new WorkflowDefinition { Id = "without-spaces", TenantId = "tenant-a", Name = "Order" });
        await db.SaveChangesAsync();

        var store = new EfWorkflowDefinitionStore(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(["with-spaces"], (await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = " Order " })).Select(x => x.Id));
    }

    [Fact]
    public async Task Definition_text_bounds_are_rejected_without_truncation_and_search_keys_fit()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition
        {
            Id = new string('i', 128), TenantId = "tenant-a", Name = new string('n', 256), Description = new string('d', 256)
        });
        await db.SaveChangesAsync();
        var keys = await db.Definitions.AsNoTracking().Select(x => new
        {
            Id = EF.Property<string>(x, "IdSearchKey"),
            Hash = EF.Property<string>(x, "IdLookupHash")
        }).SingleAsync();
        Assert.Equal(WorkflowDefinitionLimits.IdentitySearchKeyMaximumLength, keys.Id.Length);
        Assert.Equal(64, keys.Hash.Length);

        db.Definitions.Add(new WorkflowDefinition { Id = "too-long", TenantId = "tenant-a", Name = new string('x', 257) });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        Assert.Equal(1, await db.Definitions.CountAsync());
    }

    [Fact]
    public async Task Definition_search_uses_provider_neutral_unicode_ordinal_ignore_case_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access); var serializer = new TestSerializer(); var identities = new TestIdentity();
        var command = new EfAddWorkflowDefinitionCommand(db, access, writer, serializer, identities);
        await command.Execute(new DesignOperationKey("unicode-cafe"),
            new WorkflowDefinition { Id = "cafe", TenantId = "tenant-a", Name = "Café" },
            new WorkflowDefinitionDraft { Id = "cafe-draft", TenantId = "tenant-a", WorkflowDefinitionId = "cafe", State = State() });
        await command.Execute(new DesignOperationKey("unicode-deseret"),
            new WorkflowDefinition { Id = "deseret", TenantId = "tenant-a", Name = "𐐀" },
            new WorkflowDefinitionDraft { Id = "deseret-draft", TenantId = "tenant-a", WorkflowDefinitionId = "deseret", State = State() });
        await command.Execute(new DesignOperationKey("unicode-sharp-s"),
            new WorkflowDefinition { Id = "sharp-s", TenantId = "tenant-a", Name = "Straße" },
            new WorkflowDefinitionDraft { Id = "sharp-s-draft", TenantId = "tenant-a", WorkflowDefinitionId = "sharp-s", State = State() });

        var store = new EfWorkflowDefinitionStore(db, access);
        var persistedKeys = await db.Definitions.AsNoTracking().Select(x => new { x.Name, Key = EF.Property<string?>(x, "NameSearchKey") }).ToListAsync();
        Assert.Equal("|010400", persistedKeys.Single(x => x.Name == "𐐀").Key);
        Assert.Equal("cafe", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "CAFÉ" })).Id);
        Assert.Equal("deseret", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "𐐨" })).Id);
        Assert.Equal("sharp-s", Assert.Single(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "STRAßE" })).Id);
        Assert.Empty(await store.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "STRAẞE" }));
    }

    [Fact]
    public void Sqlite_model_uses_portable_unbounded_text_and_groundwork_operation_bounds()
    {
        using var connection = new SqliteConnection("Data Source=:memory:"); connection.Open();
        using var db = Create(connection);
        var operation = db.Model.FindEntityType(typeof(DesignOperationEntity))!;
        Assert.Equal(256, operation.FindProperty(nameof(DesignOperationEntity.OperationKind))!.GetMaxLength());
        Assert.Equal(256, operation.FindProperty(nameof(DesignOperationEntity.OperationKey))!.GetMaxLength());
        Assert.Equal("TEXT", operation.FindProperty(nameof(DesignOperationEntity.ResultJson))!.GetColumnType());
        Assert.Equal("TEXT", db.Model.FindEntityType(typeof(WorkflowDefinitionVersion))!.FindProperty(nameof(WorkflowDefinitionVersion.StateSource))!.GetColumnType());
    }

    [Fact]
    public void MySql_model_declares_provider_spike_collation_without_cross_provider_leakage()
    {
        // SQLite supplies a relational model builder here; the assertions target the provider
        // metadata emitted by the MySQL context without adding a provider dependency to this test.
        using var db = new WorkflowsDesignMySqlDbContext(new DbContextOptionsBuilder<WorkflowsDesignMySqlDbContext>().UseSqlite("Data Source=:memory:").Options);
        Assert.Equal(WorkflowsDesignMySqlDbContext.CharacterSet, db.Model.FindAnnotation("MySQL:Charset")?.Value);
        var model = db.GetService<IDesignTimeModel>().Model;
        Assert.Equal(WorkflowsDesignMySqlDbContext.Collation, model.GetCollation());
        Assert.All(model.GetEntityTypes(), entity =>
            Assert.Equal(WorkflowsDesignMySqlDbContext.Collation, entity.FindAnnotation("MySQL:Collation")?.Value));
    }

    [Fact]
    public async Task Operation_identity_over_bound_is_rejected_without_truncation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey(new string('k', 257)), "test.op", new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "never-staged" })));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Operation_identity_at_the_shared_bound_is_accepted_for_key_and_kind()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var result = await writer.ExecuteAsync(
            new DesignOperationKey(new string('k', DesignOperationKey.MaximumLength)),
            new string('o', DesignOperationKey.MaximumLength), new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "staged" }));
        Assert.Equal("staged", result.Id);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Operation_kind_over_bound_is_rejected_without_truncation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWriter(db, new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey("bounded-key"), new string('o', DesignOperationKey.MaximumLength + 1), new { Value = 1 }, ["test"],
            _ => Task.FromResult(new { Id = "never-staged" })));
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Provider_read_failures_are_normalized_at_the_public_boundary()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        await connection.CloseAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => new EfWorkflowDefinitionStore(db, access).FindByIdAsync("missing"));
        Assert.Equal(DesignPersistenceFailureKind.Provider, failure.FailureKind);
    }

    [Fact]
    public void Ef_registration_resolves_owned_surfaces_and_preserves_custom_atomic_writer()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IPersistenceAccessContextAccessor>(new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        services.AddSingleton<IPayloadSerializer, TestSerializer>();
        services.AddSingleton<IIdentityGenerator, TestIdentity>();
        services.AddSingleton<IActivityStructureService, EmptyActivityStructureService>();
        services.AddScoped<IDesignAtomicWriter, CustomDesignAtomicWriter>();
        services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=:memory:"
        });

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        var serviceProvider = scope.ServiceProvider;

        Assert.IsType<CustomDesignAtomicWriter>(serviceProvider.GetRequiredService<IDesignAtomicWriter>());
        _ = serviceProvider.GetRequiredService<WorkflowsDesignDbContext>();
        _ = serviceProvider.GetRequiredService<WorkflowsDesignSqliteDbContext>();
        foreach (var serviceType in new[]
                 {
                     typeof(EfWorkflowDefinitionStore), typeof(EfWorkflowDefinitionVersionStore), typeof(EfWorkflowDefinitionDraftStore),
                     typeof(EfWorkflowDefinitionVersionLayoutStore), typeof(EfWorkflowDefinitionListProjectionStore),
                     typeof(IWorkflowDefinitionStore), typeof(IWorkflowDefinitionVersionStore), typeof(IWorkflowDefinitionDraftStore),
                     typeof(IWorkflowDefinitionVersionLayoutStore), typeof(IWorkflowDefinitionListProjectionStore),
                     typeof(IAddWorkflowDefinitionCommand), typeof(IAddWorkflowDefinitionVersionCommand), typeof(ICreateDraftCommand),
                     typeof(ICloneDraftFromVersionCommand), typeof(IDeleteWorkflowDefinitionPermanentlyCommand), typeof(IDiscardDraftCommand),
                     typeof(IMaterializeWorkflowDefinitionCommand), typeof(IMaterializeWorkflowDefinitionVersionCommand),
                     typeof(IPromoteDraftToVersionCommand), typeof(ISaveWorkflowDefinitionCommand), typeof(ISubmitWorkflowDefinitionCommand),
                     typeof(IUpdateDraftCommand)
                 })
            Assert.NotNull(serviceProvider.GetRequiredService(serviceType));
    }

    [Fact]
    public void Ef_registration_rejects_a_different_selected_backend()
    {
        var services = new ServiceCollection();
        services.AddSingleton<object>(new object());
        var owned = Assert.Single(services, x => x.ServiceType == typeof(object));
        services.AddSingleton(new DesignPersistenceBackend(DesignPersistenceBackend.Groundwork, [owned]));

        var exception = Assert.Throws<InvalidOperationException>(() => services.AddWorkflowsDesignEntityFrameworkCore(new WorkflowsDesignEntityFrameworkCoreOptions()));

        Assert.Contains("already selected", exception.Message, StringComparison.Ordinal);
        Assert.Contains(owned, services);
    }

    [Fact]
    public void Ef_feature_registration_flows_settings_through_ConfigureServices()
    {
        var services = new ServiceCollection();
        var feature = new WorkflowsDesignEntityFrameworkCoreFeature
        {
            Provider = "Sqlite",
            ConnectionString = "Data Source=feature.db",
            ConnectionName = "workflow-design"
        };

        feature.ConfigureServices(services);

        var options = Assert.Single(services, x => x.ServiceType == typeof(WorkflowsDesignEntityFrameworkCoreOptions)).ImplementationInstance as WorkflowsDesignEntityFrameworkCoreOptions;
        Assert.NotNull(options);
        Assert.Equal(feature.Provider, options!.Provider);
        Assert.Equal(feature.ConnectionString, options.ConnectionString);
        Assert.Equal(feature.ConnectionName, options.ConnectionName);
        Assert.Contains(services, x => x.ServiceType == typeof(IDesignAtomicWriter));
    }

    [Fact]
    public async Task Marker_race_replay_does_not_publish_lifecycle_events_for_any_draft_command()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        var events = new CapturingDeferredEventPublisher();
        var atomic = new ReplayedAtomicWriter();

        await new EfCreateDraftCommand(db, access, atomic, new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("create-race"), "definition");
        await new EfCloneDraftFromVersionCommand(db, access, atomic, new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("clone-race"), "version");
        await new EfUpdateDraftCommand(db, access, atomic, serializer, new EmptyActivityStructureService(), new TestLockProvider(), deferredEvents: events)
            .Execute(new DesignOperationKey("update-race"), new UpdateDraftRequest("draft", State(), []));
        await new EfDiscardDraftCommand(db, access, atomic, new TestLockProvider(), events)
            .Execute(new DesignOperationKey("discard-race"), "draft");

        Assert.Empty(events.Events);
    }

    [Fact]
    public async Task Save_workflow_definition_covers_lookup_tenant_validation_and_failure_branches()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        db.Definitions.Add(new WorkflowDefinition { Id = "stored", TenantId = "tenant-a", Name = "before" });
        await db.SaveChangesAsync();
        var command = new EfSaveWorkflowDefinitionCommand(db, access, writer);

        await command.Execute(new DesignOperationKey("save-success"), new WorkflowDefinition { Id = "STORED", TenantId = "tenant-a", Name = "after" });
        Assert.Equal("after", (await db.Definitions.SingleAsync()).Name);
        await Assert.ThrowsAsync<EntityNotFoundException>(() => command.Execute(new DesignOperationKey("save-missing"), new WorkflowDefinition { Id = "missing", TenantId = "tenant-a", Name = "missing" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => command.Execute(new DesignOperationKey("save-tenant"), new WorkflowDefinition { Id = "stored", TenantId = "tenant-b", Name = "wrong tenant" }));
        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(new DesignOperationKey("save-invalid"), new WorkflowDefinition { Id = "stored", TenantId = "tenant-a", Name = new string('x', WorkflowDefinitionLimits.TextMaximumLength + 1) }));
    }

    [Fact]
    public async Task SQLite_save_validates_bounded_version_draft_layout_and_operation_identities()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Versions.Add(new WorkflowDefinitionVersion("definition", $"1.0.0-{new string('a', 123)}") { Id = "version", TenantId = "tenant-a" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Drafts.Add(new WorkflowDefinitionDraft { Id = new string('d', 129), TenantId = "tenant-a", WorkflowDefinitionId = "definition", StateSource = "{}" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        db.Operations.Add(new DesignOperationEntity { TenantId = "tenant-a", OperationKind = new string('o', DesignOperationKey.MaximumLength + 1), OperationKey = "key", RequestFingerprint = "request", ResultFingerprint = "result", ResultJson = "{}" });
        await Assert.ThrowsAsync<ArgumentException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task Definitions_drafts_layouts_and_versions_survive_reopen_and_preserve_scope()
    {
        var path = Path.Join(Path.GetTempPath(), $"elsa-workflow-design-reopen-{Guid.NewGuid():N}.db");
        try
        {
            var serializer = new TestSerializer(); var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
            await using (var connection = new SqliteConnection($"Data Source={path}"))
            {
                await connection.OpenAsync();
                await using var db = Create(connection);
                await db.Database.EnsureCreatedAsync(); var atomic = new EfDesignAtomicWriter(db, accessor); var identities = new TestIdentity();
                var definition = new WorkflowDefinition { Id = "definition-1", TenantId = "tenant-a", Name = "Order" }; var draft = new WorkflowDefinitionDraft { Id = "draft-1", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
                var add = new EfAddWorkflowDefinitionCommand(db, accessor, atomic, serializer, identities); await add.Execute(new DesignOperationKey("create-1"), definition, draft, [new DesignMetadataRecord("root", 1, 2)]);
                var definitions = new EfWorkflowDefinitionStore(db, accessor); Assert.Single(await definitions.ListAsync(new WorkflowDefinitionFilter { SearchTerm = "ord" }));
                var drafts = new EfWorkflowDefinitionDraftStore(db, serializer, accessor); var loadedLayout = await drafts.FindWithLayoutByIdAsync(draft.Id); Assert.NotNull(loadedLayout); Assert.Single(loadedLayout!.Layout);
                var version = new EfAddWorkflowDefinitionVersionCommand(db, accessor, atomic, serializer, identities, new TestLockProvider()); var added = await version.Execute(new DesignOperationKey("version-1"), definition.Id, State()); Assert.Equal("1.0.0", added.Version);
            }
            await using (var reopenedConnection = new SqliteConnection($"Data Source={path}"))
            {
                await reopenedConnection.OpenAsync();
                await using var reopened = Create(reopenedConnection);
                var store = new EfWorkflowDefinitionStore(reopened, accessor); Assert.NotNull(await store.FindByIdAsync("definition-1"));
                var versions = new EfWorkflowDefinitionVersionStore(reopened, serializer, store, accessor); Assert.Equal("1.0.0", (await versions.FindLatestVersionAsync("definition-1"))!.Version);
            }
            await using (var scopeConnection = new SqliteConnection($"Data Source={path}"))
            {
                await scopeConnection.OpenAsync();
                await using var scopedDb = Create(scopeConnection);
                var other = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))); Assert.Null(await new EfWorkflowDefinitionStore(scopedDb, other).FindByIdAsync("definition-1"));
            }
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public async Task Operation_key_replays_and_conflicting_request_is_rejected()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var writer = new EfDesignAtomicWriter(db, access); var key = new DesignOperationKey("same"); var first = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "winner" })); var replay = await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "loser" })); Assert.Equal(first.Id, replay.Id); await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "conflict" })));
    }

    [Fact]
    public async Task Operation_fingerprint_is_canonical_across_request_property_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("canonical-request");
        await writer.ExecuteAsync(key, "test.op", new { A = "é", B = "東京" }, ["test"], (_, _) => Task.FromResult(DesignAtomicWriteStage<string>.Accepted("winner")));
        var replay = await writer.ExecuteAsync<string>(key, "test.op", new { B = "東京", A = "é" }, ["test"], (_, _) => throw new InvalidOperationException("canonical replay must not restage"));
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal("winner", replay.Value);
    }

    [Fact]
    public async Task State_request_material_uses_the_configured_payload_serializer_and_groundwork_framing()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))); var state = State();
        var serializerOptions = new JsonSerializerOptions { PropertyNamingPolicy = null }; var serializer = new TestSerializer(serializerOptions); var identity = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, access);
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" }); await db.SaveChangesAsync();
        await new EfAddWorkflowDefinitionVersionCommand(db, access, writer, serializer, identity, new TestLockProvider()).Execute(new DesignOperationKey("serializer-request"), "definition", state);
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "serializer-request");
        var stateJson = serializer.Serialize(state);
        var materialJson = JsonSerializer.Serialize(new { definitionId = "definition", stateJson });
        Assert.Equal(GroundworkFingerprint("workflow.version.add.v1", materialJson), marker.RequestFingerprint);
    }

    [Fact]
    public async Task Projection_batches_definition_ids_without_truncating_rows_and_preserves_scope_and_order()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync(); await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var tenant = "tenant-a"; var definitions = Enumerable.Range(0, 205).Select(index => new WorkflowDefinition { Id = $"definition-{index:D3}", TenantId = tenant, Name = $"Definition {index:D3}" }).ToArray();
        db.Definitions.AddRange(definitions);
        var oldDraft = new WorkflowDefinitionDraft { Id = "draft-old", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch, LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        var currentDraft = new WorkflowDefinitionDraft { Id = "draft-current", TenantId = tenant, WorkflowDefinitionId = definitions[0].Id, CreatedAt = DateTimeOffset.UnixEpoch.AddDays(2), LastModifiedAt = DateTimeOffset.UnixEpoch.AddDays(1), StateSource = "{}" };
        db.Drafts.AddRange([oldDraft, currentDraft]);
        foreach (var definition in definitions.Skip(1))
            db.Drafts.Add(new WorkflowDefinitionDraft { Id = $"draft-{definition.Id}", TenantId = tenant, WorkflowDefinitionId = definition.Id, StateSource = "{}" });
        db.Versions.AddRange(Enumerable.Range(1, 201).Select(number => new WorkflowDefinitionVersion(definitions[0].Id, $"{number}.0.0") { Id = $"version-{number:D3}", TenantId = tenant }));
        foreach (var definition in definitions.Skip(1))
            db.Versions.Add(new WorkflowDefinitionVersion(definition.Id, "1.0.0") { Id = $"version-{definition.Id}", TenantId = tenant });
        db.Definitions.Add(new WorkflowDefinition { Id = "tenant-b-only", TenantId = "tenant-b", Name = "Foreign" });
        db.Versions.Add(new WorkflowDefinitionVersion("tenant-b-only", "9.0.0") { Id = "foreign-version", TenantId = "tenant-b" });
        await db.SaveChangesAsync();

        var requested = definitions.Select(x => x.Id).Reverse().Append("tenant-b-only").Append(definitions[0].Id).Append(definitions[0].Id.ToUpperInvariant()).ToArray();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope(tenant)));
        var projections = await new EfWorkflowDefinitionListProjectionStore(db, access).ListByDefinitionIdsAsync(requested);
        Assert.Equal(206, projections.Count);
        Assert.Equal(requested.GroupBy(WorkflowDefinitionIdentity.Fold, StringComparer.Ordinal).Select(group => group.First()), projections.Select(x => x.WorkflowDefinitionId));
        var first = Assert.Single(projections, x => x.WorkflowDefinitionId == definitions[0].Id);
        Assert.Equal("draft-current", first.DraftId);
        Assert.Equal("version-201", first.LatestVersionId);
        Assert.Equal("201.0.0", first.LatestVersion);
        Assert.Equal(201, first.VersionCount);
        var foreign = Assert.Single(projections, x => x.WorkflowDefinitionId == "tenant-b-only");
        Assert.Null(foreign.DraftId); Assert.Null(foreign.LatestVersionId); Assert.Null(foreign.LatestVersion); Assert.Equal(0, foreign.VersionCount);
    }

    [Fact]
    public async Task Same_ids_and_operation_keys_are_isolated_by_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var a = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var b = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b")));
        var serializer = new TestSerializer(); var identities = new TestIdentity();
        var first = new EfDesignAtomicWriter(db, access: a);
        var second = new EfDesignAtomicWriter(db, access: b);
        await first.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "a" }));
        var replay = await second.ExecuteAsync(new DesignOperationKey("same"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "b" }));
        Assert.Equal("b", replay.Id);
        var addA = new EfAddWorkflowDefinitionCommand(db, a, first, serializer, identities);
        var addB = new EfAddWorkflowDefinitionCommand(db, b, second, serializer, identities);
        await addA.Execute(new DesignOperationKey("add-a"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-a", Name = "A" }, new WorkflowDefinitionDraft { Id = "draft-a", TenantId = "tenant-a", WorkflowDefinitionId = "shared", State = State() });
        await addB.Execute(new DesignOperationKey("add-b"), new WorkflowDefinition { Id = "shared", TenantId = "tenant-b", Name = "B" }, new WorkflowDefinitionDraft { Id = "draft-b", TenantId = "tenant-b", WorkflowDefinitionId = "shared", State = State() });
        Assert.Equal("A", (await new EfWorkflowDefinitionStore(db, a).GetAsync("shared")).Name);
        Assert.Equal("B", (await new EfWorkflowDefinitionStore(db, b).GetAsync("shared")).Name);
    }

    [Fact]
    public async Task Scope_less_mutations_are_rejected_and_corrupt_replays_fail_closed()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var global = new TestAccessor(PersistenceAccessContext.Global);
        var writer = new EfDesignAtomicWriter(db, access: global);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(new DesignOperationKey("global"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" })));

        var scoped = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var scopedWriter = new EfDesignAtomicWriter(db, access: scoped);
        await scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "x" }));
        var marker = await db.Operations.SingleAsync(x => x.OperationKey == "corrupt"); marker.ResultJson = "{\"Id\":\"tampered\"}"; await db.SaveChangesAsync();
        var corrupt = await Assert.ThrowsAsync<DesignPersistenceException>(() => scopedWriter.ExecuteAsync(new DesignOperationKey("corrupt"), "test.op", new { Value = 1 }, ["test"], _ => Task.FromResult(new { Id = "unused" })));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, corrupt.FailureKind);
    }

    [Fact]
    public async Task Promotion_copies_layout_and_presentation_into_write_once_version_sibling()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft, [new DesignMetadataRecord("root", 3, 4)], [new ActivityPresentationRecord("root", "Root", "Description")]);
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var versionId = await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider()).Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");
        var layout = await new EfWorkflowDefinitionVersionLayoutStore(db, accessor).FindByVersionIdAsync(versionId);
        Assert.NotNull(layout); Assert.Single(layout!.Records);
        Assert.Single(layout.ActivityPresentation);
    }

    [Fact]
    public async Task Promotion_acquires_draft_then_definition_and_rejects_semver_identity_conflicts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        db.Versions.Add(new WorkflowDefinitionVersion(definition.Id, "1.0.0")
        {
            Id = "existing-version", TenantId = "tenant-a", State = State(), StateSource = serializer.Serialize(State()), CreatedAt = DateTimeOffset.UtcNow, LastModifiedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
        var locks = new RecordingLockProvider();
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var command = new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, locks);

        var conflict = await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() =>
            command.Execute(new DesignOperationKey("promote-conflict"), draft.Id, "1.0.0+build.7"));

        Assert.Equal("1.0.0+build.7", conflict.Version);
        Assert.Equal(
            [WorkflowDesignPersistenceLockKeys.DraftKey(draft.Id), WorkflowDesignPersistenceLockKeys.DefinitionKey(definition.Id)],
            locks.Acquired);
    }

    [Fact]
    public async Task Promotion_maps_reused_operation_key_to_promotion_operation_conflict()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        var command = new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider());
        await command.Execute(new DesignOperationKey("promote"), draft.Id, "1.0.0");

        await Assert.ThrowsAsync<WorkflowPromotionOperationConflictException>(() =>
            command.Execute(new DesignOperationKey("promote"), draft.Id, "2.0.0"));
    }

    [Fact]
    public async Task Submit_validates_the_complete_activity_tree_before_persisting()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var writer = new EfDesignAtomicWriter(db, accessor);
        var command = new EfSubmitWorkflowDefinitionCommand(db, accessor, writer, serializer, new TestIdentity(), new EmptyActivityStructureService());

        await Assert.ThrowsAsync<ArgumentException>(() => command.Execute(
            new DesignOperationKey("submit-invalid"), "Invalid", null, new WorkflowDefinitionState([], null, [], [], null)));

        Assert.Empty(await db.Definitions.ToListAsync());
    }

    [Fact]
    public async Task Create_draft_resolves_definition_identity_and_write_scope_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "Stored-Definition", TenantId = "tenant-a", Name = "Definition" });
        await db.SaveChangesAsync();

        var events = new CapturingDeferredEventPublisher();
        var command = new EfCreateDraftCommand(db, accessor, new EfDesignAtomicWriter(db, accessor), new TestIdentity(), serializer, new TestLockProvider(), deferredEvents: events);
        var draftId = await command.Execute(new DesignOperationKey("create-canonical-draft"), "stored-definition");
        var draft = await db.Drafts.SingleAsync(x => x.Id == draftId);
        var created = Assert.Single(events.Events.OfType<DraftCreated>());

        Assert.Equal("Stored-Definition", draft.WorkflowDefinitionId);
        Assert.Equal("tenant-a", draft.TenantId);
        Assert.Equal("Stored-Definition", created.WorkflowDefinitionId);
    }

    [Fact]
    public async Task Submit_creates_the_normalized_draft_layout_sibling_atomically()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var result = await new EfSubmitWorkflowDefinitionCommand(
            db, accessor, new EfDesignAtomicWriter(db, accessor), new TestSerializer(), new TestIdentity(), new EmptyActivityStructureService())
            .Execute(new DesignOperationKey("submit-layout"), "Definition", null,
                State() with { RootActivity = new ActivityNode("root", "activity", [], []) });

        var layout = await db.DraftLayouts.SingleAsync(x => x.WorkflowDefinitionDraftId == result.DraftId);
        Assert.Equal("tenant-a", layout.TenantId);
        Assert.Empty(layout.Records);
        Assert.Empty(layout.ActivityPresentation);
    }

    [Fact]
    public async Task Version_and_version_layout_immutable_source_fields_reject_after_save_changes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        var sourceCreatedAt = DateTimeOffset.UnixEpoch;
        var version = new WorkflowDefinitionVersion("definition", "1.0.0", "{}", sourceCreatedAt)
        {
            Id = "version", TenantId = "tenant-a", SourceDraftId = "draft"
        };
        var layout = new WorkflowDefinitionVersionLayout
        {
            Id = "layout", TenantId = "tenant-a", WorkflowDefinitionVersionId = version.Id,
            Records = [new DesignMetadataRecord("root", 1, 2)]
        };
        db.Versions.Add(version); db.Entry(layout).Property<string>("RecordsJson").CurrentValue = "[{\"nodeId\":\"root\",\"x\":1,\"y\":2,\"width\":null,\"height\":null,\"additionalProperties\":null}]"; db.Entry(layout).Property<string>("ActivityPresentationJson").CurrentValue = "[]"; db.VersionLayouts.Add(layout);
        await db.SaveChangesAsync();

        version.StateSource = "{\"changed\":true}";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        var loadedVersion = await db.Versions.SingleAsync();
        db.Entry(loadedVersion).Property(nameof(WorkflowDefinitionVersion.SemVerSortKey)).CurrentValue = "changed";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        db.ChangeTracker.Clear();
        var loadedLayout = await db.VersionLayouts.SingleAsync();
        db.Entry(loadedLayout).Property<string>("RecordsJson").CurrentValue = "[]";
        Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
    }

    [Fact]
    public async Task Update_draft_prunes_presentation_for_unreachable_activity_nodes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);
        var definition = new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" };
        var draft = new WorkflowDefinitionDraft { Id = "draft", TenantId = "tenant-a", WorkflowDefinitionId = definition.Id, State = State() };
        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities).Execute(new DesignOperationKey("add"), definition, draft);
        var root = new ActivityNode("root", "activity", [], []);
        var command = new EfUpdateDraftCommand(db, accessor, writer, serializer, new EmptyActivityStructureService(), new TestLockProvider());

        await command.Execute(new DesignOperationKey("update"), new UpdateDraftRequest(
            draft.Id,
            new WorkflowDefinitionState([], root, [], [], null),
            [],
            [new ActivityPresentationRecord("root", "Root", null), new ActivityPresentationRecord("ghost", "Ghost", null)]));

        var loaded = await new EfWorkflowDefinitionDraftStore(db, serializer, accessor).FindWithLayoutByIdAsync(draft.Id);
        Assert.Equal("root", Assert.Single(loaded!.ActivityPresentation).NodeId);
    }

    [Fact]
    public async Task Operation_fingerprints_cover_workflow_metadata_and_provenance_fields()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(); var identities = new TestIdentity(); var writer = new EfDesignAtomicWriter(db, accessor);

        var add = new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities);
        async Task AssertAddConflictAsync(string suffix, WorkflowDefinition changedDefinition, WorkflowDefinitionDraft changedDraft)
        {
            var originalDefinition = new WorkflowDefinition { Id = $"definition-{suffix}", TenantId = "tenant-a", Name = "Definition" };
            var originalDraft = new WorkflowDefinitionDraft { Id = $"draft-{suffix}", TenantId = "tenant-a", WorkflowDefinitionId = originalDefinition.Id, State = State() };
            changedDefinition.Id = originalDefinition.Id;
            changedDraft.Id = originalDraft.Id;
            changedDraft.WorkflowDefinitionId = originalDefinition.Id;
            await add.Execute(new DesignOperationKey($"add-{suffix}"), originalDefinition, originalDraft);
            await Assert.ThrowsAsync<InvalidOperationException>(() => add.Execute(new DesignOperationKey($"add-{suffix}"), changedDefinition, changedDraft));
        }

        await AssertAddConflictAsync("deleted-at", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", DeletedAt = DateTimeOffset.UnixEpoch }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("deleted-reason", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", DeletedReason = "source changed" }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("source-owned", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition", IsSourceOwned = true }, new WorkflowDefinitionDraft { TenantId = "tenant-a", State = State() });
        await AssertAddConflictAsync("source-version", new WorkflowDefinition { TenantId = "tenant-a", Name = "Definition" }, new WorkflowDefinitionDraft { TenantId = "tenant-a", SourceVersionId = "source-version", State = State() });

        var materializeDefinition = new EfMaterializeWorkflowDefinitionCommand(db, accessor, writer);
        var materializedDefinition = new WorkflowDefinition { Id = "materialized-definition", TenantId = "tenant-a", Name = "Definition", DeletedReason = "one" };
        await materializeDefinition.Execute(new DesignOperationKey("materialize-definition"), materializedDefinition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeDefinition.Execute(new DesignOperationKey("materialize-definition"), new WorkflowDefinition { Id = materializedDefinition.Id, TenantId = "tenant-a", Name = "Definition", DeletedReason = "two" }));

        var materializeVersion = new EfMaterializeWorkflowDefinitionVersionCommand(db, accessor, writer, serializer);
        var firstVersion = new WorkflowDefinitionVersion("definition-deleted-at", "2.0.0") { Id = "version-1", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch };
        await materializeVersion.Execute(new DesignOperationKey("materialize-version-draft"), firstVersion);
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeVersion.Execute(new DesignOperationKey("materialize-version-draft"), new WorkflowDefinitionVersion("definition-deleted-at", "2.0.0") { Id = firstVersion.Id, TenantId = "tenant-a", State = State(), SourceDraftId = "draft-b", SourceCreatedAt = DateTimeOffset.UnixEpoch }));
        await materializeVersion.Execute(new DesignOperationKey("materialize-version-created"), new WorkflowDefinitionVersion("definition-deleted-at", "3.0.0") { Id = "version-2", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch });
        await Assert.ThrowsAsync<InvalidOperationException>(() => materializeVersion.Execute(new DesignOperationKey("materialize-version-created"), new WorkflowDefinitionVersion("definition-deleted-at", "3.0.0") { Id = "version-2", TenantId = "tenant-a", State = State(), SourceDraftId = "draft-a", SourceCreatedAt = DateTimeOffset.UnixEpoch.AddDays(1) }));
    }

    [Fact]
    public async Task Operation_request_material_matches_groundwork_for_layout_and_promotion()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer(new JsonSerializerOptions { PropertyNamingPolicy = null });
        var identities = new TestIdentity();
        var writer = new EfDesignAtomicWriter(db, accessor);
        var state = State();
        var definition = new WorkflowDefinition
        {
            Id = "definition-parity",
            TenantId = "tenant-a",
            Name = "Parity",
            Description = "Description",
            DeletedAt = DateTimeOffset.UnixEpoch,
            DeletedReason = "source",
            IsSourceOwned = true
        };
        var draft = new WorkflowDefinitionDraft
        {
            Id = "draft-parity",
            TenantId = "tenant-a",
            WorkflowDefinitionId = definition.Id,
            SourceVersionId = "source-version",
            State = state
        };
        var layout = new DesignMetadataRecord("root", 1, 2, 3, 4);
        var presentation = new ActivityPresentationRecord("root", "Root", "Description");

        await new EfAddWorkflowDefinitionCommand(db, accessor, writer, serializer, identities)
            .Execute(new DesignOperationKey("parity-create"), definition, draft, [layout], [presentation]);

        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var expectedCreateJson = JsonSerializer.Serialize(new
        {
            name = definition.Name,
            description = definition.Description,
            deletedAt = definition.DeletedAt,
            deletedReason = definition.DeletedReason,
            isSourceOwned = definition.IsSourceOwned,
            sourceVersionId = draft.SourceVersionId,
            stateJson = serializer.Serialize(state),
            layout = new[] { new { nodeId = "root", x = 1d, y = 2d, width = 3d, height = 4d, additionalPropertiesJson = (string?)null } },
            activityPresentation = new[] { new { nodeId = "root", displayName = "Root", description = "Description" } }
        }, json);
        var createMarker = await db.Operations.SingleAsync(x => x.OperationKey == "parity-create");
        Assert.Equal(GroundworkFingerprint("workflow.definition.create.v1", expectedCreateJson), createMarker.RequestFingerprint);

        var versionStore = new EfWorkflowDefinitionVersionStore(db, serializer, new EfWorkflowDefinitionStore(db, accessor), accessor);
        await new EfPromoteDraftToVersionCommand(db, accessor, writer, serializer, identities, versionStore, new TestLockProvider())
            .Execute(new DesignOperationKey("parity-promote"), draft.Id, "1.0.0");
        var expectedPromotionJson = JsonSerializer.Serialize(new
        {
            draftId = draft.Id,
            assignmentMode = "exact",
            requestedVersion = "1.0.0"
        }, json);
        var promotionMarker = await db.Operations.SingleAsync(x => x.OperationKey == "parity-promote");
        Assert.Equal(GroundworkFingerprint("workflow.draft.promote.v1", expectedPromotionJson), promotionMarker.RequestFingerprint);
    }

    [Fact]
    public async Task Failed_stage_does_not_leak_tracked_rows_into_the_next_operation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var accessor = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, accessor);
        await Assert.ThrowsAsync<InvalidDataException>(() => writer.ExecuteAsync<object>(new DesignOperationKey("failed"), "test.op", new { Value = 1 }, ["test"], _ =>
        {
            db.Definitions.Add(new WorkflowDefinition { Id = "leaked", TenantId = "tenant-a", Name = "Should not persist" });
            throw new InvalidDataException("stage failed");
        }));
        Assert.Empty(db.ChangeTracker.Entries());
        await writer.ExecuteAsync(new DesignOperationKey("next"), "test.op", new { Value = 2 }, ["test"], _ => Task.FromResult(new { Id = "next" }));
        Assert.Null(await db.Definitions.SingleOrDefaultAsync(x => x.Id == "leaked"));
    }

    [Fact]
    public async Task Direct_atomic_interface_returns_conflict_and_rejects_empty_mutation_units()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("direct");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var conflict = await writer.ExecuteAsync(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(2)));
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        await Assert.ThrowsAsync<ArgumentException>(() => writer.ExecuteAsync(
            new DesignOperationKey("empty"), "test.op", new { Value = 1 }, [],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));
    }

    [Fact]
    public async Task Replay_and_conflict_are_resolved_before_before_attempt_work()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var key = new DesignOperationKey("preflight-order");
        await writer.ExecuteAsync(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)));
        var beforeAttemptCalls = 0;

        var replay = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });
        var conflict = await writer.ExecuteAsync<int>(key, "test.op", new { Value = 2 }, ["test"],
            (_, _) => throw new InvalidOperationException("stage must not run"),
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(DesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal(DesignAtomicWriteStatus.Conflict, conflict.Status);
        Assert.Equal(0, beforeAttemptCalls);
    }

    [Fact]
    public async Task Ef_rejects_authoritative_result_that_differs_from_staged_value()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access);
        var suppliedJson = JsonSerializer.Serialize(new ResultValue("supplied"));
        var invalid = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
            new DesignOperationKey("mismatch"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(
                new ResultValue("staged"), "sha256:invalid", suppliedJson))));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, invalid.FailureKind);
        Assert.Empty(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Ef_honors_custom_result_codec_for_authoritative_validation_and_replay()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        await using var db = Create(connection); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(new JsonStringEnumConverter());
        var value = new CustomResult(CustomResultStatus.Ready);
        var json = JsonSerializer.Serialize(value, options);
        var fingerprint = GroundworkFingerprint("test.op.result", json);
        var codec = new CustomResultCodec(options);

        var committed = await writer.ExecuteAsync(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<CustomResult>.Accepted(value, fingerprint, json)),
            resultCodec: codec);
        var replayed = await writer.ExecuteAsync<CustomResult>(
            new DesignOperationKey("custom-codec"), "test.op", new { Value = 1 }, ["test"],
            (_, _) => throw new InvalidOperationException("replay must not restage"),
            resultCodec: codec);

        Assert.Equal(DesignAtomicWriteStatus.Committed, committed.Status);
        Assert.Equal(DesignAtomicWriteStatus.Replayed, replayed.Status);
        Assert.Equal(value, committed.Value);
        Assert.Equal(value, replayed.Value);
    }

    [Fact]
    public async Task Ef_reconciles_a_commit_acknowledgement_failure_without_rerunning_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new AcknowledgementLostInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(250));
        var stageCalls = 0;
        interceptor.FailNextCommit = true;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("ack-lost"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<ResultValue>.Accepted(new ResultValue("durable")));
            });

        Assert.Equal(DesignAtomicWriteStatus.Reconciled, result.Status);
        Assert.Equal(1, stageCalls);
        Assert.Single(await db.Operations.ToListAsync());
    }

    [Fact]
    public async Task Ef_maps_reconciliation_timeout_after_provider_read_failures_to_unknown_outcome()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var state = new FailureState();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(new CommitAcknowledgementFailureInterceptor(state), new ReadFailureInterceptor(state))
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(100));
        state.FailNextCommit = true;

        var exception = await Assert.ThrowsAsync<DesignAtomicWriteUnknownOutcomeException>(() => writer.ExecuteAsync(
            new DesignOperationKey("ack-lost-read-failure"),
            "test.op",
            new { Value = 1 },
            ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1))));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
    }

    [Fact]
    public async Task Ef_preserves_caller_cancellation_during_commit_acknowledgement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        using var callerCancellation = new CancellationTokenSource();
        var interceptor = new CallerCancellationCommitInterceptor(callerCancellation);
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        interceptor.Arm();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var writer = new EfDesignAtomicWriter(db, access, reconciliationTimeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => writer.ExecuteAsync(
            new DesignOperationKey("caller-cancelled-commit"),
            "test.op",
            new { Value = 1 },
            ["test"],
            (_, _) => Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1)),
            cancellationToken: callerCancellation.Token));

        Assert.NotEqual(typeof(DesignAtomicWriteUnknownOutcomeException), exception.GetType());
        Assert.True(callerCancellation.IsCancellationRequested);
    }

    [Fact]
    public async Task Ef_permanent_delete_requires_publication_guard_invokes_all_guards_and_declares_cascade_units()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var inner = new EfDesignAtomicWriter(db, access);
        var atomic = new CapturingAtomicWriter(inner);
        var publication = new PermittingPublicationGuard();
        var other = new RecordingDeletionGuard();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition-delete", TenantId = "tenant-a", Name = "Delete", DeletedAt = DateTimeOffset.UnixEpoch });
        await db.SaveChangesAsync();

        await new EfDeleteWorkflowDefinitionPermanentlyCommand(db, access, atomic, [publication, other])
            .Execute(new DesignOperationKey("delete-all-units"), "definition-delete");

        Assert.Equal("definition-delete", publication.SeenDefinitionId);
        Assert.Equal("definition-delete", other.SeenDefinitionId);
        Assert.Contains(DesignPersistenceUnitNames.VersionLayouts, atomic.MutatedUnits);
        Assert.Equal(
            [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.VersionLayouts],
            atomic.MutatedUnits);
    }

    [Fact]
    public async Task Ef_permanent_delete_refuses_a_composition_without_publication_guard()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var db = Create(connection);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var atomic = new CapturingAtomicWriter(new EfDesignAtomicWriter(db, access));

        await Assert.ThrowsAsync<PermanentDeletionUnavailableException>(() =>
            new EfDeleteWorkflowDefinitionPermanentlyCommand(
                    db,
                    access,
                    atomic,
                    [new RecordingDeletionGuard()])
                .Execute(new DesignOperationKey("delete-without-publication-guard"), "missing"));

        Assert.Equal(
            [DesignPersistenceUnitNames.Definitions, DesignPersistenceUnitNames.Drafts, DesignPersistenceUnitNames.Versions, DesignPersistenceUnitNames.DraftLayouts, DesignPersistenceUnitNames.VersionLayouts],
            atomic.MutatedUnits);
    }

    [Fact]
    public async Task Ef_maps_only_unique_provider_failures_during_promotion_to_version_conflict()
    {
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var unique = new DbUpdateException("unique", new SqliteException("UNIQUE constraint failed", 19, 1555));
        var writer = new ThrowingAtomicWriter(new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.draft.promote.v1",
            null,
            unique.InnerException!));
        var command = new EfPromoteDraftToVersionCommand(
            null!, access, writer, new TestSerializer(), new TestIdentity(), null!, new TestLockProvider());

        var exception = await Assert.ThrowsAsync<WorkflowDefinitionVersionConflictException>(() => command.Execute(
            new DesignOperationKey("promotion-unique-race"), "draft-1", "1.0.0"));

        Assert.Equal("draft-1", exception.DefinitionId);
    }

    [Fact]
    public async Task Ef_does_not_map_unrelated_provider_failures_during_promotion_to_version_conflict()
    {
        var providerFailure = new DesignPersistenceException(
            DesignPersistenceDomain.Workflow,
            DesignPersistenceFailureKind.Provider,
            "workflow.draft.promote.v1",
            null,
            new InvalidOperationException("provider unavailable"));
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var command = new EfPromoteDraftToVersionCommand(
            null!, access, new ThrowingAtomicWriter(providerFailure), new TestSerializer(), new TestIdentity(), null!, new TestLockProvider());

        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => command.Execute(
            new DesignOperationKey("promotion-provider-failure"), "draft-1", "1.0.0"));

        Assert.Same(providerFailure, exception);
    }

    [Fact]
    public async Task Ef_retries_transient_writes_after_rerunning_attempt_setup()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options); await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        IDesignAtomicWriter writer = new EfDesignAtomicWriter(db, access);
        var beforeAttemptCalls = 0;
        var stageCalls = 0;
        interceptor.FailNextSave = true;

        var result = await writer.ExecuteAsync(
            new DesignOperationKey("transient-retry"), "test.op", new { Value = 1 }, ["test"],
            (_, _) =>
            {
                stageCalls++;
                return Task.FromResult(DesignAtomicWriteStage<int>.Accepted(1));
            },
            _ =>
            {
                beforeAttemptCalls++;
                return Task.CompletedTask;
            });

        Assert.Equal(DesignAtomicWriteStatus.Committed, result.Status);
        Assert.Equal(2, beforeAttemptCalls);
        Assert.Equal(2, stageCalls);
    }

    [Fact]
    public async Task Ef_clone_releases_the_previous_generated_draft_lock_before_retrying_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new TransientSaveInterceptor();
        var options = new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>()
            .UseSqlite(connection)
            .AddInterceptors(interceptor)
            .Options;
        await using var db = new WorkflowsDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccessor(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var serializer = new TestSerializer();
        db.Definitions.Add(new WorkflowDefinition { Id = "definition", TenantId = "tenant-a", Name = "Definition" });
        db.Versions.Add(new WorkflowDefinitionVersion("definition", "1.0.0")
        {
            Id = "source-version",
            TenantId = "tenant-a",
            State = State(),
            StateSource = serializer.Serialize(State())
        });
        await db.SaveChangesAsync();
        var locks = new DisposalRecordingLockProvider();
        interceptor.FailNextSave = true;
        var atomic = new EfDesignAtomicWriter(db, access);
        var clone = new EfCloneDraftFromVersionCommand(
            db,
            access,
            atomic,
            new TestIdentity(),
            serializer,
            locks);

        var draftId = await clone.Execute(new DesignOperationKey("clone-retry"), "source-version");

        Assert.Equal("generated-3", draftId);
        Assert.Equal(2, locks.AcquireCount);
        Assert.Equal(2, locks.DisposeCount);
    }

    [Fact]
    public async Task Shared_protocol_rolls_back_when_commit_is_rejected()
    {
        var scope = new ProtocolScope();
        var rollbackCount = 0;
        var lane = new DesignAtomicWriteLane<ProtocolScope, ProtocolMarker, ProtocolStage, ProtocolResult>
        {
            MarkerId = "marker",
            LoadMarker = _ => Task.FromResult<ProtocolMarker?>(null),
            BeginScope = () => scope,
            SaveMarker = (_, _, _) => Task.CompletedTask,
            Commit = (_, _) => Task.FromResult(DesignAtomicCommitDisposition.Rejected),
            Rollback = _ => rollbackCount++,
            ClassifyMarkerRace = _ => false,
            ClassifyUncertainCommit = _ => false,
            OnUncertainCommit = (_, _) => throw new InvalidOperationException(),
            TryReconcileAfterCommit = (_, _) => Task.FromResult<ProtocolResult?>(null),
            Delay = (_, _) => Task.CompletedTask,
            IsAccepted = stage => stage.Accepted,
            OnCommitted = _ => new ProtocolResult("committed"),
            OnReplay = _ => new ProtocolResult("replayed"),
            OnRejected = () => new ProtocolResult("rejected")
        };

        var result = await DesignAtomicWriteProtocol.ExecuteAsync(
            lane,
            (_, _) => Task.FromResult(new ProtocolStage(true)),
            null,
            CancellationToken.None);

        Assert.Equal("rejected", result.Status);
        Assert.Equal(1, rollbackCount);
        Assert.True(scope.Disposed);
    }


    private static WorkflowsDesignSqliteDbContext Create(SqliteConnection connection) => new(new DbContextOptionsBuilder<WorkflowsDesignSqliteDbContext>().UseSqlite(connection).Options);
    private static WorkflowDefinitionState State() => new([], null, [], [], null);
    private sealed class TestIdentity : IIdentityGenerator { private int n; public string Generate() => $"generated-{Interlocked.Increment(ref n)}"; }
    private sealed class CustomDesignAtomicWriter : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) => throw new NotSupportedException();
    }
    private sealed class ReplayedAtomicWriter : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) =>
            Task.FromResult(new DesignAtomicWriteResult<T>(DesignAtomicWriteStatus.Replayed, default));
    }
    private sealed class TestLockProvider : IDistributedLockProvider
    {
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle());
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => new Handle();
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(new Handle());
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class DisposalRecordingLockProvider : IDistributedLockProvider
    {
        public int AcquireCount { get; private set; }
        public int DisposeCount { get; private set; }

        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            new Handle(this);

        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
        {
            AcquireCount++;
            return ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle(this));
        }

        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            AcquireLock(name, timeout, cancellationToken);

        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IDistributedSynchronizationHandle?>(AcquireLock(name, timeout, cancellationToken));

        private sealed class Handle(DisposalRecordingLockProvider owner) : IDistributedSynchronizationHandle
        {
            private int disposed;
            public CancellationToken HandleLostToken => CancellationToken.None;
            public void Dispose()
            {
                if (Interlocked.Exchange(ref disposed, 1) == 0)
                    owner.DisposeCount++;
            }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
    private sealed class RecordingLockProvider : IDistributedLockProvider
    {
        public List<string> Acquired { get; } = [];
        public IDistributedSynchronizationHandle AcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) { Acquired.Add(name); return new Handle(); }
        public ValueTask<IDistributedSynchronizationHandle> AcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) { Acquired.Add(name); return ValueTask.FromResult<IDistributedSynchronizationHandle>(new Handle()); }
        public IDistributedSynchronizationHandle? TryAcquireLock(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => AcquireLock(name, timeout, cancellationToken);
        public ValueTask<IDistributedSynchronizationHandle?> TryAcquireLockAsync(string name, TimeSpan? timeout = null, CancellationToken cancellationToken = default) => ValueTask.FromResult<IDistributedSynchronizationHandle?>(AcquireLock(name, timeout, cancellationToken));
        private sealed class Handle : IDistributedSynchronizationHandle { public CancellationToken HandleLostToken => CancellationToken.None; public void Dispose() { } public ValueTask DisposeAsync() => ValueTask.CompletedTask; }
    }
    private sealed class EmptyActivityStructureService : IActivityStructureService
    {
        public IReadOnlyCollection<ActivityChildProjection> ProjectChildren(ActivityNode activity) => [];
        public ActivityNode ReplaceChildren(ActivityNode activity, IReadOnlyCollection<ActivityChildProjection> childProjections) => activity;
        public ActivityNodeStructure? CompileExecutableStructure(ActivityNode activity) => null;
        public IReadOnlyCollection<Elsa.Expressions.Core.Models.VariableDefinition> ProjectScopedVariables(ActivityNode activity) => [];
        public bool SupportsScopedVariables(ActivityNode activity) => false;
    }
    private sealed class TestAccessor(PersistenceAccessContext current) : IPersistenceAccessContextAccessor { public PersistenceAccessContext Current => current; }
    private sealed record ResultValue(string Value);
    private enum CustomResultStatus { Ready }
    private sealed record CustomResult(CustomResultStatus Status);
    private sealed class CustomResultCodec(JsonSerializerOptions options) : IDesignAtomicWriteResultCodec<CustomResult>
    {
        public CustomResult Deserialize(string json) => JsonSerializer.Deserialize<CustomResult>(json, options)!;
        public bool Equivalent(CustomResult left, CustomResult right) => left == right;
    }

    private static string GroundworkFingerprint(string operationKind, string json)
    {
        using var document = JsonDocument.Parse(json);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
            WriteCanonical(writer, document.RootElement);
        var canonical = Encoding.UTF8.GetString(stream.ToArray());
        var identity = "elsa-design-material:v1";
        var material = $"{Encoding.UTF8.GetByteCount(identity)}:{identity}{Encoding.UTF8.GetByteCount(operationKind)}:{operationKind}1:1{Encoding.UTF8.GetByteCount(canonical)}:{canonical}";
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(material)))}";
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
            {
                writer.WritePropertyName(property.Name);
                WriteCanonical(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else if (element.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in element.EnumerateArray())
                WriteCanonical(writer, item);
            writer.WriteEndArray();
        }
        else
            element.WriteTo(writer);
    }
    private sealed class ProtocolScope : IDisposable { public bool Disposed { get; private set; } public void Dispose() => Disposed = true; }
    private sealed class ProtocolMarker { }
    private sealed record ProtocolStage(bool Accepted);
    private sealed record ProtocolResult(string Status);
    private sealed class AcknowledgementLostInterceptor : DbTransactionInterceptor
    {
        public bool FailNextCommit { get; set; }

        public override void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
        {
            if (!FailNextCommit)
                return;
            FailNextCommit = false;
            throw new InvalidOperationException("commit acknowledgement lost");
        }

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!FailNextCommit)
                return Task.CompletedTask;
            FailNextCommit = false;
            return Task.FromException(new InvalidOperationException("commit acknowledgement lost"));
        }
    }

    private sealed class TransientSaveInterceptor : SaveChangesInterceptor
    {
        private int failNextSave;
        public bool FailNextSave { set => Interlocked.Exchange(ref failNextSave, value ? 1 : 0); }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref failNextSave, 0) == 1)
                return ValueTask.FromException<InterceptionResult<int>>(
                    new DbUpdateException("simulated transient write conflict", new SqliteException("database is locked", 5, 5)));
            return ValueTask.FromResult(result);
        }
    }
    private sealed class FailureState
    {
        public bool FailNextCommit { get; set; }
        public bool FailReads { get; set; }
    }
    private sealed class CommitAcknowledgementFailureInterceptor(FailureState state) : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (!state.FailNextCommit)
                return Task.CompletedTask;
            state.FailNextCommit = false;
            state.FailReads = true;
            return Task.FromException(new InvalidOperationException("commit acknowledgement lost"));
        }
    }
    private sealed class CallerCancellationCommitInterceptor(CancellationTokenSource cancellation) : DbTransactionInterceptor
    {
        private int armed;

        public void Arm() => Interlocked.Exchange(ref armed, 1);

        public override Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Exchange(ref armed, 0) == 0)
                return Task.CompletedTask;
            cancellation.Cancel();
            return Task.FromCanceled(cancellation.Token);
        }
    }
    private sealed class ReadFailureInterceptor(FailureState state) : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) => state.FailReads
            ? ValueTask.FromException<InterceptionResult<DbDataReader>>(new InvalidOperationException("provider read unavailable"))
            : ValueTask.FromResult(result);
    }
    private sealed class CapturingAtomicWriter(IDesignAtomicWriter inner) : IDesignAtomicWriter
    {
        public IReadOnlyCollection<string> MutatedUnits { get; private set; } = [];
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null)
        {
            MutatedUnits = mutatedUnits.ToArray();
            return inner.ExecuteAsync(operationKey, operationKind, requestMaterial, mutatedUnits, stage, beforeAttempt, cancellationToken, resultCodec);
        }
    }
    private sealed class ThrowingAtomicWriter(Exception exception) : IDesignAtomicWriter
    {
        public Task<DesignAtomicWriteResult<T>> ExecuteAsync<T>(DesignOperationKey operationKey, string operationKind, object requestMaterial, IReadOnlyCollection<string> mutatedUnits, Func<IDesignAtomicWriteContext, CancellationToken, Task<DesignAtomicWriteStage<T>>> stage, Func<CancellationToken, Task>? beforeAttempt = null, CancellationToken cancellationToken = default, IDesignAtomicWriteResultCodec<T>? resultCodec = null) => Task.FromException<DesignAtomicWriteResult<T>>(exception);
    }
    private sealed class PermittingPublicationGuard : IWorkflowDefinitionPublicationDeletionGuard
    {
        public string? SeenDefinitionId { get; private set; }
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) { SeenDefinitionId = definitionId; return Task.CompletedTask; }
    }
    private sealed class RecordingDeletionGuard : IWorkflowDefinitionPermanentDeletionGuard
    {
        public string? SeenDefinitionId { get; private set; }
        public Task EnsureCanDeleteAsync(string definitionId, CancellationToken cancellationToken = default) { SeenDefinitionId = definitionId; return Task.CompletedTask; }
    }
    private sealed class CapturingDeferredEventPublisher : IDeferredEventPublisher
    {
        public List<IEvent> Events { get; } = [];
        public Task Publish(IEvent @event, CancellationToken cancellationToken = default)
        {
            Events.Add(@event);
            return Task.CompletedTask;
        }
    }
    private sealed class TestSerializer(JsonSerializerOptions? serializerOptions = null) : IPayloadSerializer
    {
        private readonly JsonSerializerOptions options = serializerOptions ?? new();
        public string Serialize(object payload) => JsonSerializer.Serialize(payload, options);
        public JsonElement SerializeToElement(object payload) => JsonSerializer.SerializeToElement(payload, options);
        public object Deserialize(string serializedData) => JsonSerializer.Deserialize<JsonElement>(serializedData);
        public object Deserialize(string serializedData, Type type) => JsonSerializer.Deserialize(serializedData, type)!;
        public object Deserialize(JsonElement serializedData) => serializedData;
        public T Deserialize<T>(string serializedData) => JsonSerializer.Deserialize<T>(serializedData)!;
        public T Deserialize<T>(JsonElement serializedData) => serializedData.Deserialize<T>(options)!;
        public JsonSerializerOptions GetOptions() => options;
    }
}
