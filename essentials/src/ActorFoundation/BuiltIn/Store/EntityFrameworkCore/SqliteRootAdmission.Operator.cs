using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal static async Task<FoundationReadCursor> ReadSelectionCursorAsync(KernelDb db, CancellationToken token) =>
        new(HMACSHA256.HashData(await ReadRootIssuerKeyAsync(db, token), "elsa-preview/journal-cursor/1"u8));

    internal Task<Contract.Admission> TerminateAsync(string tenant, Contract.ActorRef reference, string operation,
        string selection, long? due, TimeSpan? keepFor, CancellationToken token)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference.Id) || !FoundationMetadataLimits.IsOutsideMessageId(operation))
            return Task.FromResult(new Contract.Admission(Contract.AdmissionOutcome.Refused));
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var digest = FoundationRootCreator.OperationDigest(operation);
        return WriteAsync<Contract.Admission>(async (db, now, ct) =>
        {
            var cursor = await ReadSelectionCursorAsync(db, ct);
            if (!PostgresRootSelection.MatchesRunning(cursor, selection, tenant, reference.Id))
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
            var target = await ReadTargetAsync(db, tenant, reference.Id, ct);
            if (target is null)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
            var prior = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= {now}
                """).SingleOrDefaultAsync(ct);
            if (prior == reference.Id) return new(Contract.AdmissionOutcome.Duplicate, reference);
            if (prior is not null || target.Status != (int)InstanceStatus.Running)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);

            var until = checked(now + (long)window.TotalMilliseconds);
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationOperatorReceipts" ("Tenant", "Operation", "Reference", "Until")
                VALUES ({tenant}, {digest}, {reference.Id}, {until})
                ON CONFLICT ("Tenant", "Operation") DO UPDATE SET "Reference" = excluded."Reference", "Until" = excluded."Until"
                """, ct));
            var (id, header) = PostgresRootAdmission.EncodeOperator(reference.Id, operation, digest, 1, Kinds.Terminate, due);
            await EnqueueAsync(db, target.Actor, target.Partition, id, InboxKey.FoundationOccurrenceScope, Kinds.Terminate,
                header, null, due, until, replyTo: null, token: ct);
            return new(Contract.AdmissionOutcome.Accepted, reference);
        }, token);
    }
}
