using Elsa.Persistence.Groundwork.Runtime;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.Groundwork.V2.Tests;

public sealed class ElsaRuntimeV2ProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public void Applies_all_runtime_units_and_opens_a_scoped_session_per_unit(string providerName)
    {
        var connectionString = providerName == "sqlite"
            ? $"Data Source=file:runtime-v2-{Guid.NewGuid():N};Mode=Memory;Cache=Shared"
            : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} provider proof.");

        using var connection = CreateConnection(providerName, connectionString!);
        var units = FreshPhysicalUnits();
        Assert.Equal(27, units.Count);
        Assert.Equal(units.Count, units.Select(unit => unit.Name).Distinct(StringComparer.Ordinal).Count());

        foreach (var unit in units)
            connection.Schema.Apply(unit);

        Assert.All(units, unit =>
        {
            var session = connection.OpenSession(
                unit,
                StorageAccess.Scoped(new StorageScope($"runtime-v2-{providerName}")));
            Assert.Equal(unit.Id, session.Unit.Id);
            Assert.Equal(unit.Name, session.Unit.Name);
        });
    }

    private static IReadOnlyList<StorageUnit> FreshPhysicalUnits()
    {
        var runId = Guid.NewGuid().ToString("N");
        return ElsaRuntimeV2StorageManifest.CreateUnits()
            .Select((unit, index) => unit with { Name = $"gwv2_{runId}_{index}" })
            .ToArray();
    }

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) =>
        providerName switch
        {
            "sqlite" => new SqliteProviderFactory().Create(connectionString),
            "postgresql" => new PostgreSqlProviderFactory().Create(connectionString),
            "sqlserver" => new SqlServerProviderFactory().Create(connectionString),
            "mongodb" => new MongoProviderFactory().Create(connectionString),
            _ => throw new ArgumentOutOfRangeException(nameof(providerName), providerName, null)
        };

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

}
