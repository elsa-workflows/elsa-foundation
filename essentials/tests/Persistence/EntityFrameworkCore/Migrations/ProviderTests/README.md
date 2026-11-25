# Native migration provider tests

`ProviderDatabase` normally starts the existing Testcontainers images. A permitted Linux test runner can instead
use already provisioned, stream-owned disposable provider servers, without access to a Docker daemon. Set the
appropriate variable in that runner's private environment; never print its value or put it in test receipts:

| Provider | Explicit connection variable |
|---|---|
| PostgreSQL | `ELSA_MIGRATIONS_EF_POSTGRESQL_TEST_CONNECTION_STRING` |
| SQL Server | `ELSA_MIGRATIONS_EF_SQLSERVER_TEST_CONNECTION_STRING` |
| MySQL | `ELSA_MIGRATIONS_EF_MYSQL_TEST_CONNECTION_STRING` |

These are trusted **disposable-server admin/test connections**, pointing at an existing administrative database.
The account must be able to create a database and drop the databases it creates. Use only a server owned by the
current test stream, never a shared application or production server. The configured administrative database is
used for CREATE/DROP commands and is never itself selected for deletion by this helper.

For each `RunAsync`, the helper creates `elsa_migrations_<random 32 lowercase hex digits>`, passes a connection to
that fresh database to the test, and drops only that exact captured name in cleanup. Identifiers are validated and
quoted for the provider. Admin and test connections disable their own pooling; the helper never clears shared
pools, terminates other sessions or deletes a database inferred from the supplied connection. A failed CREATE
does not confirm ownership and therefore does not cause a DROP. Server acknowledgement confirms ownership before
the admin command/connection is disposed, so a later disposal failure still causes cleanup. A test failure or
cancellation still runs cleanup; simultaneous
test and cleanup failures are both reported. A leaked open test connection can make DROP fail, which remains a
failure to repair rather than forcing other sessions closed.

An explicitly configured connection takes precedence over containers. Authentication, connection, CREATE, test
and cleanup failures fail the test; they never trigger Docker fallback or an unavailable-provider skip. If no
connection is configured, existing Testcontainers behavior remains. `ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1`
continues to make an unavailable container provider fail rather than skip.

Some existing tests, notably `OrdinalCollationProviderTests`, create additional databases themselves to inspect
database-level collation. The helper's isolated-database cleanup does not own those independently named resources;
their existing setup/cleanup must also be reviewed before selecting that suite against an external server.

Run sequentially through the installed `dotnet` build-slot wrapper. After supplying private connection variables,
the focused Decisions check is:

```sh
ELSA_REQUIRE_NATIVE_PROVIDER_MATRIX=1 dotnet test essentials/tests/Persistence/EntityFrameworkCore/Migrations/ProviderTests/Elsa.Persistence.EntityFrameworkCore.Migrations.ProviderTests.csproj -c Release --filter 'FullyQualifiedName~DecisionsJsonProviderTests'
```

`ProviderDatabaseTests` checks naming, connection isolation and failure cleanup without opening a connection.
