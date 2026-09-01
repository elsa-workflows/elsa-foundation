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
        var sqlitePath = providerName == "sqlite"
            ? Path.Combine(Path.GetTempPath(), $"elsa-runtime-v2-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = providerName == "sqlite"
            ? $"Data Source={sqlitePath}"
            : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        Skip.If(
            string.IsNullOrWhiteSpace(connectionString),
            $"Set {EnvironmentVariable(providerName)} to run the {providerName} provider proof.");

        try
        {
            using var connection = CreateConnection(providerName, connectionString!);
            var units = providerName == "sqlite"
                ? ElsaRuntimeV2StorageManifest.CreateUnits()
                : FreshPhysicalUnits();
            Assert.Equal(29, units.Count);
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
                var outcome = session.Upsert(
                    MatrixValues(unit, providerName),
                    WriteOptions.Unconditional);
                Assert.True(outcome.Succeeded, $"Upsert failed for '{unit.Id.Value}' on {providerName}: {outcome.Status}.");
            });
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                    if (File.Exists(path))
                        File.Delete(path);
            }
        }
    }

    [Fact]
    public void Fresh_physical_units_have_unique_subject_identity()
    {
        var units = FreshPhysicalUnits();

        Assert.Equal(29, units.Count);
        Assert.Equal(units.Count, units.Select(unit => unit.Id.Value).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(units.Count, units.Select(unit => unit.Name).Distinct(StringComparer.Ordinal).Count());
        Assert.All(units, unit => Assert.Equal(unit.Id.Value, unit.Name));
        var activationSlots = units.Single(unit => unit.Indexes.Any(index => index.Name == ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByActiveActivation));
        Assert.Contains(activationSlots.Indexes, index => index.Name == ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByDefinitionAndSlotId);
        Assert.Contains(activationSlots.Indexes, index => index.Name == ElsaRuntimeV2StorageManifest.WorkflowActivationSlotByActiveActivationAndSlotId);
    }

    private static IReadOnlyList<StorageUnit> FreshPhysicalUnits()
    {
        var runId = Guid.NewGuid().ToString("N")[..8];
        return ElsaRuntimeV2StorageManifest.CreateUnits()
            .Select((unit, index) =>
            {
                var subject = $"gwv2_{runId}_{index:D2}";
                return unit with { Id = new StorageUnitId(subject), Name = subject };
            })
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

    private static StorageValues MatrixValues(StorageUnit unit, string providerName)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [ElsaRuntimeV2StorageManifest.IdField] = $"matrix-{providerName}-{unit.Id.Value}",
            [ElsaRuntimeV2StorageManifest.SchemaVersionField] = ElsaRuntimeV2StorageManifest.SchemaVersion,
            [ElsaRuntimeV2StorageManifest.ContentField] = "{}"
        };
        foreach (var column in unit.Columns.Where(column =>
                     !column.IsNullable &&
                     !values.ContainsKey(column.Name) &&
                     !StringComparer.Ordinal.Equals(column.Name, unit.Concurrency.TokenColumn)))
            values[column.Name] = RequiredValue(column.Type);
        return new StorageValues(values);
    }

    private static object RequiredValue(PortableType type) => type switch
    {
        PortableType.String => "matrix",
        PortableType.Int32 => 1,
        PortableType.Int64 => 1L,
        PortableType.Boolean => true,
        PortableType.DateTimeOffset => DateTimeOffset.UnixEpoch,
        PortableType.Guid => Guid.Parse("11111111-1111-1111-1111-111111111111"),
        PortableType.Binary => new byte[] { 1 },
        PortableType.Decimal => 1m,
        _ => throw new InvalidOperationException($"No provider-matrix value is declared for required type '{type}'.")
    };

    private static string EnvironmentVariable(string providerName) =>
        $"GROUNDWORK_V2_{providerName.ToUpperInvariant()}_CONNECTION_STRING";

}
