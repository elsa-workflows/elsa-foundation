using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Etl.Core;
using Elsa.Etl.Core.Runtime;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Tests.Support;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore;
using Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.DependencyInjection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Etl.Tests;

public sealed partial class EtlDefinitionStoreV2Tests
{
    [Fact]
    public async Task Historical_v1_design_hashes_are_refused_without_rewriting_their_rows()
    {
        var original = EtlV1CompatibilityFixture.Open();
        original.VerifyFiles();
        await using var database = await EtlDesignTestDatabase.CreateAsync(original.FilePath("published/etl-design.db"));
        await using var context = database.CreateContext();
        var before = await ArchivedEtlRows.ReadAsync(context.Database.GetDbConnection().DataSource);
        var store = Store(context);
        var scope = new EtlScope(null, "partition-test");
        var draft = await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindDraftAsync(scope, original.Manifest.DefinitionId));
        Assert.Equal("The ETL draft row does not match its scoped identity or content hash.", draft.Message);
        var published = await Assert.ThrowsAsync<InvalidDataException>(async () => await store.FindPublishedAsync(scope, original.Manifest.DefinitionId, original.Manifest.DefinitionVersion));
        Assert.Equal("The published ETL artifact row does not match its scoped identity or envelope hash.", published.Message);
        Assert.Equal(before, await ArchivedEtlRows.ReadAsync(context.Database.GetDbConnection().DataSource));
        original.VerifyFiles();
    }
}

public sealed class EtlArchivedFixtureRefusalTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Historical_native_and_runtime_material_refuse_their_precise_old_pins_without_row_changes(bool workflow)
    {
        var original = EtlV1CompatibilityFixture.Open();
        original.VerifyFiles();
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.S9OriginalRefusal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "original.db");
        File.Copy(original.FilePath(workflow ? "published/workflow.db" : "runtime/runtime.db"), database);
        try
        {
            var services = new ServiceCollection();
            services.AddLogging();
            if (workflow)
                services.AddPersistenceCore("partition-test").AddRuntimeArtifactsEntityFrameworkCore(new RuntimeArtifactsEntityFrameworkCoreOptions
                { Provider = "Sqlite", ConnectionString = $"Data Source={database};Pooling=False" });
            else
                new EtlRuntimeEntityFrameworkCoreFeature { ConnectionString = $"Data Source={database};Pooling=False" }.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var operation = provider.CreateAsyncScope();
            if (workflow) await operation.ServiceProvider.GetRequiredService<RuntimeDbContext>().Database.MigrateAsync();
            else await operation.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Database.MigrateAsync();
            var before = await ArchivedEtlRows.ReadAsync(database);
            var error = workflow
                ? await Assert.ThrowsAsync<InvalidDataException>(async () => await operation.ServiceProvider.GetRequiredService<IWorkflowExecutableStore>().FindAsync(original.Manifest.WorkflowArtifactId))
                : await Assert.ThrowsAsync<InvalidDataException>(async () => await operation.ServiceProvider.GetRequiredService<IEtlRuntimeStore>().FindRunAsync(new(original.Manifest.TenantId, original.Manifest.PersistencePartition), original.Manifest.RuntimeRunId));
            Assert.Equal(workflow ? "The persisted workflow executable payload is corrupt." : "The persisted ETL invocation is invalid or unsupported.", error.Message);
            var cause = Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Equal(workflow ? "fingerprint" : "invocation", cause.ParamName);
            Assert.StartsWith(workflow ? "Value conversion plan fingerprint does not match its pinned contents." : "The ETL invocation artifact hash does not match its pipeline.", cause.Message);
            Assert.Equal(before, await ArchivedEtlRows.ReadAsync(database));
        }
        finally { Directory.Delete(directory, recursive: true); original.VerifyFiles(); }
    }

    [Fact]
    public async Task Historical_continuous_invocation_hash_is_refused_without_rewriting_history()
    {
        var original = Path.Combine(EtlV1CompatibilityFixture.FindRepositoryRoot(), "extensions", "Etl", "tests", "Shared", "Fixtures", "Continuous1192", "runtime.db");
        const string originalSha = "7cb52ed573db7fffb21ffa803c57d27a13a2974c4bddb2e002275cce461b4f14";
        Assert.Equal(originalSha, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(original))));
        var directory = Path.Combine(Path.GetTempPath(), "Elsa.Etl.S9ContinuousRefusal", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var database = Path.Combine(directory, "original.db");
        File.Copy(original, database);
        try
        {
            var services = new ServiceCollection();
            new EtlRuntimeEntityFrameworkCoreFeature { ConnectionString = $"Data Source={database};Pooling=False" }.ConfigureServices(services);
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<EtlRuntimeDbContext>().Database.MigrateAsync();
            var before = await ArchivedEtlRows.ReadAsync(database);
            var error = await Assert.ThrowsAsync<InvalidDataException>(async () => await scope.ServiceProvider.GetRequiredService<IEtlConnectorRuntimeStore>()
                .FindConnectorRunAsync(new("tenant-fixture", "compaction-1192"), "compaction-1192"));
            Assert.Equal("The persisted ETL invocation is invalid or unsupported.", error.Message);
            var artifact = Assert.IsType<ArgumentException>(error.InnerException);
            Assert.Equal("artifact", artifact.ParamName);
            Assert.StartsWith("The ETL invocation carries an invalid or unsupported v2 artifact.", artifact.Message);
            Assert.Equal("The ETL invocation v2 artifact hash does not match its pipeline.", Assert.IsType<ArgumentException>(artifact.InnerException).Message);
            Assert.Equal(before, await ArchivedEtlRows.ReadAsync(database));
        }
        finally { Directory.Delete(directory, recursive: true); }
        Assert.Equal(originalSha, Convert.ToHexStringLower(SHA256.HashData(await File.ReadAllBytesAsync(original))));
    }
}

internal static class ArchivedEtlRows
{
    internal static async Task<string[]> ReadAsync(string database)
    {
        await using var connection = new SqliteConnection($"Data Source={database};Mode=ReadOnly;Pooling=False");
        await connection.OpenAsync();
        var tables = new List<string>();
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = "SELECT name FROM sqlite_master WHERE type='table' ORDER BY name";
            await using var rows = await command.ExecuteReaderAsync();
            while (await rows.ReadAsync()) tables.Add(rows.GetString(0));
        }
        var result = new List<string>();
        foreach (var table in tables)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT * FROM \"" + table.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
            await using var rows = await command.ExecuteReaderAsync();
            var values = new List<string>();
            while (await rows.ReadAsync())
            {
                var row = new object[rows.FieldCount];
                rows.GetValues(row);
                values.Add(JsonSerializer.Serialize(row, Elsa.Serialization.Core.JsonBaseline.Options));
            }
            result.AddRange(values.Order(StringComparer.Ordinal).Select(value => table + ":" + value));
        }
        return result.ToArray();
    }
}
