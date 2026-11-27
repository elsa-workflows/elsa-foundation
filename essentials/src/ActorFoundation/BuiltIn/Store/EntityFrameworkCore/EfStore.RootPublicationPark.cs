using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private sealed record RootPublicationPark(Message Input, long Sequence, int Attempts)
    {
        internal async Task<bool> KeepAsync(KernelDb db, WorkerRoot root, CancellationToken token)
        {
            if (!db.UsesRootPagedPublicEvents) return false;
            var row = await db.Messages.AsNoTracking().SingleOrDefaultAsync(message =>
                message.Id == Input.Id && message.InboxScope == RootPublicEventTicket.Scope && message.Kind == Kinds.Publish &&
                message.Target == root.Actor && message.Partition == root.Partition && message.Seq == Sequence &&
                message.Attempts == Attempts && !message.Done, token);
            if (row is null || !Input.HasIssuedTicket ||
                RootPublicEventTicket.Decode(row).ToMessage().WithoutTicketAuthority() != Input.WithoutTicketAuthority()) return false;
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                UPDATE "Messages" SET "Done" = true, "Trial" = 0, "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                WHERE "Id" = {row.Id} AND "InboxScope" = {RootPublicEventTicket.Scope} AND "Kind" = {Kinds.Publish}
                    AND "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "Seq" = {Sequence} AND "Attempts" = {Attempts}
                    AND "EventMatch" IS NULL AND NOT "Done" AND "FoundationKeptBy" IS NULL
                """, token) == 1;
        }
    }

    private static async Task<bool> RequeueParkPublicationsAsync(KernelDb db, WorkerRoot root, CancellationToken token)
    {
        if (!db.UsesRootPagedPublicEvents) return true;
        // The author can have just one publication reopen its current Park. This ticket has no actor inbox cohort.
        var rows = await ReadParkPublicationsAsync(db, root, token);
        if (rows.Count == 0) return true;
        var row = rows.Single();
        var seq = db.Database.IsNpgsql() ? "DEFAULT" : """(SELECT max("Seq") + 1 FROM "Messages")""";
        return await ChangeParkPublicationAsync(db, row,
            $""" "Seq" = {seq}, "Done" = false, "Seen" = 0, "Trial" = 0, "Attempts" = 0, "FoundationKeptBy" = NULL""",
            token) == 1;
    }

    private static async Task<bool> AbandonParkPublicationsAsync(KernelDb db, WorkerRoot root, CancellationToken token)
    {
        if (!db.UsesRootPagedPublicEvents) return true;
        foreach (var row in await ReadParkPublicationsAsync(db, root, token))
        {
            var ticket = row.Decode();
            if (!ticket.Initial && await db.Database.ExecuteSqlInterpolatedAsync($"""
                    UPDATE "FoundationPublicEventDispatch" SET "Status" = {(int)Contract.PublicationStatus.DeadLettered}
                    WHERE "Occurrence" = {ticket.Occurrence} AND "Ticket" = {ticket.Id} AND "Target" = {root.Actor}
                        AND "Partition" = {root.Partition} AND "Status" = {(int)Contract.PublicationStatus.Pending}
                    """, token) != 1) return false;
            if (await ChangeParkPublicationAsync(db, row, """
                    "FoundationKeptBy" = NULL, "Payload" = NULL, "Start" = NULL, "KeepUntil" = 0
                    """, token) != 1) return false;
        }
        return true;
    }

    private sealed record KeptPublication(string Id, string Target, int Partition, long Seq, string Start, string? Payload)
    {
        internal RootPublicEventTicket Decode() => RootPublicEventTicket.Decode(new MessageEntity
        {
            Id = Id, InboxScope = RootPublicEventTicket.Scope, Target = Target, Partition = Partition,
            Kind = Kinds.Publish, Start = Start, Payload = Payload
        });
    }

    private static async Task<List<KeptPublication>> ReadParkPublicationsAsync(KernelDb db, WorkerRoot root, CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<KeptPublication>($"""
            SELECT "Id", "Target", "Partition", "Seq", "Start", "Payload"
            FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {RootPublicEventTicket.Scope}
                AND "Kind" = {Kinds.Publish} AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """).ToListAsync(token);
        if (rows.Count > 1 || rows.Any(row => row.Decode().Author?.Tenant != root.Tenant)) throw PublicationShape();
        return rows;
    }

    private static Task<int> ChangeParkPublicationAsync(KernelDb db, KeptPublication row, string assignments, CancellationToken token)
    {
        FormattableString command = $"""
            UPDATE "Messages" SET /*publish-assignments*/
            WHERE "Id" = {row.Id} AND "InboxScope" = {RootPublicEventTicket.Scope} AND "Target" = {row.Target}
                AND "Partition" = {row.Partition} AND "Seq" = {row.Seq} AND "Kind" = {Kinds.Publish}
                AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
            """;
        return db.Database.ExecuteSqlInterpolatedAsync(FormattableStringFactory.Create(
            command.Format.Replace("/*publish-assignments*/", assignments, StringComparison.Ordinal), command.GetArguments()), token);
    }
}
