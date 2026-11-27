using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private async Task<IReadOnlyList<InstanceRow>> LoadPostgresWorkerRootsAsync(KernelDb db, WorkerRoots scope,
        IReadOnlyCollection<string> ids, CancellationToken token)
    {
        var actors = ids.Distinct(StringComparer.Ordinal).ToArray();
        if (actors.Length == 0) return [];
        await using var pinned = await BeginPinnedRootOperationAsync(db, token);
        // Pending and retired actors retain a birth but have no loadable checkpoint. An unknown birth is a malformed worker lookup.
        var rows = await db.Database.SqlQuery<WorkerRootLoad>(WithPhysicalRootColumns(db, $"""
            SELECT b."Tenant", b."Reference", b."Actor", b."Partition", b."Fingerprint",
                i."Id", b."Creation", i."State", /*root-wire*/ AS "Wire", i."StateVersion", i."Revision",
                i."FoundationHeader" AS "Header", i."History", i."Status", i."Suspended",
                i."FoundationTerminalRevision" AS "TerminalRevision", i."FoundationTerminalAt" AS "TerminalAt",
                /*root-read-clock*/ AS "AsOf", i."Jobs", i."NextJobGeneration", i."Fault", /*root-park-at*/ AS "ParkAt",
                i."Parent", i."Children"
            FROM unnest({actors}::text[]) WITH ORDINALITY AS request(actor, ordinal)
            JOIN "FoundationRootBirths" b ON b."Actor" = request.actor AND b."Creation" = 1
            LEFT JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                AND i."FoundationReference" = b."Reference" AND i."FoundationTenant" = b."Tenant" AND NOT i."Retired"
            WHERE ({scope.FixedPartition}::integer IS NULL OR b."Partition" = {scope.FixedPartition})
            ORDER BY request.ordinal
            """)).ToListAsync(token);
        return MaterializeWorkerRoots(db, scope, rows, actors.Length);
    }
}
