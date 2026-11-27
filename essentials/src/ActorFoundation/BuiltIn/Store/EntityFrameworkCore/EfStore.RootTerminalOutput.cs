using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private static readonly string PostgresRootTerminalOutputDefinition = FormattableString.Invariant($"""
        CREATE TABLE "FoundationWorkflowTerminalOutputs" (
            "Tenant" text COLLATE "C" NOT NULL, "Reference" text COLLATE "C" NOT NULL,
            "Revision" bigint NOT NULL CHECK ("Revision" > 0), "CapturedAt" bigint NOT NULL,
            "Names" text NOT NULL CHECK (octet_length("Names") <= {RootContentBytes}),
            "Output" bytea CHECK (octet_length("Output") <= {RootContentBytes}),
            PRIMARY KEY ("Tenant", "Reference"),
            FOREIGN KEY ("Tenant", "Reference") REFERENCES "FoundationWorkflowHistory" ("Tenant", "Reference") ON DELETE CASCADE)
        """);

    private static SqliteDefinition SqliteRootTerminalOutputDefinition() => Table("FoundationWorkflowTerminalOutputs", FormattableString.Invariant($"""
        "Tenant" TEXT NOT NULL, "Reference" TEXT NOT NULL,
        "Revision" INTEGER NOT NULL CHECK ("Revision" > 0), "CapturedAt" INTEGER NOT NULL,
        "Names" TEXT NOT NULL CHECK (length(CAST("Names" AS BLOB)) <= {RootContentBytes}),
        "Output" BLOB CHECK (length("Output") <= {RootContentBytes}),
        PRIMARY KEY ("Tenant", "Reference"),
        FOREIGN KEY ("Tenant", "Reference") REFERENCES "FoundationWorkflowHistory" ("Tenant", "Reference") ON DELETE CASCADE
        """));

    private static async Task RequirePostgresRootTerminalOutputSchemaAsync(KernelDb db, CancellationToken token)
    {
        try
        {
            await db.Database.ExecuteSqlRawAsync("""
                LOCK TABLE "FoundationWorkflowHistory", "FoundationWorkflowTerminalOutputs" IN ACCESS SHARE MODE
                """, token);
        }
        catch (PostgresException error) when (error.SqlState is PostgresErrorCodes.UndefinedTable or PostgresErrorCodes.WrongObjectType)
        {
            throw new StoreFormatException("The workflow terminal output history is missing from the installed root schema.", error);
        }
        if (!await db.Database.SqlQueryRaw<bool>("""
            SELECT (SELECT count(*) = 2 FROM pg_catalog.pg_class source
                WHERE source.oid IN (to_regclass('"FoundationWorkflowHistory"'), to_regclass('"FoundationWorkflowTerminalOutputs"'))
                    AND source.relkind = 'r' AND source.relnamespace =
                        (SELECT relnamespace FROM pg_catalog.pg_class WHERE oid = to_regclass('"Instances"')))
                AND EXISTS (SELECT 1 FROM pg_catalog.pg_constraint key
                    WHERE key.contype = 'f' AND key.confdeltype = 'c' AND key.convalidated
                        AND key.conrelid = to_regclass('"FoundationWorkflowTerminalOutputs"')
                        AND key.confrelid = to_regclass('"FoundationWorkflowHistory"')
                        AND key.conkey = ARRAY[1, 2]::smallint[] AND key.confkey = ARRAY[1, 2]::smallint[]) AS "Value"
            """).SingleAsync(token))
            throw new StoreFormatException("The workflow terminal output history is incomplete in the installed root schema.");
    }

    private sealed record RootTerminalOutputCheckpoint(WorkerRoot Root, long Revision, TerminalOutput Output)
        : QualifiedRootCheckpoint(0)
    {
        internal override async Task<bool> BindCheckpointAsync(KernelDb db, CancellationToken token)
        {
            if (!db.UsesRootTerminalOutput) return false;
            if (db.Database.IsNpgsql()) await RequirePostgresRootTerminalOutputSchemaAsync(db, token);
            // The preceding checkpoint must have advanced this root's committed history in this fenced transaction.
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationWorkflowTerminalOutputs" ("Tenant", "Reference", "Revision", "CapturedAt", "Names", "Output")
                SELECT history."Tenant", history."Reference", history."Revision", history."UpdatedAt", {Output.Names}, {Output.Value}
                FROM "FoundationWorkflowHistory" history
                JOIN "Instances" i ON i."FoundationTenant" = history."Tenant" AND i."FoundationReference" = history."Reference"
                WHERE history."Tenant" = {Root.Tenant} AND history."Reference" = {Root.Reference} AND history."Revision" = {Revision}
                    AND i."Id" = {Root.Actor} AND i."Partition" = {Root.Partition} AND i."Status" = {(int)InstanceStatus.Finished}
                    AND i."Revision" = {Revision} AND i."FoundationTerminalRevision" = {Revision} AND NOT i."Retired"
                    AND i."FoundationTerminalAt" = history."UpdatedAt"
                """, token) == 1;
        }
    }

    internal async Task<Contract.ActorTerminalOutput?> ReadRootTerminalOutputAsync(string tenant, string reference,
        long endedAt, bool includeValues, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootTerminalOutput) return null;
        if (!postgres)
            return await ReadSqliteRootSnapshotAsync(db, _ => ReadAsync(), token);

        await using var pinned = await BeginPinnedRootOperationAsync(db, token);
        await RequirePostgresRootTerminalOutputSchemaAsync(db, token);
        return await ReadAsync();

        async Task<Contract.ActorTerminalOutput?> ReadAsync()
        {
            var found = await db.Database.SqlQuery<Contract.ActorTerminalOutput>($"""
                SELECT output."CapturedAt", output."Names", output."Output" IS NULL AS "Omitted",
                    CASE WHEN {includeValues} THEN output."Output" END AS "Values"
                FROM "FoundationWorkflowTerminalOutputs" output
                JOIN "FoundationWorkflowHistory" history USING ("Tenant", "Reference")
                WHERE output."Tenant" = {tenant} AND output."Reference" = {reference} AND output."CapturedAt" = {endedAt}
                    AND history."Revision" = output."Revision" AND history."UpdatedAt" = output."CapturedAt"
                """).SingleOrDefaultAsync(token);
            return found;
        }
    }
}
