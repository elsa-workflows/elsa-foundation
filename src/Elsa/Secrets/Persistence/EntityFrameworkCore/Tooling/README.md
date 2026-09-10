# Secrets EF design-time tooling

This project exists so `dotnet ef` can see Sqlite, SqlServer, and Npgsql providers plus
`Microsoft.EntityFrameworkCore.Design`. It is **not** a Nuplane runtime package.

Generated **Initial** migrations live in the **module** assembly
(`../Migrations/Sqlite|SqlServer|PostgreSql`) so `MigrateAsync` can apply them without
loading this tooling project.

## Generate

From the repository root (see also `tools/ef/README.md`):

```bash
dotnet tool restore  # or: dotnet tool install dotnet-ef --version 10.0.10 --tool-path .tools
dotnet ef migrations add Initial \
  --context SecretsSqliteDbContext \
  --project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj \
  --startup-project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj \
  --output-dir Migrations/Sqlite \
  --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.Sqlite

dotnet ef migrations add Initial \
  --context SecretsSqlServerDbContext \
  --project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj \
  --startup-project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj \
  --output-dir Migrations/SqlServer \
  --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.SqlServer

dotnet ef migrations add Initial \
  --context SecretsPostgreSqlDbContext \
  --project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj \
  --startup-project src/Elsa/Secrets/Persistence/EntityFrameworkCore/Tooling/Elsa.Secrets.Persistence.EntityFrameworkCore.Tooling.csproj \
  --output-dir Migrations/PostgreSql \
  --namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Migrations.PostgreSql
```

Factories set `MigrationsHistoryTable(__EFMigrationsHistory_ElsaSecrets)`.

If a provider snapshot emits `*ModelBuilderExtensions` calls that need the provider
package to compile, rewrite them to Relational `HasColumnType` / annotations so the
module stays provider-free. Do not add SqlServer/Npgsql PackageReferences to the module.

## Phase 2 (out of scope here)

Nuplane dual-migrate CI (apply + fail-if-pending per derived context) is a later hook.
Pending-model drift per provider can call `dotnet ef migrations has-pending-model-changes`.
