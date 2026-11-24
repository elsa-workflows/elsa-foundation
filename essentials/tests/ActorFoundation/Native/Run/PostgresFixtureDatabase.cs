using Kernel.EntityFramework;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Native.Run;

internal static class PostgresFixtureDatabase
{
    // Objects in a later search-path schema can be resolved by unqualified fixture SQL.
    internal static Task<bool> IsEmptyAsync(KernelDb database, CancellationToken cancellationToken) =>
        database.Database.SqlQueryRaw<bool>("""
            WITH user_schema AS (
                SELECT oid, nspname FROM pg_catalog.pg_namespace
                WHERE nspname <> 'information_schema' AND nspname !~ '^pg_'
            )
            SELECT EXISTS (SELECT 1 FROM user_schema WHERE nspname = pg_catalog.current_schema()) AND NOT EXISTS (
                SELECT 1 FROM pg_catalog.pg_depend dependency
                JOIN user_schema ns ON ns.oid = dependency.refobjid
                WHERE dependency.refclassid = 'pg_catalog.pg_namespace'::pg_catalog.regclass
            ) AS "Value"
            """).SingleAsync(cancellationToken);
}
