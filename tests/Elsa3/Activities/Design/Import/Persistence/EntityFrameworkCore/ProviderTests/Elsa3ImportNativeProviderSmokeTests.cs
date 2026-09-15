using Elsa.Activities.Design.Persistence.Core.Entities;
using Elsa.Activities.Design.Persistence.EntityFrameworkCore;
using Elsa.Persistence.EntityFramework;
using Elsa.Workflows.Design.Persistence.EntityFrameworkCore;
using Elsa3.Activities.Design.Import.Models;
using Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Xunit;
using static Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.Tests.Support.ImportFixtures;

namespace Elsa3.Activities.Design.Import.Persistence.EntityFrameworkCore.ProviderTests;

[Collection(Elsa3ImportPostgreSqlFixture.CollectionName)]
public sealed class Elsa3ImportPostgreSqlSmokeTests(Elsa3ImportPostgreSqlFixture fixture)
{
    [SkippableFact]
    public async Task Native_model_crud_atomic_import_commit_and_rollback()
    {
        fixture.RequireAvailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await Elsa3ImportNativeSmoke.RunAsync(new ImportDatabase(
            interceptors => new Elsa3ImportPostgreSqlDbContext(Options<Elsa3ImportPostgreSqlDbContext>(connectionString, interceptors)),
            interceptors => new ActivitiesDesignPostgreSqlDbContext(Options<ActivitiesDesignPostgreSqlDbContext>(connectionString, interceptors)),
            interceptors => new WorkflowsDesignPostgreSqlDbContext(Options<WorkflowsDesignPostgreSqlDbContext>(connectionString, interceptors))),
            EfProviderNames.PostgreSql);
    }

    private static DbContextOptions<TContext> Options<TContext>(string connectionString, IInterceptor[] interceptors) where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseNpgsql(connectionString).AddInterceptors(interceptors).Options;
}

[Collection(Elsa3ImportSqlServerFixture.CollectionName)]
public sealed class Elsa3ImportSqlServerSmokeTests(Elsa3ImportSqlServerFixture fixture)
{
    [SkippableFact]
    public async Task Native_model_crud_atomic_import_commit_and_rollback()
    {
        fixture.RequireAvailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await Elsa3ImportNativeSmoke.RunAsync(new ImportDatabase(
            interceptors => new Elsa3ImportSqlServerDbContext(Options<Elsa3ImportSqlServerDbContext>(connectionString, interceptors)),
            interceptors => new ActivitiesDesignSqlServerDbContext(Options<ActivitiesDesignSqlServerDbContext>(connectionString, interceptors)),
            interceptors => new WorkflowsDesignSqlServerDbContext(Options<WorkflowsDesignSqlServerDbContext>(connectionString, interceptors))),
            EfProviderNames.SqlServer);
    }

    private static DbContextOptions<TContext> Options<TContext>(string connectionString, IInterceptor[] interceptors) where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseSqlServer(connectionString).AddInterceptors(interceptors).Options;
}

[Collection(Elsa3ImportMySqlFixture.CollectionName)]
public sealed class Elsa3ImportMySqlSmokeTests(Elsa3ImportMySqlFixture fixture)
{
    [SkippableFact]
    public async Task Native_model_crud_atomic_import_commit_and_rollback()
    {
        fixture.RequireAvailable();
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await Elsa3ImportNativeSmoke.RunAsync(new ImportDatabase(
            interceptors => new Elsa3ImportMySqlDbContext(Options<Elsa3ImportMySqlDbContext>(connectionString, interceptors)),
            interceptors => new ActivitiesDesignMySqlDbContext(Options<ActivitiesDesignMySqlDbContext>(connectionString, interceptors)),
            interceptors => new WorkflowsDesignMySqlDbContext(Options<WorkflowsDesignMySqlDbContext>(connectionString, interceptors))),
            EfProviderNames.MySql);
    }

    private static DbContextOptions<TContext> Options<TContext>(string connectionString, IInterceptor[] interceptors) where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>().UseMySQL(connectionString).AddInterceptors(interceptors).Options;
}

/// <summary>
/// One provider-neutral smoke per native provider: the three models build and bind to the provider, the
/// ledger round-trips opaque identities, one import commits across all three contexts, a later import reuses
/// what the first created, and failures before or at the commit leave no partial rows.
/// </summary>
internal static class Elsa3ImportNativeSmoke
{
    public static async Task RunAsync(ImportDatabase database, string expectedProvider)
    {
        await using var db = database;
        await using (var import = db.Import())
        await using (var activities = db.Activities())
        await using (var workflows = db.Workflows())
        {
            foreach (var context in new DbContext[] { import, activities, workflows })
                EfProviderGuard.Ensure(context, expectedProvider);
            Assert.Equal(
                [Elsa3ImportEfModule.CollectionTable, Elsa3ImportEfModule.DefinitionBindingTable, Elsa3ImportEfModule.ReceiptTable],
                import.Model.GetEntityTypes().Select(entity => entity.GetTableName()!).Order(StringComparer.Ordinal));
        }
        await db.CreateSchemaAsync();

        var access = MutableAccess.Tenant("tenant-a");
        var clock = new MutableTimeProvider(Now);

        // Ledger CRUD with identities that differ only by case, trailing space, and an embedded NUL: hashed keys
        // keep them distinct partitions on every collation.
        var store = db.OperationStore(access);
        var upper = new ReusableActivityImportAccessScope("tenant-a", "User-A");
        var folded = new ReusableActivityImportAccessScope("tenant-a", "user-a \0");
        var collection = new ReusableActivityImportCollectionHandle(
            "native-handle", upper, Now, Now.AddHours(1), 7, new ReusableActivityImportCollection("native-handle", [Workflow("a", "a-v1", 1, true, Leaf("root"))]));
        Assert.True(await store.TryCreateCollectionAsync(collection));
        Assert.False(await store.TryCreateCollectionAsync(collection));
        Assert.True(await store.TryCreateCollectionAsync(collection with { AccessScope = folded }));
        Assert.Equal(7, (await db.OperationStore(access).FindCollectionAsync("native-handle", upper))!.ContentLength);
        Assert.Equal(folded, (await db.OperationStore(access).FindCollectionAsync("native-handle", folded))!.AccessScope);
        Assert.Null(await store.FindCollectionAsync("NATIVE-HANDLE", upper));

        // One atomic import across the ledger, Activities Design, and Workflows Design.
        var scope = new ReusableActivityImportAccessScope("tenant-a", "user-a");
        var service = db.Service(access, clock);
        var upload = await service.UploadAsync(Json(
            Workflow("a", "a-v1", 1, true, Leaf("root-v1")),
            Workflow("a", "a-v2", 2, true, Leaf("root-v2")),
            Workflow("consumer", "consumer-v1", 1, false, Reference("consumer-to-a", "a-v1"))), null, scope);
        var planId = (await service.AnalyzeAsync(upload.CollectionHandle, 0, 10, scope)).PlanId;
        var applied = await service.ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "consumer-v1"], "native-first", scope);
        Assert.Equal(ReusableActivityImportReceiptStatus.Applied, applied.Status);
        var afterFirst = await db.CountAsync();
        Assert.Equal((1, 3, 1, 1, 1, 2, 2), (afterFirst.Receipts, afterFirst.Bindings, afterFirst.ActivityDefinitions, afterFirst.ActivityVersions, afterFirst.Authoring, afterFirst.WorkflowDefinitions, afterFirst.WorkflowVersions));

        // Restart: fresh contexts replay the durable receipt.
        Assert.Equal(ReusableActivityImportReceiptStatus.AlreadyImported,
            (await db.Service(access, clock).ApplyAsync(upload.CollectionHandle, planId, ["a-v1", "consumer-v1"], "native-first", scope)).Status);

        // Rollback: a failure after the Design writes of a later import leaves nothing partial.
        var failing = db.Service(access, clock, db.Command(access, clock, activitiesInterceptors: [new SaveFailureInterceptor<ActivityManagementProjectionSnapshot>()]));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await failing.ApplyAsync(upload.CollectionHandle, planId, ["a-v2"], "native-rolled-back", scope));
        Assert.Equal(afterFirst, await db.CountAsync());
        var lostCommit = db.Service(access, clock, db.Command(access, clock, importInterceptors: [new CommitFailureInterceptor(afterCommit: false)]));
        await Assert.ThrowsAsync<ReusableActivityImportPersistenceException>(async () =>
            await lostCommit.ApplyAsync(upload.CollectionHandle, planId, ["a-v2"], "native-rolled-back", scope));
        Assert.Equal(afterFirst, await db.CountAsync());

        // The later import reuses the definition created by the first, on this provider's stored precision.
        var later = await db.Service(access, clock).ApplyAsync(upload.CollectionHandle, planId, ["a-v2"], "native-rolled-back", scope);
        var v2 = Assert.Single(later.Sources);
        Assert.Equal(ReusableActivityImportResourceDisposition.Reused, v2.ActivityDefinitionDisposition);
        Assert.Equal(ReusableActivityImportResourceDisposition.Created, v2.ActivityVersionDisposition);
        await using var readback = db.Activities();
        Assert.Equal(v2.ActivityDefinitionVersionId, (await readback.ActivityDefinitionAuthoringStates.AsNoTracking().SingleAsync()).HeadVersionId);
        var afterLater = await db.CountAsync();
        Assert.Equal((2, 2, 1), (afterLater.Receipts, afterLater.ActivityVersions, afterLater.ActivityDefinitions));
    }
}
