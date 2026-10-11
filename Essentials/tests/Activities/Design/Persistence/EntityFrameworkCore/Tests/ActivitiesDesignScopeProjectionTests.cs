using Elsa.Activities.Design.Core.Models;
using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore.Stores;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Elsa.Activities.Design.Persistence.EntityFrameworkCore.Tests;

public sealed class ActivitiesDesignScopeProjectionTests
{
    [Fact]
    public async Task Sqlite_projection_writer_allows_same_resource_id_in_two_tenant_scopes()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definitionA = Definition("same-resource", "tenant-a");
        var definitionB = Definition("same-resource", "tenant-b");
        var authoringA = Authoring("authoring-a", definitionA);
        var authoringB = Authoring("authoring-b", definitionB);
        db.ActivityDefinitions.AddRange(definitionA, definitionB);
        db.ActivityDefinitionAuthoringStates.AddRange(authoringA, authoringB);
        await db.SaveChangesAsync();

        var sequence = await new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(
                DateTimeOffset.UtcNow,
                [new(definitionA, authoringA), new(definitionB, authoringB)],
                [],
                []));

        var rows = await db.ActivityDefinitionManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal(1, sequence);
        Assert.Equal(["tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Single(rows.Select(x => x.Id).Distinct());
        Assert.Equal(1, await db.ActivityManagementProjectionWatermarks.Select(x => x.Sequence).SingleAsync());
    }

    [Fact]
    public async Task Sqlite_projection_writer_rejects_same_scope_duplicate_resource_ids_before_marker()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definitionA = Definition("same-resource", "tenant-a");
        var definitionB = Definition("same-resource", "tenant-a");
        var authoringA = Authoring("authoring-a", definitionA);
        var authoringB = Authoring("authoring-b", definitionB);

        await Assert.ThrowsAsync<ArgumentException>(() => new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(
                DateTimeOffset.UtcNow,
                [new(definitionA, authoringA), new(definitionB, authoringB)],
                [],
                [])));

        Assert.Empty(await db.ActivityDefinitionManagementProjections.ToListAsync());
        Assert.Empty(await db.ActivityManagementProjectionWatermarks.ToListAsync());
        Assert.Empty(await db.ActivityManagementProjectionSnapshots.ToListAsync());
    }

    [Fact]
    public async Task Sqlite_projection_writer_roundtrips_a_450_code_unit_resource_id()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var id = new string('x', ActivitiesDesignDbContext.MaximumIdLength);
        var definition = Definition(id, "tenant-a");
        var authoring = Authoring("authoring-long", definition);
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        await db.SaveChangesAsync();

        await new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(
                DateTimeOffset.UtcNow,
                [new(definition, authoring)],
                [],
                []));

        var row = await db.ActivityDefinitionManagementProjections.SingleAsync();
        Assert.Equal(id, row.ResourceId);
        Assert.Equal(ActivitiesDesignDbContext.ComputeIdentityHash(id), db.Entry(row).Property<string>("ResourceIdIdentityHash").CurrentValue);
    }

    [Fact]
    public async Task Sqlite_projection_writer_keeps_global_and_tenant_resource_identities_separate()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var global = Definition("shared-resource", null);
        var tenant = Definition("shared-resource", "tenant-a");
        var globalAuthoring = Authoring("authoring-global", global);
        var tenantAuthoring = Authoring("authoring-tenant", tenant);
        db.ActivityDefinitions.AddRange(global, tenant);
        db.ActivityDefinitionAuthoringStates.AddRange(globalAuthoring, tenantAuthoring);
        await db.SaveChangesAsync();

        await new EfActivityManagementProjectionWriter(db).WriteAsync(
            new EfActivityManagementProjectionMutation(
                DateTimeOffset.UtcNow,
                [new(global, globalAuthoring), new(tenant, tenantAuthoring)],
                [],
                []));

        var rows = await db.ActivityDefinitionManagementProjections.AsNoTracking().OrderBy(x => x.TenantId).ToListAsync();
        Assert.Equal([null, "tenant-a"], rows.Select(x => x.TenantId));
        Assert.Single(rows.Select(x => x.Id).Distinct());
    }

    [Fact]
    public async Task Sqlite_projection_writer_preserves_temporal_order_for_a_scoped_resource()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definition = Definition("temporal-resource", "tenant-a");
        var authoring = Authoring("authoring-temporal", definition);
        db.ActivityDefinitions.Add(definition);
        db.ActivityDefinitionAuthoringStates.Add(authoring);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);

        Assert.Equal(1, await writer.WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow, [new(definition, authoring)], [], [])));
        definition.DisplayName = "updated";
        definition.LastModifiedAt = DateTimeOffset.UtcNow.AddSeconds(1);
        Assert.Equal(2, await writer.WriteAsync(new EfActivityManagementProjectionMutation(DateTimeOffset.UtcNow.AddSeconds(1), [new(definition, authoring)], [], [])));

        var rows = await db.ActivityDefinitionManagementProjections.AsNoTracking().OrderBy(x => x.ValidFromSequence).ToListAsync();
        Assert.Equal([1L, 2L], rows.Select(x => x.ValidFromSequence));
        Assert.Equal([2L, long.MaxValue], rows.Select(x => x.ValidToSequenceExclusive));
        Assert.Equal(2, rows.Select(x => x.Id).Distinct().Count());
        Assert.Equal("updated", rows[^1].DisplayName);
    }

    [Fact]
    public async Task Sqlite_projection_writer_allows_same_resource_id_in_sequential_tenant_checkpoints()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definitionA = Definition("sequential-resource", "tenant-a");
        var definitionB = Definition("sequential-resource", "tenant-b");
        var authoringA = Authoring("authoring-sequential-a", definitionA);
        var authoringB = Authoring("authoring-sequential-b", definitionB);
        db.ActivityDefinitions.AddRange(definitionA, definitionB);
        db.ActivityDefinitionAuthoringStates.AddRange(authoringA, authoringB);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);

        Assert.Equal(1, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [new(definitionA, authoringA)], [], [])));
        Assert.Equal(2, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow.AddSeconds(1), [new(definitionB, authoringB)], [], [])));

        definitionA.DisplayName = "tenant-a-updated";
        definitionA.LastModifiedAt = DateTimeOffset.UtcNow.AddSeconds(2);
        Assert.Equal(3, await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow.AddSeconds(2), [new(definitionA, authoringA)], [], [])));

        var rows = await db.ActivityDefinitionManagementProjections.AsNoTracking()
            .OrderBy(x => x.TenantId).ThenBy(x => x.ValidFromSequence).ToListAsync();
        Assert.Equal(3, rows.Count);
        Assert.Equal(["tenant-a", "tenant-a", "tenant-b"], rows.Select(x => x.TenantId));
        Assert.Equal([1L, 3L, 2L], rows.Select(x => x.ValidFromSequence));
        Assert.Equal([3L, long.MaxValue, long.MaxValue], rows.Select(x => x.ValidToSequenceExclusive));
        Assert.All(rows, row => Assert.Equal("sequential-resource", row.ResourceId));
        Assert.Equal(3, rows.Select(x => x.Id).Distinct().Count());
    }

    [Fact]
    public async Task Sqlite_projection_writer_rejects_current_revision_with_corrupted_target_scope_provenance()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<ActivitiesDesignSqliteDbContext>().UseSqlite(connection).Options;
        await using var db = new ActivitiesDesignSqliteDbContext(options);
        await db.Database.EnsureCreatedAsync();
        var definitionB = Definition("corruptible-resource", "tenant-b");
        var authoringB = Authoring("authoring-corruptible-b", definitionB);
        db.ActivityDefinitions.Add(definitionB);
        db.ActivityDefinitionAuthoringStates.Add(authoringB);
        await db.SaveChangesAsync();
        var writer = new EfActivityManagementProjectionWriter(db);
        await writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow, [new(definitionB, authoringB)], [], []));

        await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE elsa_activity_management_definitions SET TenantScopeKey = {ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a")}");
        db.ChangeTracker.Clear();

        var definitionA = Definition("corruptible-resource", "tenant-a");
        var authoringA = Authoring("authoring-corruptible-a", definitionA);
        db.ActivityDefinitions.Add(definitionA);
        db.ActivityDefinitionAuthoringStates.Add(authoringA);
        await db.SaveChangesAsync();

        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.WriteAsync(new EfActivityManagementProjectionMutation(
            DateTimeOffset.UtcNow.AddSeconds(1), [new(definitionA, authoringA)], [], [])));

        Assert.Equal(1, await db.ActivityDefinitionManagementProjections.CountAsync());
        Assert.Equal(1, await db.ActivityManagementProjectionWatermarks.Select(x => x.Sequence).SingleAsync());
        var unchanged = await db.ActivityDefinitionManagementProjections.SingleAsync();
        Assert.Equal("tenant-b", unchanged.TenantId);
        Assert.Equal(ActivitiesDesignDbContext.NormalizeTenantKey("tenant-a"), db.Entry(unchanged).Property<string>("TenantScopeKey").CurrentValue);
    }

    private static ActivityDefinition Definition(string id, string? tenantId) => new()
    {
        Id = id,
        TenantId = tenantId,
        ActivityTypeKey = $"type-{tenantId ?? "global"}",
        Category = "Tests",
        DisplayName = id,
        CreatedAt = DateTimeOffset.UtcNow,
        LastModifiedAt = DateTimeOffset.UtcNow
    };

    private static ActivityDefinitionAuthoringState Authoring(string id, ActivityDefinition definition) => new()
    {
        Id = id,
        DefinitionId = definition.Id,
        TenantId = definition.TenantId,
        ContentAuthority = new(ActivityContentAuthorityKind.Design, WellKnownActivityContentAuthorities.Design)
    };
}
