namespace Elsa.Etl.Tests;

/// <summary>The runtime-store contracts on SQLite, with the cases only SQLite can stage in the SQLite-only partials.</summary>
public sealed partial class EtlRuntimeStoreTests : EtlRuntimeStoreContract
{
    protected override async Task<IEtlRuntimeTestDatabase> CreateDatabaseAsync() => await EtlRuntimeTestDatabase.CreateAsync();

    private static Task<EtlRuntimeTestDatabase> CreateSqliteDatabaseAsync() => EtlRuntimeTestDatabase.CreateAsync();
}
