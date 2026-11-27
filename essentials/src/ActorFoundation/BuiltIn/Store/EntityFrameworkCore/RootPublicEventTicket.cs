using System.Runtime.CompilerServices;
using Kernel.Runtime;
using Microsoft.EntityFrameworkCore;
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.EntityFramework;

internal sealed record RootPublicEventTicket(string Id, InstanceRef Target, string QualifiedKey, string EventId,
    Guid Incarnation, string Occurrence, long? ReceiptUntil)
{
    internal const long Scope = -1;
    private const string Prefix = "root-publication/1/";
    private static readonly TimeSpan AuthoredDeadLetterWindow = TimeSpan.FromHours(1);

    private sealed record Wire(string Key, string EventId, Guid Incarnation, string Occurrence,
        AuthoredPublication? Author, bool Initial, bool PayloadEscaped, string? DeadLetterAuthor = null);

    internal AuthoredPublication? Author { get; init; }
    internal bool Initial { get; init; }
    internal bool DeadLettered { get; init; }
    internal (string? Text, bool Escaped) Payload { get; init; }

    internal static RootPublicEventTicket Authored(EventRow publication, Message issued, long acceptedAt)
    {
        if (!RootAdmissionWire.FitsRetainedEventPayload(publication.Payload) || publication.KeepUntil > long.MaxValue - acceptedAt)
            throw new StoreException(Failure.Definite, "The authored Event cannot retain its exact body or deadline.");
        var occurrence = FoundationRootCreator.OperationDigest(DurableKeyJson.Write(new[] { "authored", publication.Key, publication.Id }));
        return new(issued.Id, issued.Target, publication.Key, publication.Id, Guid.NewGuid(), occurrence, ReceiptUntil: null)
        {
            Author = new(publication.FoundationTenant!, publication.KeepUntil, acceptedAt), Initial = true,
            Payload = RootAdmissionWire.EncodePayload(publication.Payload)
        };
    }

    internal static RootPublicEventTicket Outside(EventRow publication, Guid incarnation, string occurrence, int partitions, long receiptUntil)
    {
        var id = FoundationEventIdentity.PublishId(publication, incarnation);
        return new(id, InstanceRef.In(InstanceRef.Spread(id, partitions), "publisher"), publication.Key, publication.Id,
            incarnation, occurrence, receiptUntil);
    }

    internal static bool IsStored(long scope, string? kind) => scope == Scope && kind == Kinds.Publish;

    internal static RootPublicEventTicket Decode(MessageEntity row)
    {
        var storedTarget = new InstanceRef(row.Target);
        if (!IsStored(row.InboxScope, row.Kind) || row.Start is not { } header || !header.StartsWith(Prefix, StringComparison.Ordinal) ||
            row.ReplyTo is not null || row.Sender is not null || row.Due != 0 || row.JobGeneration != 0 ||
            row.Partition < 0 || storedTarget.Partition != row.Partition)
            throw BadTicket();
        var wire = DurableKeyJson.Read<Wire>(header[Prefix.Length..]);
        if (wire is not { Key.Length: > 0, EventId.Length: > 0 } || wire.Incarnation == Guid.Empty ||
            !FoundationRootCreator.IsGeneratedReference(wire.Occurrence) ||
            !wire.Initial && (row.Payload is not null || wire.PayloadEscaped) || wire.PayloadEscaped && row.Payload is null)
            throw BadTicket();
        var deadLettered = wire.DeadLetterAuthor is not null;
        var target = deadLettered ? new InstanceRef(wire.DeadLetterAuthor!) : storedTarget;
        if (wire.Author is { } author)
        {
            if (wire.EventId != "~" + row.Id || author.AcceptedAt is not > 0 || author.KeepFor < 0 ||
                !wire.Key.StartsWith(author.Tenant + "_", StringComparison.Ordinal))
                throw BadTicket();
            if (deadLettered
                ? !row.Done || row.Trial != 0 || row.KeepUntil is not > 0 ||
                    storedTarget != Publisher(row.Partition) || target == storedTarget || target.Partition != row.Partition
                : row.KeepUntil is not null)
                throw BadTicket();
        }
        else if (deadLettered || wire.Initial || row.KeepUntil is null || wire.EventId[0] == '~' || target != Publisher(row.Partition) ||
            row.Id != FoundationEventIdentity.PublishId(new(wire.Key, wire.EventId, null, 0), wire.Incarnation))
            throw BadTicket();
        return new(row.Id, target, wire.Key, wire.EventId, wire.Incarnation, wire.Occurrence, row.KeepUntil)
            { Author = wire.Author, Initial = wire.Initial, DeadLettered = deadLettered, Payload = (row.Payload, wire.PayloadEscaped) };
    }

    internal Message ToMessage()
    {
        if (DeadLettered) throw BadTicket();
        return new Message(Id, Target, Kinds.Publish, Sender: Author is null ? null : Target.Id, KeepUntil: ReceiptUntil)
        {
            FoundationPublish = new(QualifiedKey, EventId, Incarnation, Authored: Author),
            SenderCreation = Author is null ? 0 : 1,
            TargetCreation = Author is null ? null : 1
        }.AsIssued().AsKernelTicket();
    }

    private string Header(bool initial, string? deadLetterAuthor = null) => Prefix + DurableKeyJson.Write(
        new Wire(QualifiedKey, EventId, Incarnation, Occurrence, Author, initial, initial && Payload.Escaped, deadLetterAuthor));

    private static InstanceRef Publisher(int partition) => InstanceRef.In(partition, "publisher");

    internal async Task EnqueueAsync(KernelDb db, Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (DeadLettered) throw BadTicket();
        var header = Header(Initial);
        if (bind is null)
        {
            await SqliteRootAdmission.EnqueueAsync(db, Target.Id, Target.Partition, Id, Scope, Kinds.Publish, header,
                Payload.Text, due: null, ReceiptUntil, replyTo: null, token);
            return;
        }
        var written = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            INSERT INTO "Messages" ("Id", "InboxScope", "Target", "Partition", "Kind", "Start", "Payload", "Due",
                "Seen", "Attempts", "Trial", "Done", "KeepUntil", "JobGeneration")
            SELECT {Id}, {Scope}, {Target.Id}, {Target.Partition}, {Kinds.Publish}, {header}, CAST({Payload.Text} AS TEXT), 0,
                0, 0, 0, false, CAST({ReceiptUntil} AS BIGINT), 0
            FROM source WHERE "SourceReady"
            """, bind), token);
        if (written != 1) throw BadTicket();
    }

    internal async Task<bool> DeadLetterAuthoredAsync(KernelDb db, long sequence, int attempts, bool fromPark,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        if (Author is null || DeadLettered || Target == Publisher(Target.Partition)) throw BadTicket();
        if (!Initial && await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
                WITH source AS MATERIALIZED (/*public-event-start-source*/)
                UPDATE "FoundationPublicEventDispatch" SET "Status" = {(int)Contract.PublicationStatus.DeadLettered}
                WHERE "Occurrence" = {Occurrence} AND "Ticket" = {Id} AND "Target" = {Target.Id} AND "Partition" = {Target.Partition}
                    AND "Status" = {(int)Contract.PublicationStatus.Pending} AND (SELECT "SourceReady" FROM source)
                """, bind), token) != 1) return false;

        var now = db.Database.IsNpgsql() ? await RootEventAdmission.ReadPostgresClockAsync(db, token) :
            await SqliteRootAdmission.ReadClockAsync(db, token);
        var until = checked(now + (long)AuthoredDeadLetterWindow.TotalMilliseconds);
        // Keep the same ticket and initial body off the actor mailbox so retirement can remove its own residue.
        var written = await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind($"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/)
            UPDATE "Messages" SET "Target" = {Publisher(Target.Partition).Id}, "Start" = {Header(Initial, Target.Id)},
                "Done" = true, "Trial" = 0, "FoundationKeptBy" = NULL, "KeepUntil" = {until}
            WHERE "Id" = {Id} AND "InboxScope" = {Scope} AND "Kind" = {Kinds.Publish} AND "Target" = {Target.Id}
                AND "Partition" = {Target.Partition} AND "Seq" = {sequence} AND "Attempts" = {attempts}
                AND "EventMatch" IS NULL AND "Done" = {fromPark} AND (SELECT "SourceReady" FROM source)
                AND "FoundationKeptBy" IS NOT DISTINCT FROM CAST({(fromPark ? (short?)ParkKeepReason.Consumed : null)} AS SMALLINT)
            """, bind), token);
        if (written != 1) return false;
        await RootEventStarts.DiscardStagedMatchesAsync(db, Occurrence, bind, token);
        return true;
    }

    internal enum Change { Reserve, RecordFailure, Materialize, Advance, Settle }

    internal async Task<bool> ChangeAsync(KernelDb db, long sequence, int attempts, long? epoch, Change mutation,
        Func<FormattableString, FormattableString>? bind, CancellationToken token)
    {
        const string observedEpoch = """(SELECT "Epoch" FROM requested)""";
        // SQLite computes the next sequence while the current ticket is still present, even if it is the last row.
        var nextSequence = bind is null ? """(SELECT max("Seq") + 1 FROM "Messages")""" : "DEFAULT";
        var assignments = mutation switch
        {
            Change.Reserve => $""" "Seen" = {observedEpoch}, "Trial" = {observedEpoch}""",
            Change.RecordFailure => """ "Trial" = 0, "Attempts" = "Attempts" + 1""",
            Change.Materialize => $""" "Seq" = {nextSequence}, "Seen" = 0, "Trial" = 0, "Attempts" = 0, "Payload" = NULL, "Start" = (SELECT "Header" FROM requested)""",
            Change.Advance => $""" "Seq" = {nextSequence}, "Seen" = 0, "Trial" = 0, "Attempts" = 0""",
            Change.Settle => """ "Done" = true, "Trial" = 0, "KeepUntil" = coalesce("KeepUntil", (SELECT "KeepUntil" FROM requested)), "Payload" = NULL, "Start" = NULL""",
            _ => throw new ArgumentOutOfRangeException(nameof(mutation))
        };
        var trial = mutation == Change.Reserve ? $"""AND "Trial" <> {observedEpoch}""" :
            epoch is null && mutation == Change.Settle ? "" : $"""AND "Trial" = {observedEpoch}""";
        FormattableString query = $"""
            WITH source AS MATERIALIZED (/*public-event-start-source*/), requested AS (SELECT CAST({epoch} AS BIGINT) AS "Epoch",
                CAST({(mutation == Change.Materialize ? Header(initial: false) : null)} AS TEXT) AS "Header",
                CAST({(Author is { AcceptedAt: long accepted } author ? checked(accepted + author.KeepFor) : 0)} AS BIGINT) AS "KeepUntil")
            UPDATE "Messages" SET /*assign*/
            WHERE "Id" = {Id} AND "InboxScope" = {Scope} AND "Target" = {Target.Id}
                AND "Partition" = {Target.Partition} AND "Seq" = {sequence} AND "Attempts" = {attempts}
                AND "Kind" = {Kinds.Publish} AND NOT "Done" /*trial*/ AND (SELECT "SourceReady" FROM source)
            """;
        var format = query.Format.Replace("/*assign*/", assignments, StringComparison.Ordinal)
            .Replace("/*trial*/", trial, StringComparison.Ordinal);
        return await db.Database.ExecuteSqlInterpolatedAsync(RootPublicEventStarts.Bind(
            FormattableStringFactory.Create(format, query.GetArguments()), bind), token) == 1;
    }

    private static StoreFormatException BadTicket() => new("The durable public Event publication ticket has an invalid shape.");
}
