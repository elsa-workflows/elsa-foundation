using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.OpenIddict;

// One context per engine other than SQLite, which keeps the base context and its migrations (MySQL is not supported: see
// WorkbenchOpenIddictStoreProvider). EF Core finds a migration by the context type it was scaffolded for, so each engine's
// migration set is told apart by its context type, as every first-party EF module does. The model is the base context's, unchanged;
// the stores still ask for the base type, which the Workbench's store registration resolves to the selected engine's context.

/// <summary>The OpenIddict store on SQL Server.</summary>
public sealed class OpenIddictIdentitySqlServerDbContext(DbContextOptions<OpenIddictIdentitySqlServerDbContext> options)
    : OpenIddictIdentityDbContext(options);

/// <summary>The OpenIddict store on PostgreSQL.</summary>
public sealed class OpenIddictIdentityPostgreSqlDbContext(DbContextOptions<OpenIddictIdentityPostgreSqlDbContext> options)
    : OpenIddictIdentityDbContext(options);
