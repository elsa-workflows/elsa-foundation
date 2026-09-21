using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.Core.Filters;
using Elsa.Activities.Design.Persistence.Core.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Workflows.Design.Persistence.Core.Exceptions;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignScopeStoreRouteTests
{
    [Fact]
    public async Task Sqlite_point_routes_preserve_scope_and_fail_closed_on_visible_duplicate_ids()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        db.ActivityDefinitions.AddRange(
            Definition("shared", "tenant-a"),
            Definition("shared", null));
        db.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion("1.0.0", "shared")
        {
            Id = "shared-version", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
            ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "test", SourceId = "shared-source"
        });
        await db.SaveChangesAsync();

        var tenantA = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.GetAsync("shared"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.FindAsync(new ActivityDefinitionFilter { Id = "shared" }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tenantA.FindByIdOrActivityTypeKeyAsync("shared", "unused"));

        var tenantB = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));
        Assert.Null((await tenantB.GetAsync("shared")).TenantId);
        var version = await ((IActivityDefinitionVersionStore)tenantA).GetWithDefinitionAsync("shared-version");
        Assert.Equal("tenant-a", version.Definition!.TenantId);

        var acrossScopes = new EfActivityDesignStores(db, Access(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("scope-route-test"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.GetAsync("shared"));
    }

    [Fact]
    public async Task Sqlite_reference_routes_round_trip_full_450_character_ids()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var definitionId = new string('d', ActivitiesDesignDbContext.MaximumIdLength);
        db.ActivityDefinitions.Add(Definition(definitionId, "tenant-a"));
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = "authoring-450", TenantId = "tenant-a", DefinitionId = definitionId,
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        });
        db.ActivityDefinitionVersions.Add(new ActivityDefinitionVersion("1.0.0", definitionId)
        {
            Id = "version-450", TenantId = "tenant-a", ProviderKey = "provider", ProviderSchemaVersion = "1",
            ConsumerKey = "consumer", ConsumerSchemaVersion = "1", SourceKind = "test", SourceId = "source-450"
        });
        await db.SaveChangesAsync();

        var store = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var authoring = await ((IActivityDefinitionAuthoringStore)store).FindAsync(definitionId);
        var version = await ((IActivityDefinitionVersionStore)store).GetAsync("version-450");

        Assert.Equal(definitionId, authoring!.DefinitionId);
        Assert.Equal(definitionId, version.DefinitionId);
        Assert.Equal(definitionId, (await store.GetAsync(definitionId)).Id);
    }

    [Fact]
    public async Task Sqlite_scoped_reads_reject_rows_whose_raw_and_physical_owners_disagree()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        db.ActivityDefinitions.AddRange(
            CorruptibleDefinition("misplaced-tenant", "tenant-a"),
            CorruptibleDefinition("misplaced-global", null),
            CorruptibleDefinition("owned-tenant", "tenant-a"),
            CorruptibleDefinition("owned-global", null));
        db.ActivityUpgradePlans.AddRange(
            new ActivityUpgradePlanRecord { Id = "misplaced-plan-tenant", PlanId = "misplaced-plan-tenant", TenantId = "tenant-a", PlanJson = "{}" },
            new ActivityUpgradePlanRecord { Id = "misplaced-plan-global", PlanId = "misplaced-plan-global", TenantId = null, PlanJson = "{}" });
        db.ActivityUpgradeApplyReceipts.AddRange(
            new ActivityUpgradeApplyReceiptRecord { Id = "misplaced-receipt-tenant", ReceiptId = "misplaced-receipt-tenant", PlanId = "plan", IdempotencyKeyHash = "tenant", TenantId = "tenant-a", ReceiptJson = "{}" },
            new ActivityUpgradeApplyReceiptRecord { Id = "misplaced-receipt-global", ReceiptId = "misplaced-receipt-global", PlanId = "plan", IdempotencyKeyHash = "global", TenantId = null, ReceiptJson = "{}" });
        await db.SaveChangesAsync();

        var tenantKey = await db.ActivityDefinitions.Where(x => x.Id == "owned-tenant")
            .Select(x => EF.Property<string>(x, "TenantScopeKey")).SingleAsync();
        var globalKey = await db.ActivityDefinitions.Where(x => x.Id == "owned-global")
            .Select(x => EF.Property<string>(x, "TenantScopeKey")).SingleAsync();
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_definitions SET TenantScopeKey = {0} WHERE Id = 'misplaced-tenant'", globalKey);
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_definitions SET TenantScopeKey = {0} WHERE Id = 'misplaced-global'", tenantKey);
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_upgrade_plans SET TenantScopeKey = {0} WHERE Id = 'misplaced-plan-tenant'", globalKey);
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_upgrade_plans SET TenantScopeKey = {0} WHERE Id = 'misplaced-plan-global'", tenantKey);
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_upgrade_apply_receipts SET TenantScopeKey = {0} WHERE Id = 'misplaced-receipt-tenant'", globalKey);
        await db.Database.ExecuteSqlRawAsync("UPDATE elsa_activity_upgrade_apply_receipts SET TenantScopeKey = {0} WHERE Id = 'misplaced-receipt-global'", tenantKey);
        db.ChangeTracker.Clear();

        var scoped = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var global = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));
        Assert.Equal("tenant-a", (await scoped.GetAsync("owned-tenant")).TenantId);
        Assert.Null((await global.GetAsync("owned-global")).TenantId);
        foreach (var id in new[] { "misplaced-tenant", "misplaced-global" })
        {
            Assert.Null(await scoped.FindAsync(new ActivityDefinitionFilter { Id = id }));
            Assert.Null(await global.FindAsync(new ActivityDefinitionFilter { Id = id }));
        }
        foreach (var id in new[] { "misplaced-plan-tenant", "misplaced-plan-global" })
        {
            Assert.Null(await scoped.FindAsync(id));
            Assert.Null(await global.FindAsync(id));
        }
        foreach (var id in new[] { "misplaced-receipt-tenant", "misplaced-receipt-global" })
        {
            Assert.Null(await ((IActivityUpgradeApplyReceiptStore)scoped).FindAsync(id));
            Assert.Null(await ((IActivityUpgradeApplyReceiptStore)global).FindAsync(id));
        }
    }

    [Fact]
    public async Task Sqlite_recommendation_join_keeps_global_and_tenant_rows_distinct_when_ids_overlap()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        foreach (var tenant in new string?[] { null, "tenant-a" })
        {
            db.ActivityDefinitions.Add(new ActivityDefinition { Id = "shared-definition", TenantId = tenant, ActivityTypeKey = "Acme.Shared", Category = "Tests" });
            db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
            {
                Id = "shared-authoring", TenantId = tenant, DefinitionId = "shared-definition",
                RecommendedVersionId = "shared-version",
                ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
            });
            db.ActivityDefinitionVersionPublications.Add(Publication("shared-definition", "shared-version", tenant));
        }
        await db.SaveChangesAsync();

        var picker = (IRecommendedActivityDefinitionPickerStore)new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var page = await picker.ReadAsync("tenant-a", 0, 10);

        Assert.Equal([null, "tenant-a"], page.Items.Select(x => x.Definition.TenantId));
        Assert.All(page.Items, item => Assert.Equal(item.Definition.TenantId, item.Version.TenantId));
    }

    [Fact]
    public async Task Sqlite_projection_integrity_keyset_and_offset_pages_keep_case_distinct_ids_across_batch_boundaries()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var ids = Enumerable.Range(0, 300)
            .SelectMany(index => new[] { $"resource-{index:D4}", $"RESOURCE-{index:D4}" })
            .ToArray();
        db.ActivityDefinitionManagementProjections.AddRange(ids.Select(id => new ActivityDefinitionManagementProjectionRevision
        {
            Id = $"projection-{id}", ResourceId = id, DefinitionId = id, TenantId = "tenant-a",
            ValidFromSequence = 1, ValidToSequenceExclusive = long.MaxValue,
            ValidFromKey = "00000000000000000001", ValidToKey = "9223372036854775807",
            VisibilityKey = "tenant-a", SortKey = "same-sort-key", SearchText = id,
            ActivityTypeKey = "Acme.Test", Category = "Tests",
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design),
            ContentAuthorityKind = ActivityContentAuthorityKind.Design, UpdatedAt = DateTimeOffset.UnixEpoch
        }));
        db.ActivityManagementProjectionWatermarks.Add(new ActivityManagementProjectionWatermark
        {
            Id = ActivityManagementProjectionWatermark.CurrentId, Sequence = 1,
            RetainedFromSequence = 1, AdvancedAt = DateTimeOffset.UnixEpoch
        });
        db.ActivityManagementProjectionSnapshots.Add(new ActivityManagementProjectionSnapshot
        {
            Id = "00000000000000000001", Sequence = 1, AsOf = DateTimeOffset.UnixEpoch
        });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var store = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var first = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 0, 500));
        var next = await store.ReadDefinitionsAsync(new ActivityManagementProjectionPageQuery("tenant-a", 1, 500, 100));
        var expected = ids.OrderBy(id => id, StringComparer.Ordinal).ToArray();
        Assert.Equal(ids.Length, first.TotalCount);
        Assert.Equal(ids.Length, next.TotalCount);
        Assert.Equal(expected, first.Items.Concat(next.Items).Select(x => x.ResourceId));
        Assert.Equal(500, first.NextOffset);
        Assert.Null(next.NextOffset);
    }

    [Fact]
    public async Task Sqlite_validation_fence_and_duplicate_insert_are_atomic_per_scope()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        db.ActivityDefinitionAuthoringStates.Add(new ActivityDefinitionAuthoringState
        {
            Id = "authoring", TenantId = "tenant-a", DefinitionId = "definition",
            ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
        });
        db.ActivityDefinitionDrafts.Add(Draft("draft", "definition", "tenant-a", 1));
        await db.SaveChangesAsync();

        var store = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var validation = new ActivityDraftValidationState { Id = "validation", DraftId = "draft", TenantId = "tenant-a", Revision = 1, Diagnostics = [] };
        await store.ExecuteAsync(validation);
        var tokenBeforeDuplicate = await db.ActivityDefinitionDrafts
            .Select(x => EF.Property<byte[]>(x, "ConcurrencyToken"))
            .SingleAsync();
        db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<DesignPersistenceException>(() => store.ExecuteAsync(new ActivityDraftValidationState
        {
            Id = "validation", DraftId = "draft", TenantId = "tenant-a", Revision = 1, Diagnostics = []
        }));

        db.ChangeTracker.Clear();
        Assert.Single(await db.ActivityDraftValidations.AsNoTracking().ToListAsync());
        var tokenAfterDuplicate = await db.ActivityDefinitionDrafts
            .Select(x => EF.Property<byte[]>(x, "ConcurrencyToken"))
            .SingleAsync();
        Assert.Equal(tokenBeforeDuplicate, tokenAfterDuplicate);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => store.ExecuteAsync(new ActivityDraftValidationState
        {
            Id = "stale", DraftId = "draft", TenantId = "tenant-a", Revision = 0, Diagnostics = []
        }));
    }

    [Fact]
    public async Task Sqlite_availability_settings_are_partitioned_by_persistence_scope()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        var tenantA = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var tenantB = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-b"))));
        var scope = ActivityAvailabilitySettings.HostDefaultScope;

        await tenantA.SaveAsync(new ActivityAvailabilitySettings { Scope = scope, Mode = ActivityAvailabilityManagementMode.AllExcept });
        Assert.Null(await tenantB.LoadAsync(scope));

        await tenantB.SaveAsync(new ActivityAvailabilitySettings { Scope = scope, Mode = ActivityAvailabilityManagementMode.Only });
        Assert.Equal(ActivityAvailabilityManagementMode.AllExcept, (await tenantA.LoadAsync(scope))!.Mode);
        Assert.Equal(ActivityAvailabilityManagementMode.Only, (await tenantB.LoadAsync(scope))!.Mode);

        var acrossScopes = new EfActivityDesignStores(db, Access(PersistenceAccessContext.PrivilegedAcrossScopes(new PersistenceAccessPurpose("availability-test"))));
        await Assert.ThrowsAsync<InvalidOperationException>(() => acrossScopes.LoadAsync(scope));
    }

    [Fact]
    public async Task Sqlite_substring_search_is_refused_above_the_catalog_bound()
    {
        await using var connection = await OpenDatabaseAsync();
        await using var db = CreateContext(connection);
        db.ActivityDefinitions.AddRange(Enumerable.Range(0, EfActivityDesignStores.MaximumSearchCatalogRows).Select(index => CorruptibleDefinition($"definition-{index}", "tenant-a")));
        await db.SaveChangesAsync();
        var store = new EfActivityDesignStores(db, Access(PersistenceAccessContext.Scoped(new PersistenceScope("tenant-a"))));
        var search = new ActivityDefinitionFilter { SearchTerm = "definition-9999" };

        Assert.Single(await store.ListAsync(search));

        db.ActivityDefinitions.Add(CorruptibleDefinition("definition-overflow", "tenant-a"));
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ListAsync(search));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.FindAsync(search));
        Assert.Single(await store.ListAsync(new ActivityDefinitionFilter { Id = "definition-overflow" }));
    }

    private static async Task<SqliteConnection> OpenDatabaseAsync()
    {
        var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        await using var setup = CreateContext(connection);
        await setup.Database.EnsureCreatedAsync();
        return connection;
    }

    private static ActivitiesDesignSqliteDbContext CreateContext(SqliteConnection connection) =>
        new(new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options);

    private static IPersistenceAccessContextAccessor Access(PersistenceAccessContext context) => new TestAccess(context);

    private static ActivityDefinition Definition(string id, string? tenant) =>
        new() { Id = id, TenantId = tenant, ActivityTypeKey = id == "shared" ? "Acme.Shared" : "Acme.Test", Category = "Tests" };

    private static ActivityDefinition CorruptibleDefinition(string id, string? tenant) =>
        new() { Id = id, TenantId = tenant, ActivityTypeKey = id, Category = "Tests" };

    private static ActivityDefinitionDraft Draft(string id, string definitionId, string tenant, long revision) =>
        new()
        {
            Id = id, DefinitionId = definitionId, TenantId = tenant, Revision = revision,
            State = new(new("1", [], [], []), new("provider", "1", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()), new Dictionary<string, string>())
        };

    private static ActivityDefinitionVersionPublication Publication(string definitionId, string versionId, string? tenant) =>
        new()
        {
            Id = $"publication-{tenant ?? "global"}", TenantId = tenant, DefinitionVersionId = versionId, DefinitionId = definitionId,
            Version = "1.0.0", ActivityTypeKey = "Acme.Shared", Contract = new("1", [], [], []),
            Provider = new("provider", "1", System.Text.Json.JsonDocument.Parse("{}").RootElement.Clone()),
            TemplateId = "template", TemplateHash = "hash", SourceReferenceId = "source", ProviderFingerprint = "fingerprint",
            DirectDependencyCount = 0, ClosedTemplateCount = 0, RuntimeRequirements = [], PublishedAt = DateTimeOffset.UtcNow
        };

    private sealed class TestAccess(PersistenceAccessContext current) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = current;
    }
}
