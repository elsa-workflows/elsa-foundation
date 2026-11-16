using Elsa.Persistence.EntityFramework.Tests;
using Xunit;

namespace Elsa.Workflows.Runtime.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// <see cref="LiveDrainSweepContentionContract"/> on the EF Runtime stores over a SQLite file. PostgreSQL runs it in the
/// provider-test lane.
/// </summary>
public sealed class RuntimeEntityFrameworkCoreLiveDrainSweepContentionTests : IAsyncDisposable
{
    private readonly TemporarySqliteDatabase _database = new("runtime-ef-contention");

    public static TheoryData<string> Scenarios => LiveDrainSweepContentionContract.Scenarios;

    [Theory]
    [MemberData(nameof(Scenarios))]
    public Task Sqlite(string scenario) =>
        LiveDrainSweepContentionContract.RunAsync(
            scenario,
            LiveDrainSweepContentionContract.EntityFramework("Sqlite", $"{_database.ConnectionString};Pooling=False"));

    public ValueTask DisposeAsync() => _database.DisposeAsync();
}
