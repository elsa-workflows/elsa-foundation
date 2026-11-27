using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    internal static async ValueTask<FoundationChildBirthProof> ConfirmRootChildBirthAsync(Func<KernelDb> create, Contract.BoundScope scope,
        Contract.ActorRef child, string issuance, CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootChildren || !db.UsesRootRetirement) return FoundationChildBirthProof.Unresolved;

        try
        {
            if (db.UsesSqliteRootRuntime)
                return await ReadSqliteRootSnapshotAsync(db, async _ => await ConfirmAsync(
                    await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token)), token);

            // Hold the marker and source namespace while authenticating the issuance and reading the
            // occurrence, retirement mark and disarming fence in the same physical snapshot.
            await using var snapshot = await db.Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, token);
            await PostgresRootAdmission.PinRootNamespaceAsync(db, token);
            return await ConfirmAsync(await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token));
        }
        catch (Exception error) when (!token.IsCancellationRequested &&
            error is TimeoutException or SqliteException { SqliteErrorCode: 5 or 6 } or NpgsqlException { IsTransient: true })
        {
            return FoundationChildBirthProof.Unresolved;
        }

        async Task<FoundationChildBirthProof> ConfirmAsync(byte[] key)
        {
            var birth = FoundationChildIssuance.Verify(key, scope.Tenant, child, issuance);
            if (birth is null) return FoundationChildBirthProof.Unresolved;
            var (partition, epoch) = birth.Fence();
            var parent = FoundationRootCreator.ReferenceFor(key, scope.Tenant, new(birth.Parent, 1));
            var creator = "root:" + birth.Child;
            var proof = await db.Database.SqlQuery<int>($"""
                SELECT CASE
                    WHEN EXISTS (
                        SELECT 1 FROM "FoundationRootBirths" b
                        JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                            AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
                        WHERE b."Reference" = {child.Id} AND b."Tenant" = {scope.Tenant}
                            AND b."Actor" = {birth.Child} AND b."Partition" = {partition} AND b."Creation" = 1
                            AND b."Creator" = {creator} AND b."Fingerprint" = {birth.ChildFingerprint}
                            AND i."Retired" AND i."Status" <> {(int)InstanceStatus.Running}
                            AND i."FoundationTerminalRevision" > 0 AND i."FoundationTerminalAt" IS NOT NULL
                            AND i."FoundationWriteRevision" = i."Revision") THEN 1
                    WHEN NOT EXISTS (SELECT 1 FROM "FoundationRootBirths" b WHERE b."Reference" = {child.Id})
                        AND NOT EXISTS (SELECT 1 FROM "FoundationRootBirths" b WHERE b."Actor" = {birth.Child})
                        AND NOT EXISTS (SELECT 1 FROM "Instances" i WHERE i."Id" = {birth.Child} AND i."Partition" = {partition})
                        AND NOT EXISTS (SELECT 1 FROM "Messages" m WHERE m."InboxScope" = 0 AND m."Id" = {creator})
                        AND (EXISTS (SELECT 1 FROM "Partitions" p WHERE p."Id" = {partition} AND p."Epoch" > {epoch})
                            OR EXISTS (
                                SELECT 1 FROM "FoundationRootBirths" b
                                JOIN "Instances" i ON i."Id" = b."Actor" AND i."Partition" = b."Partition"
                                    AND i."FoundationTenant" = b."Tenant" AND i."FoundationReference" = b."Reference"
                                WHERE b."Reference" = {parent} AND b."Tenant" = {scope.Tenant}
                                    AND b."Actor" = {birth.Parent} AND b."Partition" = {partition} AND b."Creation" = 1
                                    AND b."Fingerprint" = {birth.ParentFingerprint}
                                    AND i."Revision" >= {birth.ParentRevision}
                                    AND (i."Revision" = 1 OR i."FoundationWriteRevision" = i."Revision"))) THEN 2
                    ELSE 0 END AS "Value"
                """).SingleAsync(token);
            return (FoundationChildBirthProof)proof;
        }
    }
}
