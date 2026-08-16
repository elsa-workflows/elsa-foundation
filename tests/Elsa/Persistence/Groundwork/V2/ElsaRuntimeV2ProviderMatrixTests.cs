using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;
using Groundwork.Sqlite;
using Groundwork.Store;
using Xunit;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class ElsaRuntimeV2ProviderMatrixTests
{
    [Fact]
    public void Sqlite_applies_a_fresh_catalog_for_every_runtime_unit()
    {
        var catalogName = $"runtime-v2-{Guid.NewGuid():N}";
        using var connection = new SqliteProviderFactory().Create(
            $"Data Source=file:{catalogName};Mode=Memory;Cache=Shared");

        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);

        Assert.All(ElsaRuntimeV2StorageManifest.CreateUnits(), unit =>
        {
            var session = connection.OpenSession(unit, StorageAccess.Scoped(new StorageScope("tenant-a")));
            Assert.Equal(unit.Id, session.Unit.Id);
        });
    }

    [Theory]
    [InlineData("Groundwork.PostgreSql", "Groundwork.PostgreSql.PostgreSqlProviderFactory", "GROUNDWORK_POSTGRES_CONNECTION_STRING")]
    [InlineData("Groundwork.SqlServer", "Groundwork.SqlServer.SqlServerProviderFactory", "GROUNDWORK_SQLSERVER_CONNECTION_STRING")]
    [InlineData("Groundwork.MongoDb", "Groundwork.MongoDb.MongoDbProviderFactory", "GROUNDWORK_MONGODB_CONNECTION_STRING")]
    public void Optional_live_provider_applies_a_fresh_catalog_when_configured(
        string assemblyName,
        string factoryTypeName,
        string connectionStringVariable)
    {
        var connectionString = Environment.GetEnvironmentVariable(connectionStringVariable);
        if (string.IsNullOrWhiteSpace(connectionString))
            return;

        var assembly = System.Reflection.Assembly.Load(assemblyName);
        var factoryType = assembly.GetType(factoryTypeName, throwOnError: true)!;
        var factory = (IStorageProviderFactory)Activator.CreateInstance(factoryType)!;
        var routedConnectionString = AddFreshCatalogRouting(connectionString, assemblyName);
        using var connection = factory.Create(routedConnectionString);

        foreach (var unit in ElsaRuntimeV2StorageManifest.CreateUnits())
            connection.Schema.Apply(unit);
    }

    private static string AddFreshCatalogRouting(string connectionString, string provider)
    {
        var catalog = $"elsa_runtime_v2_{Guid.NewGuid():N}";
        return provider switch
        {
            "Groundwork.PostgreSql" => $"{connectionString};Database={catalog}",
            "Groundwork.SqlServer" => $"{connectionString};Initial Catalog={catalog}",
            "Groundwork.MongoDb" => $"{connectionString.TrimEnd('/')}/{catalog}",
            _ => connectionString
        };
    }
}
