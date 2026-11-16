using Elsa.Etl.Tests;

namespace Elsa.Etl.Persistence.ProviderTests;

// The design-store contracts Elsa.Etl.Tests runs on SQLite, on each server engine.

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlDefinitionStorePostgreSqlTests(EtlPostgreSqlProviderFixture fixture) : EtlDefinitionStoreContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlDefinitionStoreSqlServerTests(EtlSqlServerProviderFixture fixture) : EtlDefinitionStoreContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlDefinitionStoreMySqlTests(EtlMySqlProviderFixture fixture) : EtlDefinitionStoreContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlDefinitionStoreV2PostgreSqlTests(EtlPostgreSqlProviderFixture fixture) : EtlDefinitionStoreV2Contract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlDefinitionStoreV2SqlServerTests(EtlSqlServerProviderFixture fixture) : EtlDefinitionStoreV2Contract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlDefinitionStoreV2MySqlTests(EtlMySqlProviderFixture fixture) : EtlDefinitionStoreV2Contract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlPostgreSqlProviderCollection.Name)]
public sealed class EtlDefinitionCatalogPostgreSqlTests(EtlPostgreSqlProviderFixture fixture) : EtlDefinitionCatalogContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlSqlServerProviderCollection.Name)]
public sealed class EtlDefinitionCatalogSqlServerTests(EtlSqlServerProviderFixture fixture) : EtlDefinitionCatalogContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}

[Collection(EtlMySqlProviderCollection.Name)]
public sealed class EtlDefinitionCatalogMySqlTests(EtlMySqlProviderFixture fixture) : EtlDefinitionCatalogContract
{
    protected override Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => EtlProviderDesignDatabase.CreateAsync(fixture);
}
