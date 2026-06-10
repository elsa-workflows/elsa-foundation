using System.Data.Common;
using Groundwork.Documents.Store;
using Groundwork.SqlServer.Documents;
using Groundwork.SqlServer.Materialization;
using Microsoft.Data.SqlClient;
using Testcontainers.MsSql;
using Xunit;

namespace Groundwork.RelationalProviders.Tests;

public sealed class SqlServerProviderTests : RelationalProviderContractTests, IAsyncLifetime
{
    private readonly MsSqlContainer container = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-CU18-ubuntu-22.04").Build();

    public async Task InitializeAsync() => await container.StartAsync();

    public async Task DisposeAsync() => await container.DisposeAsync();

    protected override Task<IRelationalProviderHarness> CreateHarnessAsync()
    {
        var connection = new SqlConnection(container.GetConnectionString());
        var manifest = RelationalTestManifests.MetadataManifest();
        var store = new SqlServerDocumentStore(connection, manifest);
        return Task.FromResult<IRelationalProviderHarness>(new SqlServerProviderHarness(connection, store, manifest));
    }

    private sealed class SqlServerProviderHarness(SqlConnection connection, IDocumentStore store, Groundwork.Core.Manifests.StorageManifest manifest)
        : IRelationalProviderHarness
    {
        public IDocumentStore Store { get; } = store;

        public Task MaterializeAsync() =>
            new SqlServerGroundworkMaterializer(connection).MaterializeAsync(manifest, RelationalTestManifests.SqlServerProvider);

        public async Task<long> CountSchemaHistoryRowsAsync()
        {
            await EnsureOpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT COUNT(*)
                FROM groundwork_schema_history
                WHERE manifest_id = @manifestId AND provider_name = @providerName;
                """;
            command.Parameters.AddWithValue("@manifestId", manifest.Identity.Value);
            command.Parameters.AddWithValue("@providerName", RelationalTestManifests.SqlServerProvider.Name);
            return Convert.ToInt64(await command.ExecuteScalarAsync());
        }

        public async ValueTask DisposeAsync() => await connection.DisposeAsync();

        private async Task EnsureOpenAsync()
        {
            if (connection.State != System.Data.ConnectionState.Open)
                await connection.OpenAsync();
        }
    }
}
