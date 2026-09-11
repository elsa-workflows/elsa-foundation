using Elsa.Persistence.EntityFramework;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Entities;
using Elsa.Secrets.Persistence.EntityFrameworkCore.Stores;
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

        Assert.Equal("Latin1_General_BIN2", entity.FindProperty(nameof(SecretRecord.TypeNameLookupKey))!.GetCollation());
        Assert.Equal("Latin1_General_BIN2", entity.FindProperty(nameof(SecretRecord.StoreNameLookupKey))!.GetCollation());
        Assert.Equal("Latin1_General_BIN2", entity.FindProperty(nameof(SecretRecord.ScopeLookupKey))!.GetCollation());
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
        var options = new DbContextOptionsBuilder<SecretsSqlServerDbContext>()
            .UseSqlServer(connectionString, sqlServer => sqlServer
                .MigrationsAssembly(typeof(SecretsSqlServerDbContext).Assembly.GetName().Name)
                .MigrationsHistoryTable(SecretsEfModule.HistoryTableName))
            .Options;
        await using var context = new SecretsSqlServerDbContext(options);
        var migrator = context.GetService<IMigrator>();
        await migrator.MigrateAsync("20260910210213_Initial");

        var repository = new EfSecretRepository(context);
        await repository.SaveAsync(Secret("tenant-a", "before.upgrade", "preserved", scope: "Finance"));
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
