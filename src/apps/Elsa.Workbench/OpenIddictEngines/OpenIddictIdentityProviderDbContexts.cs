using Elsa.Workbench.OpenIddict;
using Microsoft.EntityFrameworkCore;

namespace Elsa.Workbench.OpenIddictEngines;

// Elsa's per-engine contexts for Workbench's OpenIddict store: one for each engine other than SQLite, which keeps the vendor
// context and its migrations (MySQL is not supported: see WorkbenchOpenIddictStoreProvider). EF Core finds a migration by the
// context type it was scaffolded for, so each engine's migration set is told apart by its context type, as every first-party EF
// module does. The model is the vendor context's, unchanged, and these hold nothing else; the stores still ask for the vendor
// type, which WorkbenchOpenIddictStoreProvider resolves to the selected engine's context.

/// <summary>The OpenIddict store on SQL Server.</summary>
public sealed class OpenIddictIdentitySqlServerDbContext(DbContextOptions<OpenIddictIdentitySqlServerDbContext> options)
    : OpenIddictIdentityDbContext(options);

/// <summary>The OpenIddict store on PostgreSQL.</summary>
public sealed class OpenIddictIdentityPostgreSqlDbContext(DbContextOptions<OpenIddictIdentityPostgreSqlDbContext> options)
    : OpenIddictIdentityDbContext(options);
