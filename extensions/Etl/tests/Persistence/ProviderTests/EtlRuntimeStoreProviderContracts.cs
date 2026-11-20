using Elsa.Etl.Tests;

namespace Elsa.Etl.Persistence.ProviderTests;

// The runtime-store contracts Elsa.Etl.Tests runs on SQLite, on each server engine.

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlRuntimeStorePostgreSqlTests(EtlPostgreSqlProviderFixture fixture) : EtlRuntimeStoreContract
{
    protected override Task<IEtlRuntimeTestDatabase> CreateDatabaseAsync() => EtlProviderRuntimeDatabase.CreateAsync(fixture);
}

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlRuntimeStoreSqlServerTests(EtlSqlServerProviderFixture fixture) : EtlRuntimeStoreContract
{
    protected override Task<IEtlRuntimeTestDatabase> CreateDatabaseAsync() => EtlProviderRuntimeDatabase.CreateAsync(fixture);
}

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlRuntimeStoreMySqlTests(EtlMySqlProviderFixture fixture) : EtlRuntimeStoreContract
{
    protected override Task<IEtlRuntimeTestDatabase> CreateDatabaseAsync() => EtlProviderRuntimeDatabase.CreateAsync(fixture);
}
