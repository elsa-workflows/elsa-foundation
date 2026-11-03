using Elsa.Etl.Persistence.EntityFrameworkCore;

namespace Elsa.Etl.Tests;

/// <summary>A migrated ETL design database on one engine, for the design-store contracts (spec 1202).</summary>
public interface IEtlDesignTestDatabase : IAsyncDisposable
{
    /// <summary>A new context over the database.</summary>
    internal EtlDesignDbContext CreateContext();
}
