using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;
using static Kernel.EntityFramework.RootAdmissionWire;

namespace Kernel.EntityFramework;

internal sealed partial class SqliteRootAdmission
{
    internal static async Task<FoundationReadCursor> ReadSelectionCursorAsync(KernelDb db, CancellationToken token) =>
        new(HMACSHA256.HashData(await ReadRootIssuerKeyAsync(db, token), "elsa-preview/journal-cursor/1"u8));

    internal Task<Contract.Admission> RetentionDeleteAsync(string tenant, Contract.ActorRef reference, string selection, CancellationToken token) =>
        OperateAsync(tenant, reference, "", Kinds.Delete, selection, null, null, token, retentionDelete: true);

    internal Task<Contract.Admission> OperateAsync(string tenant, Contract.ActorRef reference, string operation, string kind,
        string selection, long? due, TimeSpan? keepFor, CancellationToken token, bool thenDelete = false, bool retentionDelete = false,
        HeldRootRequest? reply = null)
    {
        if (!FoundationRootCreator.IsGeneratedReference(reference.Id) || !retentionDelete && !FoundationMetadataLimits.IsOutsideMessageId(operation) ||
            kind is not (Kinds.Terminate or Kinds.Delete) || thenDelete && kind != Kinds.Terminate || retentionDelete && kind != Kinds.Delete)
            return Task.FromResult(new Contract.Admission(Contract.AdmissionOutcome.Refused));
        var window = ReceiptWindow(deduplicationWindow, keepFor);
        var deletion = kind == Kinds.Delete;
        var replyAddress = reply?.Address;
        if (reply is not null && (!reply.IsOpen || reply.Tenant != tenant || due is not null || retentionDelete ||
            !PostgresRootAdmission.ValidOperatorReply(replyAddress!, reply.Caller.Deadline)))
            return Task.FromResult(new Contract.Admission(Contract.AdmissionOutcome.Refused));
        return WriteAsync<Contract.Admission>(async (db, now, ct) =>
        {
            var cursor = await ReadSelectionCursorAsync(db, ct);
            var terminal = deletion ? PostgresRootSelection.OpenTerminal(cursor, selection, tenant, reference.Id) : null;
            if (deletion ? terminal is not { Revision: > 0 } : !PostgresRootSelection.MatchesRunning(cursor, selection, tenant, reference.Id))
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
            var target = await ReadTargetAsync(db, tenant, reference.Id, ct);
            if (target is null)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
            if (reply is not null && reply.Caller.Deadline <= now)
                throw new PostgresRootAdmission.OperatorReplyExpired();
            var idKey = retentionDelete ? PostgresRootAdmission.RetentionOperationFor(tenant, reference.Id, 1, terminal!) : operation;
            var digest = FoundationRootCreator.OperationDigest(idKey);
            var prior = await db.Database.SqlQuery<string>($"""
                SELECT "Reference" AS "Value" FROM "FoundationOperatorReceipts"
                WHERE "Tenant" = {tenant} AND "Operation" = {digest} AND "Until" >= {now}
                """).SingleOrDefaultAsync(ct);
            if (prior == reference.Id) return new(Contract.AdmissionOutcome.Duplicate, reference);
            var eligible = terminal is not null
                ? target.Terminal && await MatchesTerminalAsync(db, tenant, reference.Id, target, terminal, ct)
                : target.Status == (int)InstanceStatus.Running ||
                    thenDelete && target.Terminal && await MatchesTerminalAsync(db, tenant, reference.Id, target, null, ct);
            if (prior is not null || !eligible)
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.StaleSelection);
            if (reply is not null && await db.Database.SqlQuery<bool>($"""
                SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "Target" = {target.Actor} AND "Partition" = {target.Partition}
                    AND "InboxScope" = {InboxKey.FoundationOccurrenceScope} AND "Id" GLOB 'operator/*' AND NOT "Done"
                    AND "ReplyTo" IS NOT NULL LIMIT 1 OFFSET {PostgresRootAdmission.HeldReplyLimit - 1}) AS "Value"
                """).SingleAsync(ct))
                return new(Contract.AdmissionOutcome.Refused, Reason: Contract.RefusalReason.NodeBusy);

            var until = checked(now + (long)window.TotalMilliseconds);
            RequireOne(await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "FoundationOperatorReceipts" ("Tenant", "Operation", "Reference", "Until")
                VALUES ({tenant}, {digest}, {reference.Id}, {until})
                ON CONFLICT ("Tenant", "Operation") DO UPDATE SET "Reference" = excluded."Reference", "Until" = excluded."Until"
                """, ct));
            var (id, header) = PostgresRootAdmission.EncodeOperator(reference.Id, idKey, digest, 1, kind, due,
                terminal, thenDelete: thenDelete, retentionDelete: retentionDelete, replyAddress: replyAddress);
            await EnqueueAsync(db, target.Actor, target.Partition, id, InboxKey.FoundationOccurrenceScope, kind,
                header, null, due, until, replyTo: replyAddress, token: ct);
            return new(Contract.AdmissionOutcome.Accepted, reference);
        }, token);
    }

    internal async Task<Contract.Admission?> OperateHeldAsync(Contract.ActorRef reference, string kind, Contract.OperatorCommand command,
        HeldRootRequest request, CancellationToken token)
    {
        try
        {
            return await OperateAsync(request.Tenant, reference, request.Caller.Id, kind, command.Selection!, null, request.KeepFor,
                token, thenDelete: command.ThenDelete, reply: request);
        }
        catch (PostgresRootAdmission.OperatorReplyExpired) { return null; }
    }

    private static Task<bool> MatchesTerminalAsync(KernelDb db, string tenant, string reference, Target target, FoundationTerminalStamp? mark,
        CancellationToken token) => db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Instances"
                WHERE "Id" = {target.Actor} AND "Partition" = {target.Partition} AND NOT "Retired"
                    AND "FoundationTenant" = {tenant} AND "FoundationReference" = {reference}
                    AND "FoundationParkAt" IS NULL AND "FoundationTerminalRevision" IS NOT NULL AND "FoundationTerminalAt" IS NOT NULL
                    AND ({mark?.Revision} IS NULL OR
                        "FoundationTerminalRevision" = {mark?.Revision} AND "FoundationTerminalAt" = {mark?.At})) AS "Value"
            """).SingleAsync(token);
}
