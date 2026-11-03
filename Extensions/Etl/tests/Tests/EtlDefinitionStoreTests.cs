using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>The design-store contract on SQLite, with the cases only SQLite can stage.</summary>
public sealed class EtlDefinitionStoreTests : EtlDefinitionStoreContract
{
    protected override async Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => await EtlDesignTestDatabase.CreateAsync();

    [Fact]
    public async Task Unrelated_publication_storage_failure_remains_visible_and_rolls_back_the_version()
    {
        await using var database = await EtlDesignTestDatabase.CreateAsync();
        await using var context = database.CreateContext();
        var store = Store(context);
        var scope = new EtlScope("tenant-a", "partition-a");
        var pipeline = EtlTestPipelines.Create();
        var created = await store.CreateDraftAsync(scope, "orders", pipeline);
        var artifact = Compile("orders", 1, pipeline);
        await context.Database.ExecuteSqlRawAsync("""
            CREATE TRIGGER refuse_fixture_publication BEFORE INSERT ON elsa_etl_published_artifacts
            BEGIN SELECT RAISE(ABORT, 'fixture storage refusal'); END;
            """);

        await Assert.ThrowsAsync<DbUpdateException>(async () => await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact));
        Assert.Equal(0, (await store.FindDraftAsync(scope, "orders"))!.LastPublishedVersion);
        Assert.Null(await store.FindPublishedAsync(scope, "orders", 1));
        await context.Database.ExecuteSqlRawAsync("DROP TRIGGER refuse_fixture_publication");
        Assert.Equal(EtlPublicationStatus.Published,
            (await store.PublishAsync(scope, "orders", created.Draft!.Revision, artifact)).Status);
    }
}
