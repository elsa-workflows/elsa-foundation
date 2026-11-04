using Elsa.Etl.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Elsa.Etl.Tests;

/// <summary>A migrated ETL design database on one engine, for the design-store contracts.</summary>
public interface IEtlDesignTestDatabase : IAsyncDisposable
{
    /// <summary>A new context over the database, with optional command interceptors for staging races.</summary>
    internal EtlDesignDbContext CreateContext(params IInterceptor[] interceptors);
}
