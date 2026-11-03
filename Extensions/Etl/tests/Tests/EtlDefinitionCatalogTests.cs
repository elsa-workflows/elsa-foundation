using Elsa.Etl.Core;
using Elsa.Etl.Design;
using Elsa.Etl.Design.Stores;
using Elsa.Etl.Persistence.EntityFrameworkCore;
using Elsa.Etl.Persistence.EntityFrameworkCore.Stores;
using Elsa.Persistence.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>The design-store contract on SQLite, with the cases only SQLite can stage.</summary>
public sealed class EtlDefinitionCatalogTests : EtlDefinitionCatalogContract
{
    protected override async Task<IEtlDesignTestDatabase> CreateDatabaseAsync() => await EtlDesignTestDatabase.CreateAsync();
}
