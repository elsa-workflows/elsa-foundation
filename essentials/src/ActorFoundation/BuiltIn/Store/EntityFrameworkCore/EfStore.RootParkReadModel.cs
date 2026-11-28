using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const string ParkTerminating = "terminating";
    private const string ParkDeleting = "deleting";

    internal static Task InstallRootParkReadModelAsync(KernelDb db, CancellationToken token)
    {
        var clearing = db.UsesRootParkedOperators;
        return db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationActorList" ADD COLUMN IF NOT EXISTS "ParkAt" bigint,
                ADD COLUMN IF NOT EXISTS "ParkFault" text /*park-revision-column*/ /*park-resolution-column*/;
            CREATE INDEX IF NOT EXISTS "IX_FoundationActorList_Parked" ON "FoundationActorList" ("Tenant", "Reference")
                WHERE "ParkAt" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_Inspectable" ON "Messages" /*inspectable*/;
            CREATE OR REPLACE FUNCTION kernel_project_root_park_metadata() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE changed bigint;
            BEGIN
                EXECUTE format(
                    'UPDATE %I."FoundationActorList" SET "ParkAt" = $1, "ParkFault" = $2 /*park-revision-set*/ /*park-resolution-set*/ WHERE "Tenant" = $3 AND "Reference" = $4',
                    TG_TABLE_SCHEMA)
                USING NEW."FoundationParkAt",
                    CASE WHEN NEW."FoundationParkAt" IS NULL THEN NULL ELSE NEW."Fault" END,
                    NEW."FoundationTenant", NEW."FoundationReference" /*park-revision-value*/ /*park-resolution-value*/;
                GET DIAGNOSTICS changed = ROW_COUNT;
                IF changed <> 1 THEN
                    RAISE EXCEPTION 'Park requires the projected actor header' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_project_root_park_metadata AFTER UPDATE OF "FoundationParkAt" /*park-resolution-source*/
                ON "Instances" FOR EACH ROW WHEN (NEW."FoundationReference" IS NOT NULL AND
                    (OLD."FoundationParkAt" IS DISTINCT FROM NEW."FoundationParkAt" /*park-resolution-changed*/) /*park-retired*/)
                EXECUTE FUNCTION kernel_project_root_park_metadata();

            UPDATE "FoundationActorList" p SET "ParkAt" = i."FoundationParkAt",
                "ParkFault" = CASE WHEN i."FoundationParkAt" IS NULL THEN NULL ELSE i."Fault" END /*park-revision-backfill*/ /*park-resolution-backfill*/
            FROM "Instances" i
            WHERE p."Tenant" = i."FoundationTenant" AND p."Reference" = i."FoundationReference" AND NOT i."Retired"
                AND (p."ParkAt", p."ParkFault" /*park-revision-old*/ /*park-resolution-old*/) IS DISTINCT FROM
                    (i."FoundationParkAt", CASE WHEN i."FoundationParkAt" IS NULL THEN NULL ELSE i."Fault" END /*park-revision-new*/ /*park-resolution-new*/);
            """.Replace("/*inspectable*/", InspectableIndex, StringComparison.Ordinal)
            // S5b-3a: a parked root retired by its Terminate-then-delete has no list row left to project its Park onto.
            .Replace("/*park-retired*/", db.UsesRootParkedOperators ? """AND NOT NEW."Retired" """ : "", StringComparison.Ordinal)
            .Replace("/*kept-reasons*/", KeptReasons(db), StringComparison.Ordinal)
            // Park selections name the opening revision, so a set that seals them projects that revision with the Park time.
            .Replace("/*park-revision-column*/", db.ProjectsRootParkRevision ? """, ADD COLUMN IF NOT EXISTS "ParkRevision" bigint""" : "", StringComparison.Ordinal)
            .Replace("/*park-revision-set*/", db.ProjectsRootParkRevision ? """, "ParkRevision" = $5""" : "", StringComparison.Ordinal)
            .Replace("/*park-revision-value*/", db.ProjectsRootParkRevision ? """, NEW."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-backfill*/", db.ProjectsRootParkRevision ? """, "ParkRevision" = i."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-old*/", db.ProjectsRootParkRevision ? """, p."ParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-new*/", db.ProjectsRootParkRevision ? """, i."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-column*/", clearing ? """, ADD COLUMN IF NOT EXISTS "ParkResolution" text""" : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-set*/", clearing ? """, "ParkResolution" = $6""" : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-value*/", clearing ? ", " + ParkResolutionSql("NEW") : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-source*/", clearing ? """, "FoundationParkClearingKind" """ : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-changed*/", clearing
                ? """OR OLD."FoundationParkClearingKind" IS DISTINCT FROM NEW."FoundationParkClearingKind" """
                : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-backfill*/", clearing ? $""", "ParkResolution" = {ParkResolutionSql("i")} """ : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-old*/", clearing ? """, p."ParkResolution" """ : "", StringComparison.Ordinal)
            .Replace("/*park-resolution-new*/", clearing ? ", " + ParkResolutionSql("i") : "", StringComparison.Ordinal), token);
    }

    private static string ParkResolutionSql(string row) => $"""
        CASE {row}."FoundationParkClearingKind" WHEN '{Kinds.Terminate}' THEN '{ParkTerminating}' WHEN '{Kinds.Delete}' THEN '{ParkDeleting}' END
        """;

    private static string? ParkResolutionFor(string? kind) => kind switch
    {
        Kinds.Terminate => ParkTerminating,
        Kinds.Delete => ParkDeleting,
        null => null,
        _ => throw RootShape()
    };

    internal static Task InstallRootParkArrivalReasonAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkRevision" bigint CHECK ("FoundationParkRevision" > 0);
            """, token);
}
