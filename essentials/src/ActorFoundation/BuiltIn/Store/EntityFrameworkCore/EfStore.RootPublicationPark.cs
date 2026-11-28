using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

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
        var bind = await PinParkPublicationSourceAsync(db, token);
        // The author can have just one publication reopen its current Park. This ticket has no actor inbox cohort.
        if (await ReadParkPublicationAsync(db, root, bind, token) is not { } row) return true;
        var seq = db.Database.IsNpgsql() ? "DEFAULT" : """(SELECT max("Seq") + 1 FROM "Messages")""";
        FormattableString command = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            UPDATE "Messages" SET "Seq" = /*next-sequence*/, "Done" = false, "Seen" = 0, "Trial" = 0, "Attempts" = 0,
                "FoundationKeptBy" = NULL
            WHERE "Id" = {row.Id} AND "InboxScope" = {RootPublicEventTicket.Scope} AND "Target" = {row.Target}
                AND "Partition" = {row.Partition} AND "Seq" = {row.Seq} AND "Kind" = {Kinds.Publish}
                AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed} AND (SELECT "SourceReady" FROM source)
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind(FormattableStringFactory.Create(
            command.Format.Replace("/*next-sequence*/", seq, StringComparison.Ordinal), command.GetArguments()), bind), token) == 1;
    }

    private static async Task<bool> AbandonParkPublicationsAsync(KernelDb db, WorkerRoot root, CancellationToken token)
    {
        if (!db.UsesRootPagedPublicEvents) return true;
        var bind = await PinParkPublicationSourceAsync(db, token);
        if (await ReadParkPublicationAsync(db, root, bind, token) is not { } row) return true;
        return await row.Decode().DeadLetterAuthoredAsync(db, row.Seq!.Value, row.Attempts!.Value, fromPark: true, bind, token);
    }

    private static async Task<Func<FormattableString, FormattableString>?> PinParkPublicationSourceAsync(KernelDb db, CancellationToken token) =>
        db.Database.IsNpgsql() ? await PostgresRootAdmission.PinAuthoredEventSourceAsync(db, db.RootCommitMarker, token) : null;

    private sealed record KeptPublication(bool SourceReady, string? Id, string? Target, int? Partition, long? Seq, int? Attempts,
        string? Start, string? Payload)
    {
        internal RootPublicEventTicket Decode() => RootPublicEventTicket.Decode(new MessageEntity
        {
            Id = Id!, InboxScope = RootPublicEventTicket.Scope, Target = Target!, Partition = Partition!.Value,
            Kind = Kinds.Publish, Start = Start, Payload = Payload
        });
    }

    private static async Task<KeptPublication?> ReadParkPublicationAsync(KernelDb db, WorkerRoot root,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        var rows = await db.Database.SqlQuery<KeptPublication>(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), kept AS MATERIALIZED (
                SELECT "Id", "Target", "Partition", "Seq", "Attempts", "Start", "Payload"
                FROM "Messages" WHERE "Target" = {root.Actor} AND "Partition" = {root.Partition} AND "InboxScope" = {RootPublicEventTicket.Scope}
                    AND "Kind" = {Kinds.Publish} AND "Done" AND "FoundationKeptBy" = {(short)ParkKeepReason.Consumed}
                    AND (SELECT "SourceReady" FROM source)
                LIMIT 2
            )
            SELECT source."SourceReady", kept.* FROM source LEFT JOIN kept ON true
            """, bind)).ToListAsync(token);
        if (rows is not [{ SourceReady: true } row]) throw PublicationShape();
        if (row.Id is null) return null;
        if (row.Decode().Author?.Tenant != root.Tenant) throw PublicationShape();
        return row;
    }
}
