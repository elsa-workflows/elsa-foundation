using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Composition;
using Groundwork.Kernel;
using Groundwork.MongoDb;
using Groundwork.PostgreSql;
using Groundwork.Sqlite;
using Groundwork.SqlServer;
using Groundwork.Store;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Workflows.Publishing.Persistence.Groundwork.Tests;

/// <summary>Smoke-proves publishing's public v2 catalog against each configured native provider.</summary>
public sealed class PublishingGroundworkProviderMatrixTests
{
    public static TheoryData<string> Providers => new() { "sqlite", "postgresql", "sqlserver", "mongodb" };

    [SkippableTheory]
    [MemberData(nameof(Providers))]
    public void Applies_every_publishing_unit_and_reads_a_scoped_row(string providerName)
    {
        var sqlitePath = providerName == "sqlite"
            ? Path.Combine(Path.GetTempPath(), $"elsa-publishing-v2-matrix-{Guid.NewGuid():N}.db")
            : null;
        var connectionString = providerName == "sqlite"
            ? $"Data Source={sqlitePath};Pooling=False"
            : Environment.GetEnvironmentVariable(EnvironmentVariable(providerName));
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            // Gate the escalation on the explicit native-matrix opt-in, never on CI alone: this project
            // starts no containers, so it runs in the container-free lane, which by construction has no
            // provider connection strings. Demanding them there fails a job that can never supply them.
            var required = Environment.GetEnvironmentVariable("GROUNDWORK_V2_REQUIRE_NATIVE_PROVIDER_MATRIX") is "1" or "true";
            if (required)
                throw new InvalidOperationException(
                    $"The {providerName} publishing v2 provider proof requires {EnvironmentVariable(providerName)}.");
            Skip.If(true, $"Set {EnvironmentVariable(providerName)} to run the {providerName} publishing v2 provider proof.");
        }

        try
        {
            using var connection = CreateConnection(providerName, connectionString!);
            var units = PublishingGroundworkStorageManifest.CreateUnits();
            foreach (var unit in units)
                connection.Schema.Apply(unit);

            var scope = new StorageScope($"publishing-v2-matrix-{providerName}");
            foreach (var unit in units)
            {
                var session = connection.OpenSession(unit, StorageAccess.Scoped(scope));
                var id = $"matrix-{providerName}-{unit.Id.Value}";
                var result = session.Upsert(RowFor(unit, id), WriteOptions.Unconditional);
                Assert.True(result.Succeeded, $"Upsert failed for '{unit.Id.Value}' on {providerName}: {result.Status}.");
                Assert.NotNull(session.Read(new StorageKey(new Dictionary<string, object?>
                {
                    [PublishingGroundworkStorageManifest.IdField] = id
                })));
            }
        }
        finally
        {
            if (sqlitePath is not null)
            {
                foreach (var path in new[] { sqlitePath, $"{sqlitePath}-shm", $"{sqlitePath}-wal" })
                {
                    if (File.Exists(path))
                        File.Delete(path);
                }
            }
        }
    }

    /// <summary>
    /// A row every declared column will accept. The required projections are filled from the unit itself
    /// rather than a per-unit table, so a manifest that adds or drops one does not silently stop being
    /// covered here — the point of this proof is that each unit's schema applies and round-trips, not
    /// that its columns are spelled a particular way.
    /// </summary>
    private static StorageValues RowFor(StorageUnit unit, string id)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            [PublishingGroundworkStorageManifest.IdField] = id,
            [PublishingGroundworkStorageManifest.SchemaVersionField] = PublishingGroundworkStorageManifest.SchemaVersion,
            [PublishingGroundworkStorageManifest.ContentField] = "{}"
        };
        foreach (var column in unit.Columns)
        {
            // The optimistic token is system-owned: supplying it is refused before the provider is reached.
            if (values.ContainsKey(column.Name) ||
                column.IsNullable ||
                column.Name == PublishingGroundworkStorageManifest.ConcurrencyTokenField)
                continue;
            values[column.Name] = column.Type switch
            {
                PortableType.String => $"matrix-{column.Name}",
                PortableType.DateTimeOffset => new DateTimeOffset(2026, 7, 13, 12, 0, 0, TimeSpan.Zero),
                PortableType.Int32 => 0,
                PortableType.Int64 => 0L,
                PortableType.Boolean => false,
                PortableType.Guid => Guid.Empty,
                _ => throw new NotSupportedException($"Unhandled matrix column type '{column.Type}'.")
            };
        }

        return new StorageValues(values);
    }

    private static IStorageProviderConnection CreateConnection(string providerName, string connectionString) => providerName switch
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
