using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Core.Contracts;
using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Core.Services;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Services;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Design.Persistence.Core.Models;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Design.Persistence.Core.Contracts;
using Elsa.Activities.Design.Persistence.Core.Stores;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Primitives.Contracts;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignEntityFrameworkCoreTests
{
    [Fact]
    public async Task Sqlite_version_lists_use_definition_then_semver_ordinal_order_and_empty_snapshots_use_epoch()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.AddRange(Definition("definition-a", "tenant-a"), Definition("definition-b", "tenant-a"));
        db.ActivityDefinitionVersions.AddRange(
            Version("definition-b", "b-1", "tenant-a", "1.0.0"),
            Version("definition-a", "a-2", "tenant-a", "2.0.0"),
            Version("definition-a", "a-1", "tenant-a", "1.0.0"));
        await db.SaveChangesAsync();

        var store = (IActivityDefinitionVersionStore)new EfActivityDesignStores(db);
        Assert.Equal(["a-1", "a-2", "b-1"], (await store.ListAsync()).Select(x => x.Id));
        Assert.Equal(["a-1", "a-2", "b-1"], (await store.ListByDefinitionIdsAsync(["definition-a", "definition-b"])).Select(x => x.Id));
        Assert.Equal(["a-1", "a-2"], (await store.ListByDefinitionAsync("definition-a")).Select(x => x.Id));
        var snapshot = await ((IActivityDefinitionManagementProjectionStore)new EfActivityDesignStores(db)).GetCurrentSnapshotAsync();
        Assert.Equal(DateTimeOffset.UnixEpoch, snapshot.AsOf);
    }

    [Fact]
    public async Task Sqlite_model_contains_all_registered_units_and_roundtrips_definition()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        Assert.Equal(21, db.Model.GetEntityTypes().Count(type => !type.IsOwned() && !type.ClrType.IsAbstract));
        Assert.False(db.Model.FindEntityType(typeof(ActivityDefinition))!.FindProperty("TenantKey")!.IsNullable);
        var receiptType = db.Model.FindEntityType(typeof(ActivityForkReceipt))!;
        Assert.Null(receiptType.FindNavigation(nameof(ActivityForkReceipt.Definition)));
        Assert.Null(receiptType.FindProperty(nameof(ActivityForkReceipt.Definition)));
        Assert.False(receiptType.FindProperty(nameof(ActivityForkReceipt.TenantScopeKey))!.IsNullable);
        Assert.Contains(receiptType.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([nameof(ActivityForkReceipt.TenantScopeKey), nameof(ActivityForkReceipt.ActorIdentityHash), nameof(ActivityForkReceipt.IdempotencyIdentityHash)]));
        Assert.Contains(db.Model.FindEntityType(typeof(ActivityDefinitionAuthoringState))!.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual(["TenantScopeKey", "DefinitionIdIdentityHash"]));
        Assert.Contains(db.Model.FindEntityType(typeof(ActivityForkCandidate))!.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([nameof(ActivityForkCandidate.TenantScopeKey), nameof(ActivityForkCandidate.ActorIdentityHash), nameof(ActivityForkCandidate.CandidateIdIdentityHash)]));
        Assert.Contains(db.Model.FindEntityType(typeof(ActivityDesignOperationRecord))!.GetIndexes(), index => index.IsUnique && index.Properties.Select(x => x.Name).SequenceEqual([nameof(ActivityDesignOperationRecord.TenantScopeKey), nameof(ActivityDesignOperationRecord.OperationKindIdentityHash), nameof(ActivityDesignOperationRecord.OperationKeyIdentityHash)]));
        foreach (var index in db.Model.GetEntityTypes().SelectMany(x => x.GetIndexes()).Where(x => x.IsUnique))
            Assert.DoesNotContain(index.Properties, property => property.IsNullable);
        Assert.DoesNotContain(receiptType.GetForeignKeys(), foreignKey => foreignKey.PrincipalEntityType.ClrType == typeof(ActivityDefinition));
        await db.Database.EnsureCreatedAsync();
        var definition = new Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinition { Id = "d1", TenantId = "tenant-a", ActivityTypeKey = "sample", Category = "Tests" };
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionVersions.Add(new Elsa.Activities.Design.Persistence.Core.Entities.ActivityDefinitionVersion("1.0.0", "d1")
        {
            Id = "v1", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
            ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "test", SourceId = "source"
        });
        db.ActivityDefinitionAuthoringStates.AddRange(
            new ActivityDefinitionAuthoringState { Id = "authoring-global", DefinitionId = "d1", TenantId = null, ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) },
            new ActivityDefinitionAuthoringState { Id = "authoring-literal-global", DefinitionId = "d1", TenantId = "<global>", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        await db.SaveChangesAsync();
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "authoring-global-duplicate", DefinitionId = "d1", TenantId = null, ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        await using var read = new ActivitiesDesignSqliteDbContext(options);
        var store = new EfActivityDesignStores(read);
        Assert.Equal("tenant-a", (await store.GetAsync("d1")).TenantId);
        Assert.Equal("provider", (await ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore)store).GetAsync("v1")).ProviderKey);
    }

    [Fact]
    public async Task Sqlite_global_definition_key_is_unique_across_context_race()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var first = new ActivitiesDesignSqliteDbContext(options);
        await first.Database.EnsureCreatedAsync();
        await using var second = new ActivitiesDesignSqliteDbContext(options);
        var firstDefinition = new ActivityDefinition { Id = "global-1", TenantId = null, ActivityTypeKey = "Acme.Global", Category = "Tests" };
        var secondDefinition = new ActivityDefinition { Id = "global-2", TenantId = null, ActivityTypeKey = "Acme.Global", Category = "Tests" };
        first.ActivityDefinitions.Add(firstDefinition);
        second.ActivityDefinitions.Add(secondDefinition);

        await first.SaveChangesAsync();
        Assert.Equal(ActivitiesDesignDbContext.GlobalTenantKey, first.Entry(firstDefinition).Property<string>("TenantKey").CurrentValue);
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
        Assert.Single(await first.ActivityDefinitions.ToListAsync());

        var opaqueTenantDefinition = new ActivityDefinition { Id = "opaque-tenant", TenantId = ActivitiesDesignDbContext.GlobalTenantKey, ActivityTypeKey = "Acme.Global", Category = "Tests" };
        first.ActivityDefinitions.Add(opaqueTenantDefinition);
        await first.SaveChangesAsync();
        var opaqueTenantKey = first.Entry(opaqueTenantDefinition).Property<string>("TenantKey").CurrentValue;
        Assert.StartsWith("t:", opaqueTenantKey);
        Assert.NotEqual(ActivitiesDesignDbContext.GlobalTenantKey, opaqueTenantKey);

        var longOpaqueTenantDefinition = new ActivityDefinition { Id = "long-opaque-tenant", TenantId = new string('x', 256), ActivityTypeKey = "Acme.Global", Category = "Tests" };
        first.ActivityDefinitions.Add(longOpaqueTenantDefinition);
        await first.SaveChangesAsync();
        Assert.Equal(66, first.Entry(longOpaqueTenantDefinition).Property<string>("TenantKey").CurrentValue.Length);
    }

    [Fact]
    public async Task Sqlite_tenant_agnostic_definition_queries_require_privileged_across_scope_access()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.AddRange(Definition("tenant-a", "tenant-a"), Definition("tenant-b", "tenant-b"));
        await db.SaveChangesAsync();

        var filter = new Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter { TenantAgnostic = true };
        var scoped = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.ListAsync(filter));
        var global = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("read"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => global.FindAsync(filter));
        var across = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("read"))));
        Assert.Equal(["tenant-a", "tenant-b"], (await across.ListAsync(filter)).Select(x => x.TenantId));
    }

    [Fact]
    public async Task Sqlite_availability_settings_scope_is_a_logical_key_not_a_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await store.SaveAsync(new ActivityAvailabilitySettings
        {
            Scope = ActivityAvailabilitySettings.HostDefaultScope,
            Mode = ActivityAvailabilityManagementMode.Only
        });

        var loaded = await store.LoadAsync(ActivityAvailabilitySettings.HostDefaultScope);
        Assert.Equal(ActivityAvailabilitySettings.HostDefaultScope, loaded!.Scope);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, loaded.Mode);
    }

    [Fact]
    public async Task Sqlite_dependency_projection_replacement_requires_privileged_across_scope_access()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "owner", "root", "1.0.0", TenantId: "tenant-a");
        var dependency = new ActivityDefinitionReference("ActivityVersion", "dependency", "dep", "1.0.0", TenantId: "tenant-b");
        var initial = new ActivityDependencyProjectionRebuild("initial", 1, DateTimeOffset.UnixEpoch,
            [new("initial-edge", owner, dependency, new("occurrence", []), true, 1, [])]);
        await ProjectionStore(db).RebuildAsync(initial);
        var scoped = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.RebuildCurrentAsync(
            "tenant-replacement", DateTimeOffset.UtcNow, []));
        var state = await db.ActivityDependencyProjections.SingleAsync();
        Assert.Equal("initial", state.RebuildId);
        Assert.Equal("initial-edge", Assert.Single(state.Items).RelationshipId);
    }

    [Fact]
    public void Sqlite_ef_hasher_matches_the_shared_activity_definition_hasher()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        using var db = new ActivitiesDesignSqliteDbContext(options);
        var definition = new ActivityDefinition { Id = "definition", TenantId = "tenant-a", ActivityTypeKey = "Acme.Test", Category = "Tests", DisplayName = "Test" };
        var version = new ActivityDefinitionVersion("1.0.0", definition.Id)
        {
            Id = "version", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
            ConsumerKey = "consumer", ConsumerSchemaVersion = "1", DescriptorPayload = JsonSerializer.SerializeToElement(new { mode = "opaque" }),
            Inputs = [new InputDefinition("input", "Input", new Elsa.Primitives.Models.TypeReference("string"), null, "Input", null, true)]
        };
        var shared = new DefaultActivityDefinitionHasher();
        var ef = new EfActivityDesignStores(db);

        var original = shared.Hash(definition, version);
        Assert.Equal(original, ef.Hash(definition, version));
        version.ProviderKey = "provider-changed";
        Assert.NotEqual(original, ef.Hash(definition, version));
    }

    [Fact]
    public async Task Sqlite_recommended_picker_includes_global_and_requested_tenant_only()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(commands).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        foreach (var (tenant, suffix) in new[] { ((string?)null, "global"), ((string?)"tenant-a", "tenant-a"), ((string?)"tenant-b", "tenant-b") })
        {
            db.ActivityDefinitions.Add(new ActivityDefinition { Id = $"definition-{suffix}", TenantId = tenant, ActivityTypeKey = $"Acme.{suffix}", Category = "Tests" });
            db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = $"authoring-{suffix}", TenantId = tenant, DefinitionId = $"definition-{suffix}", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), RecommendedVersionId = $"version-{suffix}" });
            var publication = Publication($"publication-{suffix}", $"version-{suffix}", $"definition-{suffix}", "1.0.0");
            publication.TenantId = tenant;
            db.ActivityDefinitionVersionPublications.Add(publication);
        }
        await db.SaveChangesAsync();
        commands.Sql.Clear();
        var picker = (IRecommendedActivityDefinitionPickerStore)new EfActivityDesignStores(db);

        var tenantItems = await picker.ReadAsync("tenant-a", 0, 10);
        Assert.InRange(commands.Sql.Count, 1, 4);
        var globalItems = await picker.ReadAsync(null, 0, 10);
        Assert.Equal(["definition-global", "definition-tenant-a"], tenantItems.Items.Select(x => x.Definition.Id));
        Assert.Equal(["definition-global"], globalItems.Items.Select(x => x.Definition.Id));
    }

    [Fact]
    public async Task Sqlite_json_value_converter_normalizes_malformed_stored_json()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionDrafts.Add(Draft("draft", "definition", "tenant-a", 1));
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_definition_drafts SET State = @p0 WHERE Id = 'draft'", "{");
        db.ChangeTracker.Clear();

        var store = (IActivityDefinitionDraftStore)new EfActivityDesignStores(db);
        var exception = await Assert.ThrowsAsync<DesignPersistenceException>(() => store.FindAsync("draft"));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, exception.FailureKind);
        Assert.Equal("json-convert", exception.Operation);
    }

    [Fact]
    public async Task Sqlite_list_operations_page_beyond_the_legacy_ten_thousand_cap()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.AddRange(Enumerable.Range(0, 10_001).Select(index => Definition($"definition-{index:D5}", "tenant-a")));
        await db.SaveChangesAsync();
        db.ActivityDefinitionVersions.AddRange(Enumerable.Range(0, 501).Select(index => new ActivityDefinitionVersion($"1.0.{index}", "definition-00000")
        {
            Id = $"version-{index:D4}", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
            ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "test", SourceId = $"source-{index:D4}"
        }));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db);
        var definitions = await store.ListAsync(new Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter());
        var versions = await ((IActivityDefinitionVersionStore)store).ListByDefinitionAsync("definition-00000");

        Assert.Equal(10_001, definitions.Count);
        Assert.Equal(501, versions.Count);
    }

    [Fact]
    public async Task Sqlite_version_roundtrip_preserves_opaque_descriptor_and_design_contracts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using (var db = new ActivitiesDesignSqliteDbContext(options))
        {
            await db.Database.EnsureCreatedAsync();
            db.ActivityDefinitions.Add(new ActivityDefinition { Id = "d1", TenantId = "tenant-a", ActivityTypeKey = "Acme.Test", Category = "Tests" });
            db.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion("1.2.3", "d1")
            {
                Id = "v1", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
                ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "json", SourceId = "source",
                DescriptorPayloadSource = "{\"mode\":\"opaque\"}",
                InputsSource = JsonSerializer.Serialize(new[] { new InputDefinition("input", "Input", new Elsa.Primitives.Models.TypeReference("string"), null, "Input", null, true) }),
                OutputsSource = "[]", DesignFacetsSource = JsonSerializer.Serialize(new[] { new ActivityDesignFacet("canvas", "1", JsonDocument.Parse("{\"color\":\"blue\"}").RootElement) })
            });
            await db.SaveChangesAsync();
        }
        await using var read = new ActivitiesDesignSqliteDbContext(options);
        var store = new EfActivityDesignStores(read);
        var version = await ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityDefinitionVersionStore)store).GetAsync("v1");
        Assert.Equal("opaque", version.DescriptorPayload.GetProperty("mode").GetString());
        Assert.Equal("input", Assert.Single(version.Inputs).ReferenceKey);
        Assert.Equal("canvas", Assert.Single(version.DesignFacets).Kind);
    }

    [Fact]
    public async Task Sqlite_atomic_writer_commits_replays_and_rejects_conflicting_operation_material()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWrite(db);
        var request = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.test", "op-1"), "request-a", ["definitions"]);
        var stages = 0;
        var first = await writer.ExecuteAsync(request, async (context, token) =>
        {
            stages++;
            context.Db.ActivityDefinitions.Add(new ActivityDefinition { Id = "d1", TenantId = "tenant-a", ActivityTypeKey = "Acme.Test", Category = "Tests" });
            return EfDesignAtomicWriteStageResult.Accepted("result-a", "{\"ok\":true}");
        });
        var replay = await writer.ExecuteAsync(request, (_, _) => throw new InvalidOperationException("stage must not replay"));
        Assert.Equal(EfDesignAtomicWriteStatus.Committed, first.Status);
        Assert.Equal(EfDesignAtomicWriteStatus.Replayed, replay.Status);
        Assert.Equal(1, stages);
        Assert.Equal(1, await db.ActivityDefinitions.CountAsync());
        var conflict = await writer.ExecuteAsync(new EfDesignAtomicWriteRequest(request.Operation, "request-b", request.MutatedUnits), (_, _) => Task.FromResult(EfDesignAtomicWriteStageResult.Accepted("result-b", "{}")));
        Assert.Equal(EfDesignAtomicWriteStatus.Conflict, conflict.Status);
    }

    [Fact]
    public async Task Sqlite_atomic_writer_does_not_classify_an_unrelated_unique_failure_as_marker_race()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.Add(Definition("existing", "tenant-a", "duplicate"));
        await db.SaveChangesAsync();
        var writer = new EfDesignAtomicWrite(db);

        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => writer.ExecuteAsync(
            new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("unique", "unrelated"), "fingerprint", ["activityDefinition"], "tenant-a"),
            (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(new ActivityDefinition { Id = "conflicting", TenantId = "tenant-a", ActivityTypeKey = "Acme.existing", Category = "Tests" });
                return Task.FromResult(EfDesignAtomicWriteStageResult.Accepted("result", "{}"));
            }));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.ActivityDesignOperations.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_concurrent_contexts_reject_a_stale_mutation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var seed = new ActivitiesDesignSqliteDbContext(options);
        await seed.Database.EnsureCreatedAsync();
        seed.ActivityDefinitions.Add(new ActivityDefinition { Id = "d1", TenantId = "tenant-a", ActivityTypeKey = "Acme.Test", Category = "Tests" });
        await seed.SaveChangesAsync();
        await using var first = new ActivitiesDesignSqliteDbContext(options);
        await using var second = new ActivitiesDesignSqliteDbContext(options);
        var left = await first.ActivityDefinitions.SingleAsync();
        var right = await second.ActivityDefinitions.SingleAsync();
        left.Category = "Left";
        right.Category = "Right";
        await first.SaveChangesAsync();
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => second.SaveChangesAsync());
    }

    [Fact]
    public async Task Sqlite_draft_create_rejects_an_authoring_update_between_read_and_fence()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-draft-create-fence-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder { DataSource = databasePath, Mode = SqliteOpenMode.ReadWriteCreate, Cache = SqliteCacheMode.Shared }.ToString();
            await using var keeper = new SqliteConnection(connectionString);
            await keeper.OpenAsync();
            await using var journal = keeper.CreateCommand();
            journal.CommandText = "PRAGMA journal_mode=WAL";
            await journal.ExecuteNonQueryAsync();
            var barrier = new TransactionStartBarrier();
            var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connectionString).AddInterceptors(barrier).Options;
            await using var seed = new ActivitiesDesignSqliteDbContext(options);
            await seed.Database.EnsureCreatedAsync();
            seed.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
            {
                Id = "authoring-d1", DefinitionId = "d1", TenantId = "tenant-a",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
            });
            await seed.SaveChangesAsync();
            await using var first = new ActivitiesDesignSqliteDbContext(options);
            await using var second = new ActivitiesDesignSqliteDbContext(options);
            barrier.BeforeContinue = async () =>
            {
                barrier.Enabled = false;
                var authoring = await second.ActivityDefinitionAuthoringStates.SingleAsync();
                authoring.HeadVersionId = "published-v1";
                await second.SaveChangesAsync();
            };
            barrier.Enabled = true;
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfActivityDesignStores(first).ExecuteAsync(new CreateActivityDraftRequest(Draft("draft-1", "d1", "tenant-a", 1), Layout("draft-1", "tenant-a", 1), null)));

            Assert.Empty(await first.ActivityDefinitionDrafts.ToListAsync());
        }
        finally
        {
            foreach (var path in new[] { databasePath, $"{databasePath}-wal", $"{databasePath}-shm" })
                if (File.Exists(path))
                    File.Delete(path);
        }
    }

    [Fact]
    public async Task Sqlite_conflict_copy_rolls_back_when_the_source_fence_is_stale()
    {
        const string connectionString = "Data Source=file:conflict-copy-fence;Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var interceptor = new SuppressConcurrencyFenceInterceptor("elsa_activity_definition_drafts");
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connectionString).AddInterceptors(interceptor).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = "authoring-d1", DefinitionId = "d1", TenantId = "tenant-a",
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        });
        db.ActivityDefinitionDrafts.Add(Draft("source", "d1", "tenant-a", 4, "v1"));
        await db.SaveChangesAsync();
        interceptor.Enabled = true;
        var store = new EfActivityDesignStores(db);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ExecuteAsync(new CreateActivityDraftConflictCopyRequest("source", 4, Draft("copy", "d1", "tenant-a", 4, "v1"), Layout("copy", "tenant-a", 4))));

        Assert.Null(await db.ActivityDefinitionDrafts.SingleOrDefaultAsync(x => x.Id == "copy"));
        Assert.Equal(4, await db.ActivityDefinitionDrafts.Where(x => x.Id == "source").Select(x => x.Revision).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_management_projection_advances_checkpoint_and_honors_snapshot_filters()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = new ActivityDefinition { Id = "d1", TenantId = "tenant-a", ActivityTypeKey = "Acme.Test", Category = "Tests", DisplayName = "Test" };
        var authoring = new ActivityDefinitionAuthoringState { Id = "d1", TenantId = "tenant-a", DefinitionId = "d1", ContentAuthority = new ActivityContentAuthority(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) };
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);
        var sequence = await writer.WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [new EfActivityManagementDefinitionChange(definition, authoring)], [], []));
        Assert.Equal(1, sequence);
        var store = new EfActivityDesignStores(db);
        var page = await store.ReadDefinitionsAsync(new Elsa.Activities.Design.Persistence.Core.Stores.ActivityManagementProjectionPageQuery("tenant-a", sequence, 0, 10));
        Assert.Equal(sequence, page.Snapshot.Sequence);
        Assert.Equal("d1", Assert.Single(page.Items).DefinitionId);
        await Assert.ThrowsAsync<Elsa.Activities.Design.Persistence.Core.Stores.ActivityManagementSnapshotExpiredException>(() => store.ReadDefinitionsAsync(new("tenant-a", sequence + 1, 0, 10)));
    }

    [Fact]
    public async Task Sqlite_scoped_access_does_not_read_another_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.AddRange(
            new ActivityDefinition { Id = "a", TenantId = "tenant-a", ActivityTypeKey = "a", Category = "Tests" },
            new ActivityDefinition { Id = "b", TenantId = "tenant-b", ActivityTypeKey = "b", Category = "Tests" });
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal("a", (await store.GetAsync("a")).Id);
        await Assert.ThrowsAsync<Elsa.Primitives.Exceptions.EntityNotFoundException>(() => store.GetAsync("b"));
        Assert.Equal("a", Assert.Single(await store.ListAsync(new Elsa.Activities.Design.Persistence.Core.Filters.ActivityDefinitionFilter())).Id);
    }

    [Fact]
    public async Task Sqlite_operation_key_commands_replay_and_rollback_authoritatively()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")));
        var atomic = new EfDesignAtomicWrite(db, access);
        var store = new EfActivityDesignStores(db, access, atomic);
        var first = await store.Execute(new DesignOperationKey("same"), Definition("d1", "tenant-a"), Version("d1", "v1", "tenant-a"));
        var replay = await store.Execute(new DesignOperationKey("same"), Definition("d1", "tenant-a"), Version("d1", "v1", "tenant-a"));
        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<DesignPersistenceOperationConflictException>(() => store.Execute(new DesignOperationKey("same"), Definition("d1", "tenant-a", "changed"), Version("d1", "v1", "tenant-a")));
        await Assert.ThrowsAsync<InvalidOperationException>(() => atomic.ExecuteAsync(
            new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("rollback", "one"), "fingerprint", ["activityDefinition"], "tenant-a"),
            (context, _) => { context.Db.ActivityDefinitions.Add(Definition("rolled-back", "tenant-a")); throw new InvalidOperationException("stage failed"); }));
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "rolled-back"));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Sqlite_atomic_writer_preserves_exact_operation_kind_and_key_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWrite(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        static EfDesignAtomicWriteStageResult Stage(string result) => EfDesignAtomicWriteStageResult.Accepted(result, $"{{\"result\":\"{result}\"}}");
        var exactKey = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.test", "exact-key"), "request-exact", ["unit"], "tenant-a");
        var trailingKey = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.test", "exact-key "), "request-key", ["unit"], "tenant-a");
        var trailingKind = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.test ", "exact-key"), "request-kind", ["unit"], "tenant-a");

        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await writer.ExecuteAsync(exactKey, (_, _) => Task.FromResult(Stage("exact")))).Status);
        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await writer.ExecuteAsync(trailingKey, (_, _) => Task.FromResult(Stage("key")))).Status);
        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await writer.ExecuteAsync(trailingKind, (_, _) => Task.FromResult(Stage("kind")))).Status);
        Assert.Equal(3, await db.ActivityDesignOperations.CountAsync());

        var replay = await writer.ExecuteAsync(trailingKey, (_, _) => throw new InvalidOperationException("stage must not replay"));
        Assert.Equal(EfDesignAtomicWriteStatus.Replayed, replay.Status);
        var conflict = await writer.ExecuteAsync(new EfDesignAtomicWriteRequest(trailingKey.Operation, "different", trailingKey.MutatedUnits, "tenant-a"), (_, _) => throw new InvalidOperationException("stage must not run for a conflict"));
        Assert.Equal(EfDesignAtomicWriteStatus.Conflict, conflict.Status);

        var unscopedWriter = new EfDesignAtomicWrite(db);
        var global = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.global", "same-key"), "global", ["unit"], null);
        var literalGlobal = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.global", "same-key"), "literal-global", ["unit"], "<global>");
        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await unscopedWriter.ExecuteAsync(global, (_, _) => Task.FromResult(Stage("global")))).Status);
        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await unscopedWriter.ExecuteAsync(literalGlobal, (_, _) => Task.FromResult(Stage("literal-global")))).Status);
        var acrossScopesWriter = new EfDesignAtomicWrite(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("atomic-across-scopes"))));
        var acrossScopes = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.global", "across-scopes"), "across-scopes", ["unit"], "tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopesWriter.ExecuteAsync(acrossScopes, (_, _) => Task.FromResult(Stage("across-scopes"))));
        Assert.Equal(5, await db.ActivityDesignOperations.CountAsync());

        var mismatchedTenant = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.global", "mismatched-tenant"), "mismatched-tenant", ["unit"], "tenant-a");
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopesWriter.ExecuteAsync(mismatchedTenant, (context, _) =>
        {
            context.Db.ActivityDefinitions.Add(Definition("cross-tenant", "tenant-b"));
            return Task.FromResult(Stage("must-not-commit"));
        }));
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "cross-tenant"));
        Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == "mismatched-tenant"));

        var globalAcrossScopes = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.global", "global-mismatched-tenant"), "global-mismatched-tenant", ["unit"]);
        var globalMismatch = await Assert.ThrowsAsync<DesignPersistenceException>(() => acrossScopesWriter.ExecuteAsync(globalAcrossScopes, (context, _) =>
        {
            context.Db.ActivityDefinitions.Add(Definition("global-cross-tenant", "tenant-b"));
            return Task.FromResult(Stage("must-not-commit"));
        }));
        Assert.Equal(DesignPersistenceFailureKind.Serialization, globalMismatch.FailureKind);
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "global-cross-tenant"));
        Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == "global-mismatched-tenant"));
    }

    [Fact]
    public async Task Sqlite_across_scope_atomic_writer_rejects_tenant_bound_request_before_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var writer = new EfDesignAtomicWrite(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("atomic-tenant-bound"))));
        var invoked = false;
        var request = new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("activity.atomic", "tenant-bound"), "tenant-bound", ["activityDefinition"], "tenant-a");

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.ExecuteAsync(request, (context, _) =>
        {
            invoked = true;
            context.Db.ActivityDefinitions.Add(Definition("must-not-stage", "tenant-a"));
            return Task.FromResult(EfDesignAtomicWriteStageResult.Accepted("must-not-commit", "{}"));
        }));

        Assert.False(invoked);
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "must-not-stage"));
        Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == request.Operation.OperationKey));
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task Sqlite_explicit_global_atomic_write_rejects_tenant_owned_stage_and_allows_global_stage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();

        static EfDesignAtomicWriteStageResult Accepted() => EfDesignAtomicWriteStageResult.Accepted("result", "{}");
        var global = new EfDesignAtomicWrite(db, new TestAccess(PersistenceAccessContext.Global));
        var globalTenantFailure = await Assert.ThrowsAsync<DesignPersistenceException>(() => global.ExecuteAsync(
            new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("global", "tenant-owned"), "request", ["activityDefinition"]),
            (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(Definition("tenant-owned", "tenant-a"));
                return Task.FromResult(Accepted());
            }));
        Assert.IsType<InvalidDataException>(globalTenantFailure.InnerException);
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "tenant-owned"));
        Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == "tenant-owned"));

        Assert.Equal(EfDesignAtomicWriteStatus.Committed, (await global.ExecuteAsync(
            new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("global", "global-owned"), "request", ["activityDefinition"]),
            (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(Definition("global-owned", null));
                return Task.FromResult(Accepted());
            })).Status);
        var privilegedGlobal = new EfDesignAtomicWrite(db, new TestAccess(PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("global-write"))));
        var privilegedGlobalTenantFailure = await Assert.ThrowsAsync<DesignPersistenceException>(() => privilegedGlobal.ExecuteAsync(
            new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("global", "privileged-tenant-owned"), "request", ["activityDefinition"]),
            (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(Definition("privileged-tenant-owned", "tenant-a"));
                return Task.FromResult(Accepted());
            }));
        Assert.IsType<InvalidDataException>(privilegedGlobalTenantFailure.InnerException);
        Assert.False(await db.ActivityDefinitions.AnyAsync(x => x.Id == "privileged-tenant-owned"));
        Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == "privileged-tenant-owned"));
    }

    [Fact]
    public async Task Sqlite_privileged_across_scope_rejects_ordinary_tenant_mutations_but_allows_projection_rebuilds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var access = new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("across-scope-admission")));

        var store = new EfActivityDesignStores(db, access);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(new UpdateActivityDefinitionPresentationRequest(
            "ordinary-definition", "tenant-a", "Tests", "Display", null, DateTimeOffset.UtcNow)));

        var definition = Definition("projection-definition", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = definition.Id, DefinitionId = definition.Id, TenantId = definition.TenantId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        };
        var writer = new EfActivityManagementProjectionWriter(db, access);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new(
            DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
        Assert.Empty(await db.ActivityManagementProjectionWatermarks.ToListAsync());

        var projectionStore = ProjectionStore(db);
        await projectionStore.RebuildAsync(new ActivityDependencyProjectionRebuild(
            "across-scope-rebuild", 1, DateTimeOffset.UtcNow,
            [new(
                "tenant-edge",
                new ActivityDefinitionReference("ActivityVersion", "owner", "owner", "1.0.0", "tenant-a"),
                new ActivityDefinitionReference("ActivityVersion", "dependency", "dependency", "1.0.0", "tenant-b"),
                new("occurrence", []), true, 1, [])]));
        Assert.Equal("across-scope-rebuild", (await db.ActivityDependencyProjections.SingleAsync()).RebuildId);
    }

    [Fact]
    public async Task Sqlite_atomic_writer_rejects_deleted_cross_scope_tenant_stage_and_rolls_back()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.Add(Definition("delete-protected", "tenant-a"));
        await db.SaveChangesAsync();

        static EfDesignAtomicWriteStageResult Accepted() => EfDesignAtomicWriteStageResult.Accepted("result", "{}");
        var policies = new[]
        {
            ("global", PersistenceAccessContext.Global),
            ("privileged-global", PersistenceAccessContext.PrivilegedGlobal(new PersistenceAccessPurpose("global-delete"))),
            ("across-scopes", PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("across-delete")))
        };

        foreach (var (key, policy) in policies)
        {
            var writer = new EfDesignAtomicWrite(db, new TestAccess(policy));
            var failure = await Assert.ThrowsAsync<DesignPersistenceException>(() => writer.ExecuteAsync(
                new EfDesignAtomicWriteRequest(new EfDesignOperationIdentity("delete", key), key, ["activityDefinition"]),
                async (context, cancellationToken) =>
                {
                    var entity = await context.Db.ActivityDefinitions.SingleAsync(x => x.Id == "delete-protected", cancellationToken);
                    context.Db.ActivityDefinitions.Remove(entity);
                    return Accepted();
                }));

            Assert.IsType<InvalidDataException>(failure.InnerException);
            Assert.True(await db.ActivityDefinitions.AnyAsync(x => x.Id == "delete-protected"));
            Assert.False(await db.ActivityDesignOperations.AnyAsync(x => x.OperationKey == key));
        }
    }

    [Fact]
    public void Fork_identity_framing_separates_global_sentinel_and_separator_data()
    {
        const string separator = "\u001f";
        var receiptA = ActivityForkReceiptIdentity.Compute(null, $"actor{separator}id", "key");
        var receiptB = ActivityForkReceiptIdentity.Compute("<global>", "actor", $"id{separator}key");
        var candidateA = ActivityForkCandidateIdentity.Compute(null, $"actor{separator}id", "key");
        var candidateB = ActivityForkCandidateIdentity.Compute("<global>", "actor", $"id{separator}key");
        Assert.NotEqual(receiptA, receiptB);
        Assert.NotEqual(candidateA, candidateB);
    }

    [Fact]
    public async Task Sqlite_model_bounds_all_indexed_strings_for_provider_safe_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        foreach (var entity in db.Model.GetEntityTypes())
            foreach (var index in entity.GetIndexes())
                foreach (var property in index.Properties.Where(x => x.ClrType == typeof(string)))
                    Assert.True(property.GetMaxLength() is > 0 and <= 256, $"{entity.Name}.{property.Name} is not bounded");
        var authorityValidity = db.Model.FindEntityType(typeof(ActivityDefinitionManagementProjectionRevision))!.FindProperty(nameof(ActivityDefinitionManagementProjectionRevision.ContentAuthorityIsValid))!;
        Assert.Equal(ValueGenerated.OnAddOrUpdate, authorityValidity.ValueGenerated);
        Assert.NotNull(authorityValidity.GetComputedColumnSql());
    }

    [Fact]
    public async Task Sqlite_version_hash_is_immutable_after_insert()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var version = Version("d1", "v1", "tenant-a");
        version.Hash = "hash-a";
        version.DescriptorPayloadSource = "descriptor-a";
        version.InputsSource = "inputs-a";
        version.OutputsSource = "outputs-a";
        version.DesignFacetsSource = "facets-a";
        db.ActivityDefinitions.Add(Definition("d1", "tenant-a"));
        db.ActivityDefinitionVersions.Add(version);
        await db.SaveChangesAsync();
        version.Hash = "hash-b";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync());
        var versionMetadata = db.Model.FindEntityType(typeof(ActivityDefinitionVersion))!;
        foreach (var property in new[] { nameof(ActivityDefinitionVersion.DescriptorPayloadSource), nameof(ActivityDefinitionVersion.InputsSource), nameof(ActivityDefinitionVersion.OutputsSource), nameof(ActivityDefinitionVersion.DesignFacetsSource) })
            Assert.Equal(PropertySaveBehavior.Throw, versionMetadata.FindProperty(property)!.GetAfterSaveBehavior());

        var mutations = new (string Property, Action<ActivityDefinitionVersion> Mutate)[]
        {
            (nameof(ActivityDefinitionVersion.DescriptorPayloadSource), value => value.DescriptorPayloadSource = "descriptor-b"),
            (nameof(ActivityDefinitionVersion.InputsSource), value => value.InputsSource = "inputs-b"),
            (nameof(ActivityDefinitionVersion.OutputsSource), value => value.OutputsSource = "outputs-b"),
            (nameof(ActivityDefinitionVersion.DesignFacetsSource), value => value.DesignFacetsSource = "facets-b")
        };
        foreach (var (property, mutate) in mutations)
        {
            await using var mutationDb = new ActivitiesDesignSqliteDbContext(options);
            var persisted = await mutationDb.ActivityDefinitionVersions.SingleAsync(x => x.Id == "v1");
            var original = (string?)mutationDb.Entry(persisted).Property(property).CurrentValue;
            mutate(persisted);
            await Assert.ThrowsAsync<InvalidOperationException>(() => mutationDb.SaveChangesAsync());
            await using var verificationDb = new ActivitiesDesignSqliteDbContext(options);
            var reloaded = await verificationDb.ActivityDefinitionVersions.SingleAsync(x => x.Id == "v1");
            Assert.Equal(original, verificationDb.Entry(reloaded).Property(property).CurrentValue);
        }
    }

    [Fact]
    public async Task Sqlite_find_projection_respects_scoped_access_for_global_query()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.AddRange(Projection("hidden", "tenant-b"), Projection("global-visible", null), Projection("literal-star", "*"));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Null(await store.FindDefinitionAsync("hidden", null));
        Assert.Equal("global-visible", (await store.FindDefinitionAsync("global-visible", "tenant-a", 1))!.DefinitionId);
        Assert.Null(await store.FindDefinitionAsync("literal-star", "tenant-a", 1));
        var literalStarStore = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("*"))));
        Assert.Equal("literal-star", (await literalStarStore.FindDefinitionAsync("literal-star", "*", 1))!.DefinitionId);
    }

    [Fact]
    public async Task Sqlite_projection_authority_filter_uses_provider_safe_scalar_projection()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.AddRange(
            Projection("design", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("provider", "tenant-a", ActivityContentAuthorityKind.ProviderSource));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UtcNow });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var store = new EfActivityDesignStores(db);
        var page = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 0, 10, Authority: ActivityContentAuthorityKind.ProviderSource));
        Assert.Equal("provider", Assert.Single(page.Items).DefinitionId);
    }

    [Fact]
    public async Task Sqlite_definition_projection_pages_large_result_in_sql()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.AddRange(Enumerable.Range(0, 600).Select(index => Projection($"definition-{index:D4}", "tenant-a")));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UtcNow });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        var store = new EfActivityDesignStores(db);
        var page = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 250, 10));

        Assert.Equal(600, page.TotalCount);
        Assert.Equal(10, page.Items.Count);
        Assert.Equal("definition-0250", page.Items[0].ResourceId);
        Assert.Equal(260, page.NextOffset);
    }

    [Fact]
    public async Task Sqlite_definition_projection_paging_excludes_invalid_authority_rows_before_bounds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(commands).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.AddRange(
            Projection("a-invalid-null", "tenant-a"), Projection("b-valid", "tenant-a"),
            Projection("c-invalid-malformed", "tenant-a"), Projection("d-valid", "tenant-a"),
            Projection("e-invalid-missing-kind", "tenant-a"), Projection("f-valid", "tenant-a"),
            Projection("g-invalid-blank-key", "tenant-a"), Projection("h-valid", "tenant-a"),
            Projection("i-invalid-mismatched", "tenant-a"), Projection("j-valid", "tenant-a"),
            Projection("aa-stale-digest", "tenant-a"));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UtcNow });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_management_definitions SET ContentAuthority = NULL, ContentAuthorityCanonicalJson = NULL WHERE ResourceId = 'a-invalid-null'");
        var malformedJson = "{";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {malformedJson}, ContentAuthorityCanonicalJson = {malformedJson} WHERE ResourceId = 'c-invalid-malformed'");
        var missingKindJson = "{\"authorityKey\":\"elsa.activity-graph\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {missingKindJson}, ContentAuthorityCanonicalJson = {missingKindJson} WHERE ResourceId = 'e-invalid-missing-kind'");
        var blankKeyJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, " "));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {blankKeyJson}, ContentAuthorityCanonicalJson = {blankKeyJson} WHERE ResourceId = 'g-invalid-blank-key'");
        var providerJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.ProviderSource, "provider"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {providerJson}, ContentAuthorityCanonicalJson = {providerJson} WHERE ResourceId = 'i-invalid-mismatched'");
        var staleDigest = await db.ActivityDefinitionManagementProjections
            .Where(x => x.ResourceId == "aa-stale-digest")
            .Select(x => x.ContentAuthorityIntegrityHash)
            .SingleAsync();
        var staleAuthorityNode = JsonNode.Parse(JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, "tampered")))!.AsObject();
        staleAuthorityNode["integrityHash"] = staleDigest;
        var staleAuthorityJson = staleAuthorityNode.ToJsonString();
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {staleAuthorityJson}, ContentAuthorityCanonicalJson = {staleAuthorityJson}, ContentAuthorityAuthorityKeyJson = {JsonSerializer.Serialize("tampered")}, ContentAuthorityAuthorityKey = 'tampered' WHERE ResourceId = 'aa-stale-digest'");

        var store = new EfActivityDesignStores(db);
        var first = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 0, 2));
        var second = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 2, 2));

        Assert.Equal(5, first.TotalCount);
        Assert.Equal(["b-valid", "d-valid"], first.Items.Select(x => x.ResourceId));
        Assert.Equal(2, first.NextOffset);
        Assert.Equal(["f-valid", "h-valid"], second.Items.Select(x => x.ResourceId));
        Assert.Equal(4, second.NextOffset);
        var third = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 4, 2));
        Assert.Equal(["j-valid"], third.Items.Select(x => x.ResourceId));
        Assert.Null(third.NextOffset);
        Assert.Contains(commands.Sql, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(commands.Sql, sql => sql.Contains("ContentAuthorityIsValid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Sqlite_definition_projection_integrity_validation_stays_bounded_for_large_catalogs()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var commands = new RecordingCommandInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(commands).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        const int definitionCount = 3_000;
        db.ActivityDefinitionManagementProjections.AddRange(Enumerable.Range(0, definitionCount).Select(index => Projection($"large-definition-{index:D4}", "tenant-a")));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UtcNow });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();

        const int offset = 2_900;
        var page = await new EfActivityDesignStores(db).ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, offset, 10));

        Assert.Equal(definitionCount, page.TotalCount);
        Assert.Equal(Enumerable.Range(offset, 10).Select(index => $"large-definition-{index:D4}"), page.Items.Select(x => x.ResourceId));
        Assert.Contains(commands.Sql, sql => sql.Contains("LIMIT", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(commands.Sql, sql => sql.Contains(" IN (", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Sqlite_projection_retention_deletes_expired_rows_and_enforces_access_and_bounds()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityManagementProjectionWatermarks.Add(new() { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 5, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UnixEpoch });
        db.ActivityManagementProjectionSnapshots.AddRange(
            new() { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UnixEpoch },
            new() { Id = "00000000000000000002", Sequence = 2, AsOf = DateTimeOffset.UnixEpoch },
            new() { Id = "00000000000000000005", Sequence = 5, AsOf = DateTimeOffset.UnixEpoch });
        var expiredDefinition = Projection("retained-definition", "tenant-a");
        expiredDefinition.ValidToSequenceExclusive = 2;
        db.ActivityDefinitionManagementProjections.AddRange(expiredDefinition, Projection("current-definition", "tenant-a"));
        db.ActivityDraftManagementProjections.Add(new ActivityDefinitionDraftManagementProjectionRevision
        {
            Id = "draft:expired", ResourceId = "draft-expired", DefinitionId = "retained-definition", TenantId = "tenant-a", ValidFromSequence = 1,
            ValidToSequenceExclusive = 2, ValidFromKey = "00000000000000000001", ValidToKey = "00000000000000000002", VisibilityKey = "tenant-a", SortKey = "draft-expired", SearchText = "DRAFT-EXPIRED",
            DraftId = "draft-expired", ProviderKey = "provider", ProviderSchemaVersion = "1", Status = ActivityDefinitionDraftStatus.Active, UpdatedAt = DateTimeOffset.UnixEpoch
        });
        db.ActivityVersionManagementProjections.Add(new ActivityDefinitionVersionManagementProjectionRevision
        {
            Id = "version:expired", ResourceId = "version-expired", DefinitionId = "retained-definition", TenantId = "tenant-a", ValidFromSequence = 1,
            ValidToSequenceExclusive = 2, ValidFromKey = "00000000000000000001", ValidToKey = "00000000000000000002", VisibilityKey = "tenant-a", SortKey = "version-expired", SearchText = "VERSION-EXPIRED",
            DefinitionVersionId = "version-expired", Version = "1.0.0", ProviderKey = "provider", ProviderSchemaVersion = "1", PublishedAt = DateTimeOffset.UnixEpoch
        });
        await db.SaveChangesAsync();

        var access = new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("retention-test")));
        var retention = new EfActivityManagementProjectionRetention(db, access);
        await retention.ExpireBeforeAsync(3, DateTimeOffset.UnixEpoch.AddDays(1));

        Assert.Equal(3, await db.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
        Assert.Equal(["current-definition"], await db.ActivityDefinitionManagementProjections.Select(x => x.ResourceId).ToArrayAsync());
        Assert.Empty(await db.ActivityDraftManagementProjections.ToListAsync());
        Assert.Empty(await db.ActivityVersionManagementProjections.ToListAsync());
        Assert.Equal(new[] { 5L }, await db.ActivityManagementProjectionSnapshots.Select(x => x.Sequence).ToArrayAsync());
        await retention.ExpireBeforeAsync(2, DateTimeOffset.UtcNow);
        Assert.Equal(3, await db.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => retention.ExpireBeforeAsync(6, DateTimeOffset.UtcNow));

        var scoped = new EfActivityManagementProjectionRetention(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => scoped.ExpireBeforeAsync(4, DateTimeOffset.UtcNow));
    }

    [Fact]
    public async Task Sqlite_projection_retention_rolls_back_bounded_delete_batches_on_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var failure = new ThrowOnDeleteInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(failure).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityManagementProjectionWatermarks.Add(new() { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 3, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UnixEpoch });
        db.ActivityManagementProjectionSnapshots.Add(new() { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UnixEpoch });
        var rollbackDefinition = Projection("rollback-definition", "tenant-a");
        rollbackDefinition.ValidToSequenceExclusive = 2;
        db.ActivityDefinitionManagementProjections.Add(rollbackDefinition);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        failure.Enabled = true;

        var retention = new EfActivityManagementProjectionRetention(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("retention-rollback-test"))));
        var failureException = await Assert.ThrowsAsync<DesignPersistenceException>(() => retention.ExpireBeforeAsync(2, DateTimeOffset.UtcNow));
        Assert.Equal(DesignPersistenceFailureKind.Provider, failureException.FailureKind);
        Assert.Equal(1, await db.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
        Assert.True(await db.ActivityDefinitionManagementProjections.AnyAsync(x => x.ResourceId == "rollback-definition"));
    }

    [Fact]
    public async Task Sqlite_projection_retention_preserves_concurrency_failures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var failure = new ThrowOnConcurrencyInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(failure).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityManagementProjectionWatermarks.Add(new() { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 3, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UnixEpoch });
        var expired = Projection("concurrency-definition", "tenant-a");
        expired.ValidToSequenceExclusive = 2;
        db.ActivityDefinitionManagementProjections.Add(expired);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        failure.Enabled = true;

        var retention = new EfActivityManagementProjectionRetention(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("retention-concurrency-test"))));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => retention.ExpireBeforeAsync(2, DateTimeOffset.UtcNow));
        Assert.Equal(1, await db.ActivityManagementProjectionWatermarks.Select(x => x.RetainedFromSequence).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_projection_writer_preserves_concurrency_failures()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var failure = new ThrowOnProjectionWriterConcurrencyInterceptor();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(failure).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        failure.Enabled = true;

        var definition = Definition("writer-concurrency", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = "authoring-writer-concurrency", TenantId = "tenant-a", DefinitionId = definition.Id,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        };
        var writer = new EfActivityManagementProjectionWriter(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
    }

    [Fact]
    public async Task Sqlite_scoped_dependency_read_includes_global_edges_but_not_other_tenant_edges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("global", "root", null, "provider"));
        var owner = new ActivityDefinitionReference("ActivityVersion", "global", "root", "1.0.0", TenantId: null);
        var globalDependency = new ActivityDefinitionReference("ActivityVersion", "g", "global-dep", "1.0.0", TenantId: null);
        var tenantDependency = new ActivityDefinitionReference("ActivityVersion", "t", "tenant-dep", "1.0.0", TenantId: "tenant-a");
        var otherTenantDependency = new ActivityDefinitionReference("ActivityVersion", "o", "other-dep", "1.0.0", TenantId: "tenant-b");
        await db.SaveChangesAsync();
        await ProjectionStore(db).RebuildCurrentAsync("scoped-read", DateTimeOffset.UtcNow,
        [
            new("global-edge", owner, globalDependency, new("occurrence-a", []), true, 1, []),
            new("tenant-edge", owner, tenantDependency, new("occurrence-b", []), true, 1, []),
            new("other-edge", owner, otherTenantDependency, new("occurrence-c", []), true, 1, [])
        ]);

        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var result = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, false, new HashSet<string>(["Versions"])), "tenant-a", null, 0, 10));
        Assert.Equal(["global-edge", "tenant-edge"], result.Items.Select(x => x.RelationshipId));
    }

    [Fact]
    public async Task Sqlite_dependency_read_filters_mixed_version_and_draft_owners_by_include()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("root-definition", "root", "tenant-a", "provider"));
        await db.SaveChangesAsync();
        var versionOwner = new ActivityDefinitionReference("ActivityVersion", "owner-definition", "owner-version", "1.0.0", TenantId: "tenant-a");
        var draftOwner = new ActivityDefinitionReference("ActivityDraft", "owner-definition", DraftId: "draft", Revision: 1, TenantId: "tenant-a");
        var dependency = new ActivityDefinitionReference("ActivityVersion", "root-definition", "root", "1.0.0", TenantId: "tenant-a");
        var store = ProjectionStore(db);
        await store.RebuildCurrentAsync("mixed-owners", DateTimeOffset.UtcNow,
        [
            new("version-edge", versionOwner, dependency, new("version-occurrence", []), true, 1, []),
            new("draft-edge", draftOwner, dependency, new("draft-occurrence", []), true, 1, [])
        ]);

        var versions = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions"])), "tenant-a", null, 0, 10));
        var drafts = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Drafts"])), "tenant-a", null, 0, 10));
        var both = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions", "Drafts"])), "tenant-a", null, 0, 10));
        Assert.Equal("version-edge", Assert.Single(versions.Items).RelationshipId);
        Assert.Equal("draft-edge", Assert.Single(drafts.Items).RelationshipId);
        Assert.Equal(["draft-edge", "version-edge"], both.Items.Select(x => x.RelationshipId));
    }

    [Fact]
    public async Task Sqlite_scoped_dependency_rebuild_rejects_other_tenant_edges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "owner", "root", "1.0.0", TenantId: "tenant-a");
        var otherTenantDependency = new ActivityDefinitionReference("ActivityVersion", "dependency", "other", "1.0.0", TenantId: "tenant-b");
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RebuildCurrentAsync("cross-tenant", DateTimeOffset.UtcNow,
        [new("cross-tenant-edge", owner, otherTenantDependency, new("occurrence", []), true, 1, [])]));
        Assert.Empty(await db.ActivityDependencyProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_dependency_rebuild_honors_caller_sequence_and_reuses_same_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("root-definition", "root", null, "provider"));
        await db.SaveChangesAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "root-definition", "root", "1.0.0");
        var dependency = new ActivityDefinitionReference("ActivityVersion", "dependency", "dependency", "1.0.0");
        var first = new ActivityDependencyProjectionRebuild("rebuild-17", 17, DateTimeOffset.UnixEpoch, [new("edge-17", owner, dependency, new("occurrence-17", []), true, 1, [])]);
        var second = first with { RebuildId = "rebuild-18", Sequence = 18, Items = [first.Items[0] with { RelationshipId = "edge-18" }] };
        var store = ProjectionStore(db);

        await store.RebuildAsync(first);
        await store.RebuildAsync(second);
        var reused = second with { Items = [second.Items[0] with { RelationshipId = "edge-reused" }] };
        await store.RebuildAsync(reused);

        var state = await db.ActivityDependencyProjections.SingleAsync();
        Assert.Equal(18, state.Sequence);
        Assert.Equal("rebuild-18", state.RebuildId);
        Assert.Equal("edge-reused", Assert.Single(state.Items).RelationshipId);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RebuildAsync(first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RebuildAsync(reused with { RebuildId = "another-rebuild" }));
    }

    [Fact]
    public async Task Sqlite_dependency_rebuild_canonicalizes_equivalent_item_order_for_reuse()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("root-definition", "root", null, "provider"));
        await db.SaveChangesAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "root-definition", "root", "1.0.0");
        var firstDependency = new ActivityDefinitionReference("ActivityVersion", "first-definition", "first", "1.0.0");
        var secondDependency = new ActivityDefinitionReference("ActivityVersion", "second-definition", "second", "1.0.0");
        var first = new ActivityDependencyItem("first-edge", owner, firstDependency, new("first-occurrence", []), true, 1, []);
        var second = new ActivityDependencyItem("second-edge", owner, secondDependency, new("second-occurrence", []), true, 1, []);
        var store = ProjectionStore(db);

        await store.RebuildAsync(new ActivityDependencyProjectionRebuild("rebuild-17", 17, DateTimeOffset.UnixEpoch, [second, first]));
        var firstRead = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, false, new HashSet<string>(["Versions"])), null, null, 0, 10));
        await store.RebuildAsync(new ActivityDependencyProjectionRebuild("rebuild-17", 17, DateTimeOffset.UnixEpoch, [first, second]));
        var secondRead = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, false, new HashSet<string>(["Versions"])), null, null, 0, 10));

        Assert.Equal(firstRead.Watermark, secondRead.Watermark);
        Assert.Equal(["first-edge", "second-edge"], (await db.ActivityDependencyProjections.SingleAsync()).Items.Select(x => x.RelationshipId));
    }

    [Fact]
    public async Task Sqlite_dependency_rebuild_requires_nonblank_occurrence_identity()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "owner", "root", "1.0.0");
        var dependency = new ActivityDefinitionReference("ActivityVersion", "dependency", "dep", "1.0.0");
        var store = ProjectionStore(db);

        await Assert.ThrowsAsync<ArgumentException>(() => store.RebuildAsync(
            new ActivityDependencyProjectionRebuild(" ", 1, DateTimeOffset.UnixEpoch,
                [new("edge", owner, dependency, new("occurrence", []), true, 1, [])])));
        await Assert.ThrowsAsync<ArgumentException>(() => store.RebuildAsync(
            new ActivityDependencyProjectionRebuild("rebuild", 1, DateTimeOffset.UnixEpoch,
                [new("edge", owner, dependency, new(" ", []), true, 1, [])])));
        Assert.Empty(await db.ActivityDependencyProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_dependency_reads_ignore_persisted_transitive_and_reproject_direct_edges()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("root-definition", "root", null, "provider"));
        await db.SaveChangesAsync();
        var owner = new ActivityDefinitionReference("ActivityVersion", "root-definition", "root", "1.0.0");
        var dependency = new ActivityDefinitionReference("ActivityVersion", "dependency", "dep", "1.0.0");
        var transitive = new ActivityDefinitionReference("ActivityVersion", "transitive", "transitive", "1.0.0");
        await ProjectionStore(db).RebuildCurrentAsync("direct-only", DateTimeOffset.UnixEpoch,
        [
            new("direct", owner, dependency, new("direct-occurrence", []), true, 99, []),
            new("stored-transitive", dependency, transitive, new("stored-occurrence", []), false, 1, [])
        ]);

        var page = await new EfActivityDesignStores(db).ReadAsync(new ActivityDependencyProjectionReadRequest(
            "root", new(ActivityDependencyDirection.Outbound, true, new HashSet<string>(["Versions"])), null, null, 0, 10));
        var item = Assert.Single(page.Items);
        Assert.Equal("direct", item.RelationshipId);
        Assert.Equal(1, item.Depth);
    }

    [Fact]
    public async Task Sqlite_projection_reads_fail_closed_for_null_malformed_and_mismatched_authority_json()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.AddRange(
            Projection("valid", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("null", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("malformed", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("mismatch", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("blank-key", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("design-source", "tenant-a", ActivityContentAuthorityKind.Design),
            Projection("missing-kind", "tenant-a", ActivityContentAuthorityKind.Design));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark { Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1, RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UtcNow });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot { Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UtcNow });
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_management_definitions SET ContentAuthority = NULL, ContentAuthorityCanonicalJson = NULL WHERE ResourceId = 'null'");
        var malformedJson = "{";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {malformedJson}, ContentAuthorityCanonicalJson = {malformedJson} WHERE ResourceId = 'malformed'");
        var providerJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.ProviderSource, "provider"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {providerJson}, ContentAuthorityCanonicalJson = {providerJson} WHERE ResourceId = 'mismatch'");
        var blankKeyJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, " "));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {blankKeyJson}, ContentAuthorityCanonicalJson = {blankKeyJson} WHERE ResourceId = 'blank-key'");
        var designSourceJson = JsonSerializer.Serialize(new ActivityContentAuthority(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design, "provider-lineage"));
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {designSourceJson}, ContentAuthorityCanonicalJson = {designSourceJson} WHERE ResourceId = 'design-source'");
        var missingKindJson = "{\"authorityKey\":\"elsa.activity-graph\"}";
        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET ContentAuthority = {missingKindJson}, ContentAuthorityCanonicalJson = {missingKindJson} WHERE ResourceId = 'missing-kind'");

        var store = new EfActivityDesignStores(db);
        var page = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 0, 10));
        Assert.Equal(["valid"], page.Items.Select(x => x.DefinitionId));
        Assert.Null(await store.FindDefinitionAsync("null", "tenant-a", 1));
        Assert.Null(await store.FindDefinitionAsync("malformed", "tenant-a", 1));
        Assert.Null(await store.FindDefinitionAsync("mismatch", "tenant-a", 1));
        Assert.Null(await store.FindDefinitionAsync("blank-key", "tenant-a", 1));
        Assert.Null(await store.FindDefinitionAsync("design-source", "tenant-a", 1));
        Assert.Null(await store.FindDefinitionAsync("missing-kind", "tenant-a", 1));
    }

    [Fact]
    public async Task Sqlite_draft_presentation_and_discard_advance_paired_revisions_and_require_active()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var draft = Draft("draft", "d1", "tenant-a", 1);
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        db.ActivityDefinitionDrafts.Add(draft);
        db.ActivityDefinitionDraftLayouts.Add(Layout("draft", "tenant-a", 1));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var updated = await store.ExecuteAsync(new UpdateActivityDraftPresentationRequest("draft", 1, "updated", DateTimeOffset.UtcNow));
        Assert.Equal(2, updated.Revision);
        Assert.Equal(2, await db.ActivityDefinitionDraftLayouts.Where(x => x.DraftId == "draft").Select(x => x.Revision).SingleAsync());
        await store.ExecuteAsync(new DiscardActivityDraftRequest("draft", 2));
        Assert.Equal(3, await db.ActivityDefinitionDrafts.Where(x => x.Id == "draft").Select(x => x.Revision).SingleAsync());
        Assert.Equal(ActivityDefinitionDraftStatus.Discarded, await db.ActivityDefinitionDrafts.Where(x => x.Id == "draft").Select(x => x.Status).SingleAsync());
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ExecuteAsync(new UpdateActivityDraftPresentationRequest("draft", 3, "nope", DateTimeOffset.UtcNow)));
    }

    [Fact]
    public async Task Sqlite_id_only_draft_mutations_admit_only_the_authoritative_scope()
    {
        var actions = new Func<EfActivityDesignStores, Task>[]
        {
            async store => _ = await store.ExecuteAsync(new UpdateActivityDraftPresentationRequest("draft", 1, "updated", DateTimeOffset.UtcNow)),
            async store => _ = await store.ExecuteAsync(new ReplaceActivityDraftRequest("draft", 1, Draft("replacement", "d1", "tenant-a", 1).State, [], "replaced")),
            async store => await store.ExecuteAsync(new DiscardActivityDraftRequest("draft", 1))
        };

        foreach (var action in actions)
        {
            async Task RunAsync(string? tenantId, PersistenceAccessContext accessContext, bool shouldCommit)
            {
                await using var connection = new SqliteConnection("Data Source=:memory:");
                await connection.OpenAsync();
                var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
                await using var db = new ActivitiesDesignSqliteDbContext(options);
                await db.Database.EnsureCreatedAsync();
                db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
                {
                    Id = "d1", DefinitionId = "d1", TenantId = tenantId,
                    ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
                });
                db.ActivityDefinitionDrafts.Add(new ActivityDefinitionDraft
                {
                    Id = "draft", DefinitionId = "d1", TenantId = tenantId, Revision = 1,
                    State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>())
                });
                db.ActivityDefinitionDraftLayouts.Add(new ActivityDefinitionDraftLayout
                {
                    Id = "layout-draft", DraftId = "draft", TenantId = tenantId, Revision = 1, Records = []
                });
                await db.SaveChangesAsync();
                var store = new EfActivityDesignStores(db, new TestAccess(accessContext));

                if (shouldCommit)
                    await action(store);
                else
                    await Assert.ThrowsAsync<InvalidOperationException>(() => action(store));

                var persisted = await db.ActivityDefinitionDrafts.AsNoTracking().SingleAsync(x => x.Id == "draft");
                Assert.Equal(shouldCommit ? 2 : 1, persisted.Revision);
                if (shouldCommit && accessContext.IsGlobal)
                    Assert.Null(persisted.TenantId);
                if (!shouldCommit)
                    Assert.Equal(ActivityDefinitionDraftStatus.Active, persisted.Status);
            }

            await RunAsync("tenant-a", PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("draft-write-admission")), shouldCommit: false);
            await RunAsync("tenant-a", PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")), shouldCommit: true);
            await RunAsync("tenant-a", PersistenceAccessContext.Global, shouldCommit: false);
            await RunAsync(null, PersistenceAccessContext.Global, shouldCommit: true);
        }
    }

    [Fact]
    public async Task Sqlite_validation_rejects_stale_revision_and_conflict_copy_preserves_lineage()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        db.ActivityDefinitionDrafts.Add(Draft("source", "d1", "tenant-a", 4, "v1"));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ExecuteAsync(new ActivityDraftValidationState { Id = "validation", DraftId = "source", TenantId = "tenant-a", Revision = 3, Diagnostics = [] }));
        var copy = Draft("copy", "d1", "tenant-a", 4, "v1");
        await store.ExecuteAsync(new CreateActivityDraftConflictCopyRequest("source", 4, copy, Layout("copy", "tenant-a", 4)));
        Assert.Equal("v1", (await db.ActivityDefinitionDrafts.SingleAsync(x => x.Id == "copy")).SourceVersionId);
    }

    [Fact]
    public async Task Sqlite_validation_rolls_back_when_the_draft_fence_is_stale()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var interceptor = new SuppressConcurrencyFenceInterceptor("elsa_activity_definition_drafts");
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).AddInterceptors(interceptor).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var source = Draft("source", "d1", "tenant-a", 4);
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        db.ActivityDefinitionDrafts.Add(source);
        await db.SaveChangesAsync();
        var before = source.LastModifiedAt;
        interceptor.Enabled = true;

        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => new EfActivityDesignStores(db).ExecuteAsync(new ActivityDraftValidationState { Id = "validation", DraftId = source.Id, TenantId = source.TenantId, Revision = source.Revision, Diagnostics = [] }));

        Assert.Null(await db.ActivityDraftValidations.SingleOrDefaultAsync(x => x.Id == "validation"));
        var persisted = await db.ActivityDefinitionDrafts.AsNoTracking().SingleAsync(x => x.Id == source.Id);
        Assert.Equal(source.Revision, persisted.Revision);
        Assert.Equal(before, persisted.LastModifiedAt);
    }

    [Fact]
    public async Task Sqlite_validation_requires_matching_tenant_and_design_authority()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.ProviderSource, "provider") });
        db.ActivityDefinitionDrafts.Add(Draft("draft", "d1", "tenant-a", 1));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(new ActivityDraftValidationState { Id = "validation", DraftId = "draft", TenantId = "tenant-a", Revision = 1, Diagnostics = [] }));
        Assert.Empty(await db.ActivityDraftValidations.ToListAsync());

        db.ChangeTracker.Clear();
        db.ActivityDefinitionAuthoringStates.RemoveRange(db.ActivityDefinitionAuthoringStates);
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) });
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(new ActivityDraftValidationState { Id = "validation-2", DraftId = "draft", TenantId = "tenant-b", Revision = 1, Diagnostics = [] }));
        Assert.Empty(await db.ActivityDraftValidations.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_projection_hydrates_head_recommendation_and_provider_keys()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("d1", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = "v1", RecommendedVersionId = "v1" };
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        db.ActivityDefinitionVersionPublications.Add(Publication("d1", "v1", "tenant-a", "provider"));
        await db.SaveChangesAsync();
        await new EfActivityManagementProjectionWriter(db).WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [new(definition, authoring)], [], []));
        var projection = await db.ActivityDefinitionManagementProjections.SingleAsync();
        Assert.Equal("v1", projection.Head?.DefinitionVersionId);
        Assert.Equal("provider", projection.HeadProviderKey);
        Assert.Equal("provider", projection.RecommendationProviderKey);
        Assert.Equal(ActivityContentAuthorityKind.Design, projection.ContentAuthorityKind);
    }

    [Fact]
    public async Task Sqlite_projection_rejects_missing_or_cross_tenant_publication_binding()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("d1", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = "missing" };
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new(DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
        Assert.Empty(await db.ActivityDefinitionManagementProjections.ToListAsync());

        await using var crossDb = new ActivitiesDesignSqliteDbContext(options);
        var crossDefinition = await crossDb.ActivityDefinitions.SingleAsync();
        var crossAuthoring = await crossDb.ActivityDefinitionAuthoringStates.SingleAsync();
        crossAuthoring.HeadVersionId = "other";
        crossDb.ActivityDefinitionVersionPublications.Add(Publication("other-definition", "other", "tenant-b", "provider"));
        await crossDb.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfActivityManagementProjectionWriter(crossDb).WriteAsync(new(DateTimeOffset.UtcNow, [new(crossDefinition, crossAuthoring)], [], [])));
        Assert.Empty(await crossDb.ActivityDefinitionManagementProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_projection_of_an_unpublished_head_version_carries_the_head_id_without_a_head_reference()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("imported", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = "imported", DefinitionId = "imported", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, "elsa3.collection-import"), HeadVersionId = "imported-v1" };
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionVersions.Add(Version("imported", "imported-v1", "tenant-a"));
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        await db.SaveChangesAsync();

        await new EfActivityManagementProjectionWriter(db).WriteAsync(new(DateTimeOffset.UtcNow, [new(definition, authoring)], [], []));

        var projection = await db.ActivityDefinitionManagementProjections.SingleAsync();
        Assert.Equal("imported-v1", projection.HeadVersionId);
        Assert.Null(projection.Head);
        Assert.Null(projection.HeadProviderKey);
    }

    [Fact]
    public async Task Sqlite_projection_rejects_a_head_naming_a_version_of_another_definition_or_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("d1", "tenant-a");
        db.ActivityDefinitions.AddRange(definition, Definition("d2", "tenant-a"), Definition("d1", "tenant-b"));
        db.ActivityDefinitionVersions.AddRange(Version("d2", "other-definition-version", "tenant-a"), Version("d1", "other-tenant-version", "tenant-b"));
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);

        foreach (var head in new[] { "other-definition-version", "other-tenant-version" })
        {
            var authoring = new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = head };
            await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new(DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
        }

        Assert.Empty(await db.ActivityDefinitionManagementProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_atomic_write_runs_inside_a_caller_owned_transaction_supplied_by_the_factory()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await using var outer = await db.Database.BeginTransactionAsync();
        var handles = new List<NonOwningTransaction>();
        var writer = new EfDesignAtomicWrite(db, transactionFactory: _ =>
        {
            var handle = new NonOwningTransaction(outer.TransactionId);
            handles.Add(handle);
            return Task.FromResult<Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction>(handle);
        });

        var result = await writer.ExecuteAsync(
            new(new EfDesignOperationIdentity("outer.v1", "key"), "request", ["activityDefinition"], "tenant-a"),
            (context, _) =>
            {
                context.Db.ActivityDefinitions.Add(Definition("inside-outer", "tenant-a"));
                return Task.FromResult(EfDesignAtomicWriteStageResult.Accepted("result", "{}"));
            });

        Assert.Equal(EfDesignAtomicWriteStatus.Committed, result.Status);
        Assert.True(Assert.Single(handles).Committed);
        await outer.RollbackAsync();
        db.ChangeTracker.Clear();
        Assert.Empty(await db.ActivityDefinitions.ToListAsync());
        Assert.Empty(await db.ActivityDesignOperations.ToListAsync());
    }

    private sealed class NonOwningTransaction(Guid transactionId) : Microsoft.EntityFrameworkCore.Storage.IDbContextTransaction
    {
        public bool Committed { get; private set; }
        public Guid TransactionId => transactionId;
        public void Commit() => Committed = true;
        public Task CommitAsync(CancellationToken cancellationToken = default) { Commit(); return Task.CompletedTask; }
        public void Rollback() { }
        public Task RollbackAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public void Dispose() { }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }

    [Fact]
    public async Task Sqlite_projection_rejects_child_for_existing_definition_in_another_tenant()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("d1", "tenant-a"));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfActivityManagementProjectionWriter(db).WriteAsync(new(
            DateTimeOffset.UtcNow, [], [Draft("wrong", "d1", "tenant-b", 1)], [])));
        Assert.Empty(await db.ActivityDraftManagementProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_projection_rejects_child_when_existing_current_definition_identity_is_corrupt()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("d1", "tenant-a", definitionId: "wrong-definition"));
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfActivityManagementProjectionWriter(db).WriteAsync(new(
            DateTimeOffset.UtcNow, [], [Draft("draft", "d1", "tenant-a", 1)], [])));
        Assert.Empty(await db.ActivityDraftManagementProjections.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_non_atomic_create_clears_staged_entities_when_projection_fails()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("create-failure", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = definition.Id, DefinitionId = definition.Id, TenantId = definition.TenantId, ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = "missing" };
        var draft = Draft("create-failure-draft", definition.Id, "tenant-a", 1);
        var layout = Layout(draft.Id, "tenant-a", 1);
        var store = new EfActivityDesignStores(db, null, null, new EfActivityManagementProjectionWriter(db));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(new CreateActivityDefinitionRequest(definition, authoring, draft, layout)));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.ActivityDefinitions.ToListAsync());
        Assert.Empty(await db.ActivityDefinitionDrafts.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_lifecycle_requires_expected_head_and_active_same_definition_replacement()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.Add(Definition("d1", "tenant-a"));
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState { Id = "d1", DefinitionId = "d1", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = "v1", RecommendedVersionId = "v1" });
        db.ActivityDefinitionVersionPublications.AddRange(Publication("d1", "v1", "tenant-a", "provider"), Publication("d1", "v2", "tenant-a", "provider"));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var stale = new ChangeActivityVersionLifecycleRequest("v1", ActivityDefinitionVersionLifecycle.Active, ActivityDefinitionVersionLifecycle.Retired, "retire", "tenant-a", new("wrong-head", "v1", ActivityRecommendationDisposition.Replace, "v2", ActivityDefinitionVersionLifecycle.Active));
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ExecuteAsync(stale));
        var changed = await store.ExecuteAsync(new ChangeActivityVersionLifecycleRequest("v1", ActivityDefinitionVersionLifecycle.Active, ActivityDefinitionVersionLifecycle.Retired, "retire", "tenant-a", new("v1", "v1", ActivityRecommendationDisposition.Replace, "v2", ActivityDefinitionVersionLifecycle.Active)));
        Assert.Equal(ActivityDefinitionVersionLifecycle.Retired, changed.Lifecycle);
        Assert.Equal("v2", await db.ActivityDefinitionAuthoringStates.Where(x => x.DefinitionId == "d1").Select(x => x.RecommendedVersionId).SingleAsync());
        await store.ExecuteAsync(new ChangeActivityVersionLifecycleRequest("v1", ActivityDefinitionVersionLifecycle.Retired, ActivityDefinitionVersionLifecycle.Revoked, "revoke", "tenant-a"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ExecuteAsync(new ChangeActivityVersionLifecycleRequest("v1", ActivityDefinitionVersionLifecycle.Revoked, ActivityDefinitionVersionLifecycle.Active, "restore", "tenant-a")));
    }

    [Fact]
    public async Task Sqlite_fork_receipt_roundtrip_retains_full_authoring_material()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("forked", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = "forked", DefinitionId = "forked", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design), HeadVersionId = "v1" };
        var draft = Draft("draft", "forked", "tenant-a", 1);
        var layout = Layout("draft", "tenant-a", 1);
        db.ActivityDefinitions.Add(definition);
        ActivityForkReceipt Receipt(string id, string? tenant, string idempotency, string actor = "actor") => new()
        {
            Id = id, TenantId = tenant, IdempotencyKey = idempotency, CandidateId = "candidate", PublicCandidateId = "public", RequestFingerprint = "request", AccessBindingFingerprint = "access", ActorId = actor, AuthorizationProfile = "auth", DefinitionId = "forked", ActivityTypeKey = definition.ActivityTypeKey, DraftId = "draft", DefinitionMaterialJson = JsonSerializer.Serialize(definition), AuthoringState = authoring, Draft = draft, Layout = layout, MigrationDiagnostics = [new ActivityDiagnostic("migration", ActivityDiagnosticSeverity.Warning, "warning", new("fork", "forked"))], AppliedAt = DateTimeOffset.UtcNow
        };
        db.ActivityForkReceipts.AddRange(Receipt("receipt", "tenant-a", "op"), Receipt("receipt-global", null, "same-op"), Receipt("receipt-literal-global", "<global>", "same-op"), Receipt("receipt-trailing", null, "same-op ", "actor "));
        await db.SaveChangesAsync();
        Assert.Equal(ActivitiesDesignDbContext.GlobalTenantKey, await db.ActivityForkReceipts.Where(x => x.Id == "receipt-global").Select(x => x.TenantScopeKey).SingleAsync());
        Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("<global>"), await db.ActivityForkReceipts.Where(x => x.Id == "receipt-literal-global").Select(x => x.TenantScopeKey).SingleAsync());
        db.ActivityForkReceipts.Add(Receipt("receipt-global-duplicate", null, "same-op"));
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        db.ChangeTracker.Clear();
        var receipt = await new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a")))).FindReceiptAsync("receipt");
        Assert.Equal("forked", receipt!.Definition.Id);
        Assert.Equal("v1", receipt.AuthoringState.HeadVersionId);
        Assert.Equal("draft", receipt.Layout.DraftId);
        Assert.Equal("warning", Assert.Single(receipt.MigrationDiagnostics).Message);
    }

    [Fact]
    public async Task Sqlite_fork_apply_rechecks_provider_source_authority_and_candidate_source_binding()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedForkSourceAsync(db, ActivityContentAuthorityKind.Design);
        var candidate = ForkCandidateMaterial();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await store.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        await Assert.ThrowsAsync<ActivityForkCandidateStaleException>(() => store.ExecuteAsync(ApplyFork(candidate, "apply-1")));
        Assert.Equal(ActivityForkCandidateStatus.Reserved, (await store.FindCandidateAsync(candidate.Id))!.Status);
        Assert.Null(await store.FindReceiptAsync(ActivityForkReceiptIdentity.Compute("tenant-a", candidate.ActorId, "apply-1")));

        var mismatched = ForkCandidateMaterial("mismatch", "other-source-version");
        await Assert.ThrowsAsync<ActivityForkCandidateStaleException>(() => store.ExecuteAsync(new SaveActivityForkCandidateRequest(mismatched)));
    }

    [Fact]
    public async Task Sqlite_fork_apply_fails_closed_on_corrupt_physical_type_key_collision()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        await SeedForkSourceAsync(db, ActivityContentAuthorityKind.ProviderSource);
        var candidate = ForkCandidateMaterial();
        var foreign = new ActivityDefinition
        {
            Id = "foreign-collision", TenantId = "tenant-b",
            ActivityTypeKey = candidate.ReservedDefinition.ActivityTypeKey,
            Category = "Tests"
        };
        db.ActivityDefinitions.Add(foreign);
        await db.SaveChangesAsync();
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE elsa_activity_definitions SET TenantScopeKey = {0} WHERE Id = {1}",
            ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a"), foreign.Id);
        db.ChangeTracker.Clear();

        var store = new EfActivityDesignStores(db,
            new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await store.ExecuteAsync(new SaveActivityForkCandidateRequest(candidate));
        await Assert.ThrowsAsync<InvalidDataException>(() => store.ExecuteAsync(ApplyFork(candidate, "apply-collision")));
        Assert.Equal(ActivityForkCandidateStatus.Reserved, (await store.FindCandidateAsync(candidate.Id))!.Status);
        Assert.Null(await store.FindReceiptAsync(ActivityForkReceiptIdentity.Compute("tenant-a", candidate.ActorId, "apply-collision")));
    }

    [Fact]
    public async Task Sqlite_fork_candidate_identity_is_fail_closed_and_actor_scoped()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfActivityDesignStores(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var first = ForkCandidateMaterial("actor-one", actor: "actor", publicCandidateId: "public-shared");
        var second = ForkCandidateMaterial("actor-two", actor: "actor ", publicCandidateId: "public-shared");
        await store.ExecuteAsync(new SaveActivityForkCandidateRequest(first));
        await store.ExecuteAsync(new SaveActivityForkCandidateRequest(second));
        Assert.Equal(2, await db.ActivityForkCandidates.CountAsync(x => x.CandidateId == "public-shared"));
        Assert.Equal(ActivityForkIdentityMaterial.ExactHash("actor"), await db.ActivityForkCandidates.Where(x => x.Id == first.Id).Select(x => x.ActorIdentityHash).SingleAsync());
        Assert.Equal(ActivityForkIdentityMaterial.ExactHash("actor "), await db.ActivityForkCandidates.Where(x => x.Id == second.Id).Select(x => x.ActorIdentityHash).SingleAsync());

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE \"elsa_activity_fork_candidates\" SET \"ActorIdentityHash\" = {"corrupt"} WHERE \"Id\" = {first.Id}");
        await Assert.ThrowsAsync<DesignPersistenceException>(() => store.FindCandidateAsync(first.Id));
    }

    [Fact]
    public async Task Sqlite_fork_candidate_save_race_reconciles_identical_preview_and_rejects_conflict()
    {
        const string connectionString = "Data Source=file:fork-candidate-race;Mode=Memory;Cache=Shared";
        await using var keeper = new SqliteConnection(connectionString);
        await keeper.OpenAsync();
        var barrier = new ForkCandidatePreReadBarrier();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connectionString).AddInterceptors(barrier).Options;
        await using var firstDb = new ActivitiesDesignSqliteDbContext(options);
        await firstDb.Database.EnsureCreatedAsync();
        barrier.Enabled = true;
        await using var secondDb = new ActivitiesDesignSqliteDbContext(options);
        var firstStore = new EfActivityDesignStores(firstDb);
        var secondStore = new EfActivityDesignStores(secondDb);
        var created = DateTimeOffset.UtcNow;
        var identicalLeftCandidate = ForkCandidateMaterial("preview-race", createdAt: created, publicCandidateId: "public-race-left", materialSuffix: "race-left");
        var identicalRightCandidate = ForkCandidateMaterial("preview-race", createdAt: created.AddSeconds(1), publicCandidateId: "public-race-right", materialSuffix: "race-right");
        var left = firstStore.ExecuteAsync(new SaveActivityForkCandidateRequest(identicalLeftCandidate));
        var right = secondStore.ExecuteAsync(new SaveActivityForkCandidateRequest(identicalRightCandidate));
        var identicalResults = await Task.WhenAll(left, right);
        Assert.Equal(identicalLeftCandidate.Id, identicalResults[0].Id);
        Assert.Equal(identicalLeftCandidate.Id, identicalResults[1].Id);
        Assert.Equal(1, await firstDb.ActivityForkCandidates.CountAsync());

        var conflictBarrier = new ForkCandidatePreReadBarrier();
        var conflictOptions = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connectionString).AddInterceptors(conflictBarrier).Options;
        await using var conflictFirstDb = new ActivitiesDesignSqliteDbContext(conflictOptions);
        await using var conflictSecondDb = new ActivitiesDesignSqliteDbContext(conflictOptions);
        conflictBarrier.Enabled = true;
        var conflictFirstStore = new EfActivityDesignStores(conflictFirstDb);
        var conflictSecondStore = new EfActivityDesignStores(conflictSecondDb);
        var conflictingCreated = DateTimeOffset.UtcNow;
        var conflictingLeft = ForkCandidateMaterial("preview-conflict", createdAt: conflictingCreated, materialSuffix: "conflict-left");
        var conflictingRight = ForkCandidateMaterial("preview-conflict", createdAt: conflictingCreated.AddSeconds(1), requestFingerprint: "request-conflict", materialSuffix: "conflict-right");
        var conflictLeft = conflictFirstStore.ExecuteAsync(new SaveActivityForkCandidateRequest(conflictingLeft));
        var conflictRight = conflictSecondStore.ExecuteAsync(new SaveActivityForkCandidateRequest(conflictingRight));
        var conflictTasks = new[] { conflictLeft, conflictRight };
        try { await Task.WhenAll(conflictTasks); }
        catch { }
        Assert.Equal(1, conflictTasks.Count(task => task.Status == TaskStatus.RanToCompletion));
        var failure = conflictTasks.Single(task => task.IsFaulted).Exception!.InnerExceptions.Single();
        Assert.IsType<ActivityForkPreviewIdempotencyConflictException>(failure);
    }

    [Fact]
    public async Task Sqlite_projection_counts_distinct_persisted_children_after_repeated_updates()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("counted", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = "counted", DefinitionId = "counted", TenantId = "tenant-a", ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) };
        var draft = Draft("counted-draft", "counted", "tenant-a", 1);
        db.ActivityDefinitions.Add(definition); db.ActivityDefinitionAuthoringStates.Add(authoring); db.ActivityDefinitionDrafts.Add(draft);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);
        await writer.WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [new(definition, authoring)], [draft], []));
        draft.Revision = 2;
        await writer.WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow.AddSeconds(1), [], [draft], []));
        var current = await db.ActivityDefinitionManagementProjections.SingleAsync(x => x.ValidToSequenceExclusive == long.MaxValue);
        Assert.Equal(1, current.DraftCount);
    }

    [Fact]
    public async Task Sqlite_projection_writer_rejects_existing_draft_with_wrong_definition_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-a"));
        db.ActivityDraftManagementProjections.Add(new ActivityDefinitionDraftManagementProjectionRevision
        {
            Id = "revision-draft", ResourceId = "draft", DefinitionId = "wrong-definition", TenantId = "tenant-a", ValidFromSequence = 1,
            ValidFromKey = "00000000000000000001", ValidToKey = "9223372036854775807", VisibilityKey = "tenant-a", SortKey = "draft", SearchText = "DRAFT",
            DraftId = "draft", Revision = 1, ProviderKey = "provider", ProviderSchemaVersion = "1", UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var draft = Draft("draft", "definition", "tenant-a", 2);
        await Assert.ThrowsAsync<InvalidOperationException>(() => new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [], [draft], [])));
        Assert.Equal("wrong-definition", (await db.ActivityDraftManagementProjections.SingleAsync()).DefinitionId);
    }

    [Fact]
    public async Task Sqlite_scoped_projection_writer_keeps_same_draft_id_in_other_tenant_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-a"));
        db.ActivityDraftManagementProjections.Add(new ActivityDefinitionDraftManagementProjectionRevision
        {
            Id = "hidden-draft-revision", ResourceId = "draft", DefinitionId = "definition", TenantId = "tenant-b", ValidFromSequence = 1,
            ValidFromKey = "00000000000000000001", ValidToSequenceExclusive = long.MaxValue, ValidToKey = "9223372036854775807", VisibilityKey = "tenant-b", SortKey = "draft", SearchText = "DRAFT",
            DraftId = "draft", Revision = 1, ProviderKey = "provider", ProviderSchemaVersion = "1", UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var writer = new EfActivityManagementProjectionWriter(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(1, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [], [Draft("draft", "definition", "tenant-a", 2)], [])));
        var rows = await db.ActivityDraftManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(["tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Equal("hidden-draft-revision", rows[1].Id);
        Assert.Equal(long.MaxValue, rows[1].ValidToSequenceExclusive);
    }

    [Fact]
    public async Task Sqlite_scoped_projection_writer_rejects_hidden_current_draft_with_wrong_definition_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-a"));
        db.ActivityDraftManagementProjections.Add(new ActivityDefinitionDraftManagementProjectionRevision
        {
            Id = "hidden-draft-definition-revision", ResourceId = "draft", DefinitionId = "wrong-definition", TenantId = "tenant-a", ValidFromSequence = 1,
            ValidFromKey = "00000000000000000001", ValidToSequenceExclusive = long.MaxValue, ValidToKey = "9223372036854775807", VisibilityKey = "tenant-a", SortKey = "draft", SearchText = "DRAFT",
            DraftId = "draft", Revision = 1, ProviderKey = "provider", ProviderSchemaVersion = "1", UpdatedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var writer = new EfActivityManagementProjectionWriter(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [], [Draft("draft", "definition", "tenant-a", 2)], [])));
        Assert.Single(await db.ActivityDraftManagementProjections.ToListAsync());
        Assert.Equal("wrong-definition", (await db.ActivityDraftManagementProjections.SingleAsync()).DefinitionId);
    }

    [Fact]
    public async Task Sqlite_projection_writer_keeps_same_version_id_in_other_tenant_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-a"));
        db.ActivityVersionManagementProjections.Add(new ActivityDefinitionVersionManagementProjectionRevision
        {
            Id = "revision-version", ResourceId = "version", DefinitionId = "definition", TenantId = "tenant-b", ValidFromSequence = 1,
            ValidFromKey = "00000000000000000001", ValidToKey = "9223372036854775807", VisibilityKey = "tenant-b", SortKey = "version", SearchText = "VERSION",
            DefinitionVersionId = "version", Version = "1.0.0", ProviderKey = "provider", ProviderSchemaVersion = "1", PublishedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var publication = Publication("definition", "version", "tenant-a", "provider");
        Assert.Equal(1, await new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [], [], [publication])));
        var rows = await db.ActivityVersionManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(["tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Equal("revision-version", rows[1].Id);
        Assert.Equal(long.MaxValue, rows[1].ValidToSequenceExclusive);
    }

    [Fact]
    public async Task Sqlite_scoped_projection_writer_keeps_same_version_id_in_other_tenant_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-a"));
        db.ActivityVersionManagementProjections.Add(new ActivityDefinitionVersionManagementProjectionRevision
        {
            Id = "hidden-version-revision", ResourceId = "version", DefinitionId = "definition", TenantId = "tenant-b", ValidFromSequence = 1,
            ValidFromKey = "00000000000000000001", ValidToSequenceExclusive = long.MaxValue, ValidToKey = "9223372036854775807", VisibilityKey = "tenant-b", SortKey = "version", SearchText = "VERSION",
            DefinitionVersionId = "version", Version = "1.0.0", ProviderKey = "provider", ProviderSchemaVersion = "1", PublishedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var writer = new EfActivityManagementProjectionWriter(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(1, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [], [], [Publication("definition", "version", "tenant-a", "provider")] )));
        var rows = await db.ActivityVersionManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(["tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Equal("hidden-version-revision", rows[1].Id);
        Assert.Equal(long.MaxValue, rows[1].ValidToSequenceExclusive);
    }

    [Fact]
    public async Task Sqlite_scoped_projection_writer_keeps_same_definition_id_in_other_tenant_scope()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionManagementProjections.Add(Projection("definition", "tenant-b"));
        await db.SaveChangesAsync();

        var definition = Definition("definition", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState
        {
            Id = "authoring-definition", DefinitionId = definition.Id, TenantId = definition.TenantId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        };
        var writer = new EfActivityManagementProjectionWriter(db, new TestAccess(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        Assert.Equal(1, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
        var rows = await db.ActivityDefinitionManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(["tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Equal(long.MaxValue, rows[1].ValidToSequenceExclusive);
    }

    [Fact]
    public async Task Sqlite_global_dependency_projection_excludes_tenant_owned_items()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitionVersionPublications.Add(Publication("global", "root", null, "provider"));
        var owner = new ActivityDefinitionReference("ActivityVersion", "global", "root", "1.0.0", TenantId: null);
        var globalDependency = new ActivityDefinitionReference("ActivityVersion", "g", "global-dep", "1.0.0", TenantId: null);
        var tenantDependency = new ActivityDefinitionReference("ActivityVersion", "t", "tenant-dep", "1.0.0", TenantId: "tenant-a");
        var otherOwner = new ActivityDefinitionReference("ActivityVersion", "other", "other-version", "1.0.0", TenantId: null);
        var transitiveDependency = new ActivityDefinitionReference("ActivityVersion", "transitive", "transitive-version", "1.0.0", TenantId: null);
        var items = new[]
        {
            new ActivityDependencyItem("global-edge", owner, globalDependency, new("occurrence", []), true, 1, []),
            new ActivityDependencyItem("tenant-edge", owner, tenantDependency, new("occurrence", []), true, 1, []),
            new ActivityDependencyItem("inbound-edge", otherOwner, owner, new("occurrence", []), true, 1, []),
            new ActivityDependencyItem("transitive-edge", globalDependency, transitiveDependency, new("transitive-occurrence", []), true, 2, [])
        };
        await db.SaveChangesAsync();
        var store = ProjectionStore(db);
        await store.RebuildCurrentAsync("rebuild", DateTimeOffset.UtcNow, items);
        var page = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, false, new HashSet<string>(["Versions"])), null, null, 0, 10));
        Assert.Equal("global-edge", Assert.Single(page.Items).RelationshipId);
        var inbound = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Inbound, false, new HashSet<string>(["Versions"])), null, null, 0, 10));
        Assert.Equal("inbound-edge", Assert.Single(inbound.Items).RelationshipId);
        var omitted = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, false, new HashSet<string>()), null, null, 0, 10));
        Assert.Empty(omitted.Items);
        var transitive = await store.ReadAsync(new ActivityDependencyProjectionReadRequest("root", new(ActivityDependencyDirection.Outbound, true, new HashSet<string>(["Versions"])), null, null, 0, 10));
        Assert.Equal(["global-edge", "transitive-edge"], transitive.Items.Select(x => x.RelationshipId));
    }

    [Fact]
    public async Task Sqlite_missing_upgrade_rows_return_null_without_deserialization_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfActivityDesignStores(db);
        Assert.Null(await ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityUpgradePlanStore)store).FindAsync("missing"));
        Assert.Null(await ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityUpgradeApplyReceiptStore)store).FindAsync("missing"));
    }

    [Fact]
    public async Task Sqlite_malformed_upgrade_material_is_a_design_persistence_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityUpgradePlans.Add(new ActivityUpgradePlanRecord { Id = "bad-plan", PlanId = "bad-plan", PlanJson = "null" });
        db.ActivityUpgradeApplyReceipts.Add(new ActivityUpgradeApplyReceiptRecord { Id = "bad-receipt", ReceiptId = "bad-receipt", PlanId = "bad-plan", IdempotencyKeyHash = "hash", ReceiptJson = "null" });
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db);
        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityUpgradePlanStore)store).FindAsync("bad-plan"));
        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => ((Elsa.Activities.Design.Persistence.Core.Stores.IActivityUpgradeApplyReceiptStore)store).FindAsync("bad-receipt"));
    }

    [Fact]
    public async Task Sqlite_empty_and_malformed_upgrade_material_is_a_design_persistence_failure()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityUpgradePlans.AddRange(
            new ActivityUpgradePlanRecord { Id = "empty", PlanId = "empty", PlanJson = "" },
            new ActivityUpgradePlanRecord { Id = "malformed", PlanId = "malformed", PlanJson = "{" });
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db);
        var plans = (Elsa.Activities.Design.Persistence.Core.Stores.IActivityUpgradePlanStore)store;
        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => plans.FindAsync("empty"));
        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => plans.FindAsync("malformed"));
    }

    [Fact]
    public async Task Sqlite_upgrade_receipt_create_and_reclaim_races_have_one_winner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var firstDb = new ActivitiesDesignSqliteDbContext(options);
        await firstDb.Database.EnsureCreatedAsync();
        var firstStore = new EfActivityDesignStores(firstDb);
        var now = DateTimeOffset.UtcNow;
        var receipt = new ActivityUpgradeApplyReceipt("receipt", "plan", "stage", "hash", "request", "tenant-a", "access", ActivityUpgradeApplyReceiptStatus.Preparing, now, now, 1, LeaseExpiresAt: now.AddMinutes(-1));
        Assert.True(await firstStore.TryCreateAsync(receipt));

        await using var secondDb = new ActivitiesDesignSqliteDbContext(options);
        var secondStore = new EfActivityDesignStores(secondDb);
        Assert.False(await secondStore.TryCreateAsync(receipt));
        var firstLoaded = await ((IActivityUpgradeApplyReceiptStore)firstStore).FindAsync(receipt.ReceiptId);
        var secondLoaded = await ((IActivityUpgradeApplyReceiptStore)secondStore).FindAsync(receipt.ReceiptId);
        var reclaimed = await firstStore.TryReclaimAsync(firstLoaded!, now, now.AddHours(1));
        var stale = await secondStore.TryReclaimAsync(secondLoaded!, now.AddMinutes(1), now.AddHours(2));
        Assert.NotNull(reclaimed);
        Assert.Null(stale);
        Assert.Equal(2, (await ((IActivityUpgradeApplyReceiptStore)firstStore).FindAsync(receipt.ReceiptId))!.Revision);
    }

    [Fact]
    public async Task Sqlite_upgrade_receipt_does_not_classify_an_unrelated_unique_failure_as_create_race()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        db.ActivityDefinitions.Add(Definition("existing", "tenant-a", "duplicate"));
        await db.SaveChangesAsync();
        db.ActivityDefinitions.Add(new ActivityDefinition { Id = "conflicting", TenantId = "tenant-a", ActivityTypeKey = "Acme.existing", Category = "Tests" });
        var receipt = new ActivityUpgradeApplyReceipt("receipt", "plan", "stage", "hash", "request", "tenant-a", "access", ActivityUpgradeApplyReceiptStatus.Preparing, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, 1);

        await Assert.ThrowsAsync<Elsa.Workflows.Design.Persistence.Core.Exceptions.DesignPersistenceException>(() => new EfActivityDesignStores(db).TryCreateAsync(receipt));
        Assert.Empty(db.ChangeTracker.Entries());
        Assert.Empty(await db.ActivityUpgradeApplyReceipts.ToListAsync());
    }

    [Fact]
    public void Ef_backend_registration_preserves_one_custom_atomic_writer()
    {
        Assert.True(typeof(IDesignAtomicWriter).IsDefined(typeof(ActivityDesignPersistenceReplacementContractAttribute), inherit: false));
        var services = new ServiceCollection();
        services.AddSingleton<IDesignAtomicWriter>(_ => throw new InvalidOperationException("custom"));
        var custom = services.Last();

        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });

        Assert.Same(custom, Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDesignAtomicWriter)));
    }

    [Fact]
    public void Ef_backend_registration_rejects_duplicate_or_other_backend_atomic_writers_before_mutation()
    {
        var duplicate = new ServiceCollection();
        duplicate.AddSingleton<IDesignAtomicWriter>(_ => throw new InvalidOperationException("first"));
        duplicate.AddScoped<IDesignAtomicWriter, EfDesignAtomicWrite>();
        var duplicateCount = duplicate.Count;
        Assert.Throws<InvalidOperationException>(() => duplicate.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        Assert.Equal(duplicateCount, duplicate.Count);

        var otherBackend = new ServiceCollection();
        otherBackend.AddSingleton<OtherAtomicWriterContract, OtherAtomicWriter>();
        var otherBackendCount = otherBackend.Count;
        Assert.Throws<InvalidOperationException>(() => otherBackend.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        Assert.Equal(otherBackendCount, otherBackend.Count);
    }

    [Fact]
    public void Ef_backend_registration_ignores_an_unrelated_same_named_contract()
    {
        var services = new ServiceCollection();
        services.AddSingleton<UnrelatedAtomicWriterTypes.IDesignAtomicWriter, UnrelatedAtomicWriterTypes.AtomicWriter>();

        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });

        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(UnrelatedAtomicWriterTypes.IDesignAtomicWriter));
        Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IDesignAtomicWriter));
    }

    [Fact]
    public void Ef_backend_registration_validates_provider_before_mutating_and_rejects_custom_ports()
    {
        var invalid = new ServiceCollection();
        Assert.ThrowsAny<ArgumentException>(() => invalid.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "not-a-provider" }));
        Assert.Empty(invalid);

        var custom = new ServiceCollection();
        custom.AddSingleton<IActivityDefinitionStore>(_ => throw new InvalidOperationException("custom"));
        var before = custom.Count;
        Assert.Throws<InvalidOperationException>(() => custom.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        Assert.Equal(before, custom.Count);
    }

    [Fact]
    public void Ef_backend_registration_owns_lookup_factories_identity_and_persistence_core()
    {
        var services = new ServiceCollection();
        services.AddSingleton<Elsa.Primitives.Contracts.ISystemClock>(new RegistrationClock());
        services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" });
        Assert.Contains(services, x => x.ServiceType == typeof(IActivityDefinitionLookup) && x.ImplementationType == typeof(Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionLookup));
        Assert.Contains(services, x => x.ServiceType == typeof(IActivityDefinitionFactory) && x.ImplementationType == typeof(Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionFactory));
        Assert.Contains(services, x => x.ServiceType == typeof(IActivityDefinitionVersionFactory) && x.ImplementationType == typeof(Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionVersionFactory));
        Assert.Contains(services, x => x.ServiceType == typeof(IIdentityGenerator) && x.ImplementationType == typeof(Elsa.Primitives.Identity.ShortIdentityGenerator));
        Assert.Contains(services, x => x.ServiceType == typeof(IPersistenceAccessContextAccessor));

        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionLookup>(scope.ServiceProvider.GetRequiredService<IActivityDefinitionLookup>());
        Assert.IsType<Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionFactory>(scope.ServiceProvider.GetRequiredService<IActivityDefinitionFactory>());
        Assert.IsType<Elsa.Activities.Design.Persistence.Core.Services.ActivityDefinitionVersionFactory>(scope.ServiceProvider.GetRequiredService<IActivityDefinitionVersionFactory>());
        Assert.IsType<Elsa.Primitives.Identity.ShortIdentityGenerator>(scope.ServiceProvider.GetRequiredService<IIdentityGenerator>());
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IPersistenceAccessContextAccessor>());
    }

    [Fact]
    public void Ef_backend_registration_rejects_custom_lookup_before_persistence_core_mutation()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IActivityDefinitionLookup>(_ => throw new InvalidOperationException("custom"));
        var before = services.Count;
        Assert.Throws<InvalidOperationException>(() => services.AddActivitiesDesignEntityFrameworkCore(new() { Provider = "Sqlite" }));
        Assert.Equal(before, services.Count);
    }

    [Fact]
    public async Task Sqlite_version_writes_require_existing_definition_owner()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var store = new EfActivityDesignStores(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Execute(new DesignOperationKey("missing-owner"), Version("missing", "v1", "tenant-a")));
        await Assert.ThrowsAsync<ArgumentException>(() => store.Execute(new DesignOperationKey("mismatched-owner"), Definition("d1", "tenant-a"), Version("d2", "v1", "tenant-a")));
        db.ActivityDefinitions.Add(Definition("d1", "tenant-a"));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.Execute(new DesignOperationKey("wrong-tenant-owner"), Version("d1", "v2", "tenant-b")));
        Assert.Empty(await db.ActivityDefinitionVersions.ToListAsync());
    }

    [Fact]
    public async Task Sqlserver_model_bounds_composite_index_keys_without_changing_sqlite_contract()
    {
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqlServerDbContext>().UseSqlServer("Server=(local);Database=activities_design_test").Options;
        await using var db = new ActivitiesDesignSqlServerDbContext(options);
        foreach (var index in db.Model.GetEntityTypes().SelectMany(x => x.GetIndexes()))
        {
            var indexedStrings = index.Properties.Where(x => x.ClrType == typeof(string)).ToArray();
            Assert.True(indexedStrings.Sum(x => x.GetMaxLength() ?? 450) <= 450, $"{index.DeclaringEntityType.Name} index exceeds SQL Server's 900-byte key budget");
        }
    }

    private static ActivityDefinition Definition(string id, string? tenant, string category = "Tests") =>
        new() { Id = id, TenantId = tenant, ActivityTypeKey = $"Acme.{id}", Category = category };

    private static async Task SeedForkSourceAsync(ActivitiesDesignSqliteDbContext db, ActivityContentAuthorityKind authority)
    {
        var sourceProvider = new ActivityProviderManifest("source-provider", "1", JsonDocument.Parse("{}").RootElement.Clone());
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = "source-authoring", DefinitionId = "source-definition", TenantId = "tenant-a",
            ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design
                ? WellKnownActivityContentAuthorities.Design : "source-provider"),
            HeadVersionId = "source-version"
        });
        db.ActivityDefinitionVersionPublications.Add(new ActivityDefinitionVersionPublication
        {
            Id = "publication-source-version", TenantId = "tenant-a", DefinitionVersionId = "source-version",
            DefinitionId = "source-definition", Version = "1.0.0", ActivityTypeKey = "Acme.Source",
            Contract = new("1", [], [], []), Provider = sourceProvider, TemplateId = "template",
            TemplateHash = "hash", SourceReferenceId = "source",
            ProviderFingerprint = ActivityProviderManifestFingerprint.Compute(sourceProvider),
            DirectDependencyCount = 0, ClosedTemplateCount = 0, RuntimeRequirements = [],
            PublishedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();
    }

    private static ActivityDefinitionVersion Version(string definitionId, string id, string tenant, string semVer = "1.0.0") =>
        new(semVer, definitionId) { Id = id, TenantId = tenant, ProviderKey = "provider", ProviderSchemaVersion = "1", ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "test", SourceId = id };

    private static ActivityDefinitionDraft Draft(string id, string definitionId, string tenant, long revision, string? sourceVersionId = null) =>
        new() { Id = id, DefinitionId = definitionId, TenantId = tenant, Revision = revision, SourceVersionId = sourceVersionId, State = new(new("1", [], [], []), new("provider", "1", JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>()) };

    private static ActivityDefinitionDraftLayout Layout(string draftId, string tenant, long revision) =>
        new() { Id = $"layout-{draftId}", DraftId = draftId, TenantId = tenant, Revision = revision, Records = [] };

    private static ActivityDefinitionVersionPublication Publication(string definitionId, string versionId, string? tenant, string provider) =>
        new() { Id = $"publication-{versionId}", TenantId = tenant, DefinitionVersionId = versionId, DefinitionId = definitionId, Version = "1.0.0", ActivityTypeKey = "Acme.Test", Contract = new("1", [], [], []), Provider = new(provider, "1", JsonDocument.Parse("{}").RootElement.Clone()), TemplateId = "template", TemplateHash = "hash", SourceReferenceId = "source", ProviderFingerprint = "fingerprint", DirectDependencyCount = 0, ClosedTemplateCount = 0, RuntimeRequirements = [], PublishedAt = DateTimeOffset.UtcNow };

    private static ActivityForkCandidate ForkCandidateMaterial(string id = "candidate", string sourceVersionForDraft = "source-version", string actor = "actor", string? publicCandidateId = null, DateTimeOffset? createdAt = null, string? requestFingerprint = null, string? materialSuffix = null)
    {
        var materialId = materialSuffix ?? id;
        var definition = Definition($"target-{materialId}", "tenant-a");
        var authoring = new ActivityDefinitionAuthoringState { Id = $"authoring-{materialId}", TenantId = "tenant-a", DefinitionId = definition.Id, ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design) };
        var draft = Draft($"draft-{materialId}", definition.Id, "tenant-a", 1, sourceVersionForDraft);
        var layout = Layout(draft.Id, "tenant-a", 1);
        var sourceProvider = new ActivityProviderManifest("source-provider", "1", JsonDocument.Parse("{}").RootElement.Clone());
        var sourceContract = new ActivityContract("1", [], [], []);
        var created = createdAt ?? DateTimeOffset.UtcNow;
        return new()
        {
            Id = id, TenantId = "tenant-a", CandidateId = publicCandidateId ?? $"public-{id}", PreviewIdempotencyKey = $"preview-{id}", RequestFingerprint = requestFingerprint ?? $"request-{id}", AccessBindingFingerprint = $"access-{id}", ActorId = actor, AuthorizationProfile = "profile",
            SourceDefinitionId = "source-definition", SourceVersionId = "source-version", SourceVersion = "1.0.0", SourceLifecycle = ActivityDefinitionVersionLifecycle.Active,
            SourceProviderFingerprint = ActivityProviderManifestFingerprint.Compute(sourceProvider), SourceContractFingerprint = ActivityForkMaterialFingerprint.Compute(sourceContract),
            TargetProviderFingerprint = ActivityProviderManifestFingerprint.Compute(draft.State.Provider), TargetContractFingerprint = ActivityForkMaterialFingerprint.Compute(draft.State.Contract),
            ReservedDefinition = definition, ReservedAuthoringState = authoring, ReservedDraft = draft, ReservedLayout = layout,
            ExpiresAt = created.AddMinutes(15), RetainUntil = created.AddDays(1), RetentionKey = ActivityForkCandidateIdentity.RetentionKey(created.AddDays(1)), CreatedAt = created, LastModifiedAt = created
        };
    }

    private static ApplyActivityForkCandidateRequest ApplyFork(ActivityForkCandidate candidate, string idempotencyKey) => new(
        candidate.Id, candidate.RequestFingerprint, candidate.AccessBindingFingerprint, candidate.ActorId, candidate.AuthorizationProfile, idempotencyKey,
        ActivityForkReceiptIdentity.Compute(candidate.TenantId, candidate.ActorId, idempotencyKey), candidate.CreatedAt.AddMinutes(1));

    private static ActivityDefinitionManagementProjectionRevision Projection(string id, string? tenant, ActivityContentAuthorityKind authority = ActivityContentAuthorityKind.Design, string? definitionId = null) =>
        new() { Id = $"projection-{id}", ResourceId = id, DefinitionId = definitionId ?? id, TenantId = tenant, ValidFromSequence = 1, ValidToSequenceExclusive = long.MaxValue, ValidFromKey = "00000000000000000001", ValidToKey = "9223372036854775807", VisibilityKey = tenant ?? "*", SortKey = id, SearchText = id, ActivityTypeKey = "Acme.Test", Category = "Tests", ContentAuthority = new(authority, authority == ActivityContentAuthorityKind.Design ? WellKnownActivityContentAuthorities.Design : "provider"), ContentAuthorityKind = authority, UpdatedAt = DateTimeOffset.UtcNow };

    private sealed class TestAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }

    private static EfActivityDesignStores ProjectionStore(ActivitiesDesignDbContext db) =>
        new(db, new TestAccess(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("test-projection-rebuild"))));

    private sealed class RegistrationClock : Elsa.Primitives.Contracts.ISystemClock
    {
        public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch;
    }

    private sealed class ForkCandidatePreReadBarrier : DbCommandInterceptor
    {
        private readonly TaskCompletionSource bothReads = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int reads;
        public bool Enabled { get; set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("elsa_activity_fork_candidates", StringComparison.OrdinalIgnoreCase) && Interlocked.Increment(ref reads) <= 2)
            {
                if (Volatile.Read(ref reads) == 2)
                    bothReads.TrySetResult();
                await bothReads.Task.WaitAsync(cancellationToken);
            }
            return await base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }
    }

    private sealed class TransactionStartBarrier : DbTransactionInterceptor
    {
        private int paused;

        public bool Enabled { get; set; }
        public Func<Task>? BeforeContinue { get; set; }

        public override async ValueTask<InterceptionResult<DbTransaction>> TransactionStartingAsync(
            DbConnection connection,
            TransactionStartingEventData eventData,
            InterceptionResult<DbTransaction> result,
            CancellationToken cancellationToken = default)
        {
            if (Enabled && Interlocked.Exchange(ref paused, 1) == 0)
                if (BeforeContinue is not null)
                    await BeforeContinue();
            return await base.TransactionStartingAsync(connection, eventData, result, cancellationToken);
        }
    }

    private sealed class SuppressConcurrencyFenceInterceptor(string tableName) : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Enabled && command.CommandText.Contains(tableName, StringComparison.OrdinalIgnoreCase) && command.CommandText.Contains("ConcurrencyToken", StringComparison.OrdinalIgnoreCase)
                ? ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(0))
                : ValueTask.FromResult(result);
    }

    [ActivityDesignPersistenceReplacementContract]
    private interface OtherAtomicWriterContract
    {
    }

    private sealed class OtherAtomicWriter : OtherAtomicWriterContract
    {
    }

    private static class UnrelatedAtomicWriterTypes
    {
        public interface IDesignAtomicWriter
        {
        }

        public sealed class AtomicWriter : IDesignAtomicWriter
        {
        }
    }

    private sealed class RecordingCommandInterceptor : DbCommandInterceptor
    {
        public List<string> Sql { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }

        public override ValueTask<InterceptionResult<object>> ScalarExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<object> result,
            CancellationToken cancellationToken = default)
        {
            Sql.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnDeleteInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Enabled && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("retention delete failure")
                : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Enabled && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                ? throw new InvalidOperationException("retention delete failure")
                : ValueTask.FromResult(result);
    }

    private sealed class ThrowOnConcurrencyInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Enabled && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                ? throw new DbUpdateConcurrencyException("retention concurrency failure")
                : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Enabled && command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
                ? throw new DbUpdateConcurrencyException("retention concurrency failure")
                : ValueTask.FromResult(result);
    }

    private sealed class ThrowOnProjectionWriterConcurrencyInterceptor : DbCommandInterceptor
    {
        public bool Enabled { get; set; }

        private static bool IsWrite(string commandText) => commandText.Contains("INSERT", StringComparison.OrdinalIgnoreCase) || commandText.Contains("UPDATE", StringComparison.OrdinalIgnoreCase);

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default) =>
            Enabled && IsWrite(command.CommandText)
                ? throw new DbUpdateConcurrencyException("projection writer concurrency failure")
                : ValueTask.FromResult(result);

        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<int> result,
            CancellationToken cancellationToken = default) =>
            Enabled && IsWrite(command.CommandText)
                ? throw new DbUpdateConcurrencyException("projection writer concurrency failure")
                : ValueTask.FromResult(result);
    }
}
