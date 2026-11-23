namespace Elsa.Etl.Tests;

/// <summary>The design-store catalog contract on SQLite.</summary>
public sealed class EtlDefinitionCatalogTests : EtlDefinitionCatalogContract
{
    protected override async Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => await EtlDesignTestDatabase.CreateAsync();
}
