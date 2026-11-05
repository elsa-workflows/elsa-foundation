# Elsa.Etl.Persistence.EntityFrameworkCore

EF Core stores for ETL design (drafts and immutable published artifacts) and the ETL runtime (runs, partitions, batches, outputs and terminal delivery). The package depends only on EF Core and its relational layer; a host brings the provider package for its engine.

- **Features:** `EtlDesignEntityFrameworkCore` and `EtlRuntimeEntityFrameworkCore`, each with its own context, schema family and migration history. They register no event handlers or tasks.
- **Design store engines:** SQLite, SQL Server, PostgreSQL and MySQL ([spec 1202](../../../../../specs/1202-etl-design-store-providers/spec.md)). The `Provider` setting selects the engine's context; key columns use the engine's binary collation; publication retries a transient write conflict (a serialization failure, a deadlock, a lock timeout or a busy or locked SQLite database) as a whole unit; a draft update retries its one conditional statement, and every store and catalog read outside a publication attempt retries, the same way.
- **Runtime store engines:** SQLite only; its registration refuses other providers until its keys, check constraints and transitions are qualified.
- **Migrations:** the SQLite design and runtime chains under `Migrations/Sqlite` are hand-maintained. The server engines' design migrations under `Migrations/EtlDesign/<Provider>` are generated as internal classes with `tools/etl/generate-etl-design-migrations.sh`, which uses the `Elsa.Etl.Tooling` design-time project.
- **Tests:** the design-store contracts run on SQLite in `Elsa.Etl.Tests` and on the three server engines in the `etl-ef` container suite (`Extensions/Etl/tests/Persistence/ProviderTests`).
