using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.DesignConformance.Target;
using Groundwork.Kernel.Schema;
using Groundwork.Sqlite;
using Groundwork.Store;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Persistence.Groundwork.DesignConformance.Sqlite.Tests;

/// <summary>The composed Groundwork design target on one uniquely named temporary SQLite database.</summary>
internal sealed class SqliteDesignPersistenceContractFixture : GroundworkDesignPersistenceContractFixture
{
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"elsa-design-groundwork-{Guid.NewGuid():N}");

    private SqliteDesignPersistenceContractFixture() : base("groundwork-sqlite", "SQLite") =>
        Directory.CreateDirectory(_directory);

    /// <summary>The isolated database's connection string, for provider-level drift probes.</summary>
    internal string SqliteConnectionString => $"Data Source={Path.Join(_directory, "design.db")}";

    public static Task<SqliteDesignPersistenceContractFixture> CreateAsync(CancellationToken cancellationToken = default) =>
        OpenAsync(new SqliteDesignPersistenceContractFixture(), cancellationToken);

    internal IReadOnlyList<GroundworkRuntimeSchemaAdmissionStatus> InspectRuntimeAdmission()
    {
        var connection = Services.GetRequiredService<IStorageProviderConnection>();
        return Services.GetRequiredService<GroundworkStorageUnitRegistry>().Registrations
            .Select(registration => connection.Schema.InspectRuntimeAdmission(
                registration.Unit,
                new GroundworkRuntimeSchemaAdmissionOptions { AutoApplyOnStartup = false }).Status)
            .ToArray();
    }

    protected override IStorageProviderConnection CreateConnection() =>
        new SqliteProviderFactory().Create(SqliteConnectionString);

    protected override void CleanUp()
    {
        try
        {
            Directory.Delete(_directory, recursive: true);
        }
        catch (IOException)
        {
            // A uniquely named test directory is harmless if SQLite releases a sidecar late.
        }
    }
}
