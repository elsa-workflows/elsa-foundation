using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal static Task InstallRootParkReadModelAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "FoundationActorList" ADD COLUMN IF NOT EXISTS "ParkAt" bigint,
                ADD COLUMN IF NOT EXISTS "ParkFault" text /*park-revision-column*/;
            CREATE INDEX IF NOT EXISTS "IX_FoundationActorList_Parked" ON "FoundationActorList" ("Tenant", "Reference")
                WHERE "ParkAt" IS NOT NULL;
            CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_Inspectable" ON "Messages" /*inspectable*/;
            CREATE OR REPLACE FUNCTION kernel_project_root_park_metadata() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE changed bigint;
            BEGIN
                EXECUTE format(
                    'UPDATE %I."FoundationActorList" SET "ParkAt" = $1, "ParkFault" = $2 /*park-revision-set*/ WHERE "Tenant" = $3 AND "Reference" = $4',
                    TG_TABLE_SCHEMA)
                USING NEW."FoundationParkAt",
                    CASE WHEN NEW."FoundationParkAt" IS NULL THEN NULL ELSE NEW."Fault" END,
                    NEW."FoundationTenant", NEW."FoundationReference" /*park-revision-value*/;
                GET DIAGNOSTICS changed = ROW_COUNT;
                IF changed <> 1 THEN
                    RAISE EXCEPTION 'Park requires the projected actor header' USING ERRCODE = '23514';
                END IF;
                RETURN NEW;
            END
            $fn$;
            CREATE OR REPLACE TRIGGER kernel_project_root_park_metadata AFTER UPDATE OF "FoundationParkAt"
                ON "Instances" FOR EACH ROW WHEN (NEW."FoundationReference" IS NOT NULL AND
                    OLD."FoundationParkAt" IS DISTINCT FROM NEW."FoundationParkAt")
                EXECUTE FUNCTION kernel_project_root_park_metadata();

            UPDATE "FoundationActorList" p SET "ParkAt" = i."FoundationParkAt",
                "ParkFault" = CASE WHEN i."FoundationParkAt" IS NULL THEN NULL ELSE i."Fault" END /*park-revision-backfill*/
            FROM "Instances" i
            WHERE p."Tenant" = i."FoundationTenant" AND p."Reference" = i."FoundationReference" AND NOT i."Retired"
                AND (p."ParkAt", p."ParkFault" /*park-revision-old*/) IS DISTINCT FROM
                    (i."FoundationParkAt", CASE WHEN i."FoundationParkAt" IS NULL THEN NULL ELSE i."Fault" END /*park-revision-new*/);
            """.Replace("/*inspectable*/", InspectableIndex, StringComparison.Ordinal)
            .Replace("/*kept-reasons*/", KeptReasons(db), StringComparison.Ordinal)
            // Recover selections name the opening revision, so its profile projects that revision with the Park time.
            .Replace("/*park-revision-column*/", db.UsesRootParkRecovery ? """, ADD COLUMN IF NOT EXISTS "ParkRevision" bigint""" : "", StringComparison.Ordinal)
            .Replace("/*park-revision-set*/", db.UsesRootParkRecovery ? """, "ParkRevision" = $5""" : "", StringComparison.Ordinal)
            .Replace("/*park-revision-value*/", db.UsesRootParkRecovery ? """, NEW."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-backfill*/", db.UsesRootParkRecovery ? """, "ParkRevision" = i."FoundationParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-old*/", db.UsesRootParkRecovery ? """, p."ParkRevision" """ : "", StringComparison.Ordinal)
            .Replace("/*park-revision-new*/", db.UsesRootParkRecovery ? """, i."FoundationParkRevision" """ : "", StringComparison.Ordinal), token);

    internal static Task InstallRootParkArrivalReasonAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Instances" ADD COLUMN IF NOT EXISTS "FoundationParkRevision" bigint CHECK ("FoundationParkRevision" > 0);
            """, token);
}
