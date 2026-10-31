# Connections EF persistence

`Elsa.Connections.Persistence.EntityFrameworkCore` replaces the default in-memory
`IConnectionRepository`. It stores tenant-scoped connection documents, their versions and
secret-reference lookup projections. Credentials remain owned by Secrets; select its durable
backend separately. Connection settings are metadata, so put credential values in Secrets.

## Default module contexts

Enable `ConnectionsEntityFrameworkCore` after `Connections`, or call
`services.AddConnectionsEntityFrameworkCore(new() { Provider = "Sqlite", ConnectionString = ... })`
after registering Connections. The host supplies the selected engine package: SQLite, SQL Server,
Npgsql PostgreSQL or Oracle MySQL. This package carries EF Core and Relational APIs without an engine.

Each provider has its own context and committed Initial migration. The module binds its migration
assembly by object and derives its history table from the `Connections` EF declaration. It uses the
shared [migration lifecycle](../../../Persistence/EntityFramework/EXTENSION_POINTS.md): shell
activation/reload and ordinary host startup honor `Elsa:Persistence:EntityFramework:Migrate:Policy`.
Named connections, schema selection and optional context pooling use the same shared binding.

## Consumer-owned contexts

Inheritance from `ConnectionsDbContext` is optional. Install the mappings in an existing context,
then register the repository over that context:

```csharp
public sealed class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.AddConnectionsModel(Database.ProviderName!);
        // Other application mappings may be installed before or after this call.
    }
}

services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connectionString));
services.AddConnections();
services.AddConnectionsRepository<ApplicationDbContext>();
```

The host owns migrations for this combined model. The mappings add only the connection document and
reference entities; their ordinal collation declarations leave other application entities alone.
The context must have a public constructor accepting its own `DbContextOptions<TContext>`.
Each repository write creates fresh contexts from those options and commits independently. It does
not flush the caller's tracked changes or enlist in the caller's transaction. Resolve a scoped
repository for each concurrent operation.

## Repository behavior

Tenant identity uses ordinal equality; connection names, secret-reference names and the type/scheme
lookup projections use the frozen ordinal casing table. Its fingerprint is checked before deriving
keys. Search operates on the stored folded name/display-name projections. List limits remain bounded.

Insert retains the supplied version. Save compares that version atomically, increments the stored
version and replaces references in one transaction. Conditional delete checks the version in the
database and removes references in the same owned transaction, including with SQLite foreign keys
disabled. Unsupported stored schemas refuse reads/reference checks
and cannot be overwritten or deleted by an older host. Provider failures retain their cause in
`ConnectionStoreException`; cancellation propagates. The repository does not resolve the connection
manager's separately tracked credential/unknown-commit recovery limits (#2855 and #3153).

`GetCapabilitiesAsync` reports the configured metadata backend. File-backed SQLite and the other
three supported relational providers report durable storage; temporary/in-memory SQLite and custom
VFS profiles do not. This describes the configured storage profile, not database reachability or
credential-store durability. Existing contributions default to an unspecified, non-durable profile
until they implement the capability explicitly.

The focused tests include the shared repository contract, simultaneous writers, schema/refusal
controls, a consumer-owned context with unsaved unrelated changes, separate writer/reader processes,
normal shell activation/reload, encrypted credential resolution and all four migration sets.
Full-tier CI sets `ELSA_CONNECTIONS_EF_REQUIRE_NATIVE_PROVIDERS=1`, so an unavailable required
database fails qualification rather than skipping the matrix.
See [extension points](EXTENSION_POINTS.md) for the public registration surface.

## Independent package qualification

From a committed source revision, run with Python 3.12 or later:

```bash
python3 tools/connections/qualify.py --source HEAD \
  --version 4.0.0-connections.local1 --output /private/tmp/connections-package-proof
```

The output directory must be new and outside the checkout. The script archives that commit, builds
and packs its actual dependency graph, restores an independent package-only consumer into separate
normal and locked caches, and compares consumed package/DLL bytes with the retained candidates.
Four processes write/read both the default module model and an unrelated consumer-owned context.
The report covers metadata persistence; it does not qualify live models or credential storage.

For pre-GA Initial regeneration, the canonical module-migrations script removes old migration types
before building its tooling. Temporarily remove their `.Migrations.Connections.` entries from
`PublicAPI.Unshipped.txt` while those types are absent, run the scoped generator, then restore the
entries and run the normal build/API checks over the regenerated types. Do not disable the API gate.
