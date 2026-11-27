using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

internal static class RootInlineBirthRevision
{
    internal const string Guard = "kernel_keep_root_first_revision";

    internal static string Columns(string baseColumns, bool sqlite)
    {
        var type = sqlite ? "INTEGER" : "bigint";
        return baseColumns.Replace(""" "Fingerprint" """.Trim(),
            $""" "FirstRevision" {type} NOT NULL DEFAULT 1 CHECK ("FirstRevision" > 0), "Fingerprint" """.Trim(),
            StringComparison.Ordinal);
    }

    internal const string SqliteGuard = """
        CREATE TRIGGER kernel_keep_root_first_revision BEFORE UPDATE OF "FirstRevision" ON "FoundationRootBirths"
        WHEN NEW."FirstRevision" IS DISTINCT FROM OLD."FirstRevision"
        BEGIN SELECT RAISE(ABORT, 'A root first revision is immutable'); END
        """;

    internal static async Task InstallPostgresAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("""
            CREATE OR REPLACE FUNCTION kernel_keep_root_first_revision() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                RAISE EXCEPTION 'A root first revision is immutable' USING ERRCODE = '23514';
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_keep_root_first_revision BEFORE UPDATE OF "FirstRevision" ON "FoundationRootBirths"
            FOR EACH ROW WHEN (NEW."FirstRevision" IS DISTINCT FROM OLD."FirstRevision")
            EXECUTE FUNCTION kernel_keep_root_first_revision();
            """, token);
    }
}
