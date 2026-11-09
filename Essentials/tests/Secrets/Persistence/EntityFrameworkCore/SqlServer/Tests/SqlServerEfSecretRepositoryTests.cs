using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Elsa.Secrets.Tests.Support;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.SqlServer.Tests;

[Collection(SqlServerContainerCollection.Name)]
public sealed class SqlServerEfSecretRepositoryTests(SqlServerContainerFixture fixture)
{
    [Fact]
    public void SqlServer_lookup_columns_use_deterministic_ordinal_collation()
    {
        var options = new DbContextOptionsBuilder<SecretsSqlServerDbContext>()
            .UseSqlServer("Server=localhost;Database=x;TrustServerCertificate=True")
            .Options;
        using var context = new SecretsSqlServerDbContext(options);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(SecretRecord))!;

        Assert.Equal(EfOrdinalCollation.SqlServer, entity.FindProperty(nameof(SecretRecord.TypeNameLookupKey))!.GetCollation());
        Assert.Equal(EfOrdinalCollation.SqlServer, entity.FindProperty(nameof(SecretRecord.StoreNameLookupKey))!.GetCollation());
        Assert.Equal(EfOrdinalCollation.SqlServer, entity.FindProperty(nameof(SecretRecord.ScopeLookupKey))!.GetCollation());
        // #1837 unified SQL Server on the 100 series; Payload is content, so it stays on the default.
        Assert.Null(entity.FindProperty(nameof(SecretRecord.Payload))!.GetCollation());
    }

    [Fact]
    public void UseSqlServer_sets_the_provider_history_table_when_the_engine_is_loaded()
    {
        var builder = new DbContextOptionsBuilder();
        EfRelationalProviderBinding.UseSqlServer(
            builder,
            "Server=localhost;Database=x;TrustServerCertificate=True",
            SecretsEfModule.HistoryTableName,
            typeof(SecretsSqlServerDbContext).Assembly.GetName().Name);

        var relational = builder.Options.Extensions.OfType<RelationalOptionsExtension>().SingleOrDefault()
                         ?? throw new InvalidOperationException("SqlServer binding did not install a relational extension.");
        Assert.Equal(SecretsEfModule.HistoryTableName, relational.MigrationsHistoryTableName);
        Assert.Contains("SqlServer", relational.GetType().Name, StringComparison.Ordinal);
    }

    [SkippableFact]
    public async Task Migrate_crud_revision_tenant_case_and_isolation_work_on_sql_server()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910210213_Initial");

        await using (var preStamp = CreatePreStampContext(connectionString))
            await PreStampSecretsModel.WriteAsync(preStamp, Secret("tenant-a", "before.upgrade", "preserved", scope: "Finance"));
        var repository = new EfSecretRepository(context);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqlServerDbContext.ExpectedProviderName);
        Assert.Equal(
            "before.upgrade",
            Assert.Single((await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: "FINANCE"))).Items).Name);

        var revisions = Assert.IsAssignableFrom<IRevisionAwareSecretRepository>(repository);
        Assert.True(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "alpha")));
        Assert.False(await repository.TryAddAsync(Secret("tenant-a", "payments.api", "duplicate")));
        Assert.True(await repository.TryAddAsync(Secret("TENANT-A", "payments.api", "other-tenant")));
        Assert.Equal("alpha", (await repository.FindAsync("tenant-a", "payments.api"))!.LatestActiveVersion!.Payload.Value);
        Assert.Equal("other-tenant", (await repository.FindAsync("TENANT-A", "payments.api"))!.LatestActiveVersion!.Payload.Value);

        var current = await revisions.FindWithRevisionAsync("tenant-a", "payments.api");
        current!.Secret.DisplayName = "updated";
        var saved = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Saved, saved.Status);
        var stale = await revisions.SaveWithRevisionAsync(current.Secret, current.Revision);
        Assert.Equal(SecretRevisionSaveStatus.Conflict, stale.Status);

        var longScope = new string('s', 100) + "München";
        await repository.SaveAsync(Secret("tenant-a", "long.lookup", "long", scope: longScope));
        Assert.Contains(
            (await repository.ListPageAsync(
                "tenant-a",
                new SecretRepositoryListRequest(scope: longScope.ToUpperInvariant()))).Items,
            secret => secret.Name == "long.lookup");
    }

    [SkippableFact]
    public async Task Projection_reindex_uses_translatable_keyset_pages_on_sql_server()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqlServerDbContext.ExpectedProviderName);

        const string legacyRuntimeKey = "\u019B";
        for (var index = 0; index < 101; index++)
        {
            var current = SecretDocument.FromSecret(Secret("tenant-a", $"keyset.{index:D3}", "value"));
            context.Secrets.Add((current with { TypeNameLookupKey = legacyRuntimeKey }).ToRecord());
        }

        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();

        Assert.Equal(101, await SecretsProjectionContract.ReindexAsync(context));
        await SecretsProjectionContract.EnsureCurrentAsync(context);
        var expectedTypeKey = SecretsSearchKeys.LookupKey(SecretTypeNames.Text);
        Assert.Equal(101, await context.Secrets.CountAsync(record => record.TypeNameLookupKey == expectedTypeKey));
    }

    [SkippableFact]
    public async Task A_deleted_name_is_free_while_the_key_admits_one_row_holding_the_name_on_sql_server()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqlServerDbContext.ExpectedProviderName);
        await using var bypass = CreateContext(connectionString);

        await SecretNameReuseScenario.RunAsync(context, bypass);
    }

    /// <summary>
    /// <see cref="SecretNameConstraints.MaximumKeyLength"/> is SQL Server's own bound on the row a delete writes: a secret
    /// whose tenant id, name and id fill it is created and deleted, and the engine refuses that row one character past it,
    /// which is the delete the bound keeps a create from leading to.
    /// </summary>
    [SkippableFact]
    public async Task A_secret_at_the_key_limit_is_created_and_deleted_and_sql_server_refuses_the_row_one_past_it()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqlServerDbContext.ExpectedProviderName);
        var repository = new EfSecretRepository(context);
        var revisions = (IRevisionAwareSecretRepository)repository;
        var tenantId = SecretKeyLimit.TenantId;
        var atLimit = Secret(tenantId, new string('a', SecretKeyLimit.NameLength), "value");
        SecretKeyLimit.AssertFillsKey(atLimit.Name, atLimit.Id);

        Assert.True(await repository.TryAddAsync(atLimit));
        var loaded = (await revisions.FindWithRevisionAsync(tenantId, atLimit.Name))!;
        loaded.Secret.Status = SecretStatus.Deleted;
        Assert.Equal(SecretRevisionSaveStatus.Saved, (await revisions.SaveWithRevisionAsync(loaded.Secret, loaded.Revision)).Status);
        Assert.Equal(atLimit.Id, (await context.Secrets.AsNoTracking().SingleAsync()).Tombstone);

        var pastLimit = Secret(tenantId, atLimit.Name + "a", "value");
        pastLimit.Status = SecretStatus.Deleted;
        await using var bypass = CreateContext(connectionString);
        bypass.Secrets.Add(SecretDocument.FromSecret(pastLimit).ToRecord());
        var refused = await Assert.ThrowsAsync<DbUpdateException>(() => bypass.SaveChangesAsync());
        Assert.Contains("maximum length of 900 bytes", refused.InnerException!.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// A secret created before <see cref="SecretNameConstraints.MaximumKeyLength"/> could have a tenant id and name of up
    /// to 450 characters. Once a host from before <c>FreeNameOnDelete</c> deletes such a secret in place, SQL Server
    /// refuses the row the reindex would move it to, so the reindex names the row for the operator to shorten or remove,
    /// and leaves it where it was.
    /// </summary>
    [SkippableFact]
    public async Task The_reindex_names_a_deleted_in_place_row_sql_server_cannot_move_under_its_id()
    {
        Skip.IfNot(fixture.IsAvailable, fixture.SkipReason ?? "Docker unavailable.");
        var connectionString = await fixture.CreateIsolatedDatabaseAsync();
        await using var context = CreateContext(connectionString);
        await EfDatabaseMigrator.ApplyAsync(context, SecretsSqlServerDbContext.ExpectedProviderName);
        var tenantId = SecretKeyLimit.TenantId;
        var preBound = Secret(tenantId, new string('a', SecretKeyLimit.NameLength + 6), "value");
        preBound.Status = SecretStatus.Deleted;
        await using (var olderHost = CreateContext(connectionString))
        {
            var deletedInPlace = SecretDocument.FromSecret(preBound).ToRecord();
            deletedInPlace.Tombstone = "";
            olderHost.Secrets.Add(deletedInPlace);
            await olderHost.SaveChangesAsync();
        }

        var refused = await Assert.ThrowsAsync<SecretsProjectionException>(() => SecretsProjectionContract.ReindexAsync(context));

        Assert.Equal((tenantId, preBound.Name), (refused.TenantId, refused.NormalizedName));
        Assert.Contains("Shorten this row's tenant id or name, or remove the row, by hand", refused.Message, StringComparison.Ordinal);
        Assert.Contains("maximum length of 900 bytes", refused.InnerException!.InnerException!.Message, StringComparison.Ordinal);
        var row = await context.Secrets.AsNoTracking().SingleAsync();
        Assert.Equal(("", "deleted"), (row.Tombstone, row.Status));
    }

    private static SecretsSqlServerDbContext CreateContext(string connectionString) => new(Options(connectionString).Options);

    /// <summary>A context with the model builds from before the schema-version stamp had, to write rows the way they did.</summary>
    private static SecretsSqlServerDbContext CreatePreStampContext(string connectionString) =>
        new(PreStampSecretsModel.Use(Options(connectionString)).Options);

    private static DbContextOptionsBuilder<SecretsSqlServerDbContext> Options(string connectionString) =>
        new DbContextOptionsBuilder<SecretsSqlServerDbContext>()
            .UseSqlServer(connectionString, sqlServer => sqlServer
                .MigrationsAssembly(typeof(SecretsSqlServerDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName));

    private static Secret Secret(string tenantId, string name, string value, string? scope = null) => new()
    {
        TenantId = tenantId,
        Name = name,
        DisplayName = name,
        TypeName = SecretTypeNames.Text,
        StoreName = SecretStoreNames.Encrypted,
        Scope = scope,
        Versions =
        [
            new SecretVersion
            {
                Version = 1,
                Status = SecretStatus.Active,
                Payload = SecretPayload.FromValue(value)
            }
        ]
    };
}
