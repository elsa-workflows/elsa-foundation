using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private Func<string, CreationAddress, string>? _rootChildReference;
    private const string RootChildNoticePrefix = "!kernel-child-notice/1:";
    private sealed record StoredChildNotice(string Id, string Payload);

    internal async Task EnableRootChildrenAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootChildren) return;
        if (!db.Database.IsNpgsql()) throw new StoreFormatException("The child qualification profile requires PostgreSQL.");
        await RequireStoreVersionsAsync(db, token);
        var key = await PostgresRootAdmission.ReadRootIssuerKeyAsync(db, token);
        _rootChildReference = (tenant, address) => FoundationRootCreator.ReferenceFor(key, tenant, address);
    }

    private sealed record RootChildStart(string Reference, Message Creator);

    private sealed record RootChildEffects(string? Children, RootChildStart[] Starts, Message? Notice)
    {
        internal async Task<bool> BindAsync(KernelDb db, string tenant, CancellationToken token)
        {
            if (!db.UsesRootChildren) return false;
            foreach (var (reference, creator) in Starts)
            {
                var fingerprint = CreationProtocol.Fingerprint(creator.Start, creator.Kind, creator.Payload)!;
                var (header, payload) = RootAdmissionWire.EncodeCreator(creator);
                if (!RootAdmissionWire.FitsEnvelope(header, payload))
                    throw new StoreException(Failure.Definite, "The child creator exceeds its storage budget.");
                if (await db.Database.ExecuteSqlInterpolatedAsync($"""
                    WITH birth AS (
                        INSERT INTO "FoundationRootBirths" ("Tenant","Reference","Actor","Partition","Creation","Creator","Fingerprint")
                        VALUES ({tenant}, {reference}, {creator.Target.Id}, {creator.Target.Partition}, 1, {creator.InboxKey.Id}, {fingerprint})
                        RETURNING "Actor", "Partition", "Creator"
                    )
                    INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Payload","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration")
                    SELECT "Creator", 0, "Actor", "Partition", {Kinds.Start}, 0, {payload}, {header}, {creator.Sender}, false, 0, 0, 0, 0 FROM birth
                    """, token) != 1) return false;
            }
            if (Notice is not { } notice) return true;
            var stored = RootChildNoticePrefix + DurableKeyJson.Write(new StoredChildNotice(notice.Id, notice.Payload!));
            if (!RootAdmissionWire.FitsEnvelope(stored, null))
                throw new StoreException(Failure.Definite, "The child result exceeds its storage budget.");
            var target = notice.Target;
            // A finalized parent has no model continuation to receive. A running or parked parent gets the exact notice.
            await db.Database.ExecuteSqlInterpolatedAsync($"""
                INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration")
                SELECT {notice.InboxKey.Id}, {InboxKey.FoundationOccurrenceScope}, b."Actor", b."Partition", {Kinds.ChildFinished}, 0,
                    {stored}, {notice.Sender}, false, 0, 0, 0, 0
                FROM "FoundationRootBirths" b
                JOIN "Instances" i ON i."Partition" = b."Partition" AND i."Id" = b."Actor" AND i."FoundationReference" = b."Reference"
                WHERE b."Tenant" = {tenant} AND b."Actor" = {target.Id} AND b."Partition" = {target.Partition}
                    AND b."Creation" = {notice.TargetCreation} AND i."FoundationTerminalRevision" IS NULL AND NOT i."Retired"
                """, token);
            return true;
        }
    }

    private RootChildEffects? PrepareRootChildEffects(WorkerRoot root, InstanceRow row, IReadOnlyList<Message> sends)
    {
        if (_rootChildReference is not { } reference) return null;
        var starts = new List<RootChildStart>();
        Message? notice = null;
        foreach (var message in sends)
        {
            if (message.BindFoundationChild)
            {
                if (message is not { Kind: Kinds.Start, Start: { Creation: { Generation: 1, Previous: 0 } } start, TargetCreation: 1 } ||
                    message.InboxScope != InboxKey.ChildCreatorScope || message.Sender != root.Actor || message.SenderCreation != 1 ||
                    start.Parent is not null && (start.Parent != root.Actor || start.ParentCreation != 1) ||
                    start.Parent is null && start.ParentCreation != 0 || !message.Target.ChildAddress || !CreationProtocol.IsPhysicalAddress(message.Target.Id))
                    throw RootShape();
                var child = new CreationAddress(message.Target.Id, 1);
                starts.Add(new(reference(root.Tenant, child), (message with { StorageId = "root:" + child.Id, Payload = start.Input })
                    .WithInboxScope(0)));
            }
            else if (message.Kind == Kinds.ChildFinished)
            {
                if (notice is not null || row.Parent is null || row.Parent != message.Target.Id ||
                    message.TargetCreation != row.ParentCreation || message.Sender != row.Id || message.SenderCreation != row.Creation ||
                    message.Id != CreationProtocol.TerminalNoticeId(row.Id, row.Creation, row.Revision) || message.Payload is null)
                    throw RootShape();
                notice = message with { StorageId = ChildNoticeOccurrence(reference(root.Tenant, new(row.Parent, row.ParentCreation)), message.Id) };
            }
            else throw RootShape();
        }
        return new(row.Children, starts.ToArray(), notice);
    }

    private static PreparedRoot WithRootChildEffects(PreparedRoot prepared, RootChildEffects? children, string? parent)
    {
        if (children is null) return prepared;
        return prepared.Checkpoint switch
        {
            FirstRootCheckpoint first => prepared with
            {
                JournalTurn = prepared.JournalTurn! with { Row = first.Row with { Parent = parent, Children = children.Children } },
                Checkpoint = first with { ChildEffects = children }
            },
            RootTurnCheckpoint subsequent => prepared with { Checkpoint = subsequent with { ChildEffects = children } },
            _ => throw RootShape()
        };
    }

    private static string ChildNoticeOccurrence(string parentReference, string notice) =>
        parentReference + "/child/" + FoundationRootCreator.OperationDigest(notice);

    private static string RootChildNoticePredicate(string alias) =>
        $"""({alias}."Kind" = 'child-finished' AND COALESCE({alias}."Start", '') LIKE '{RootChildNoticePrefix}%')""";

    private static bool IsStoredRootChildNotice(string? header) =>
        header?.StartsWith(RootChildNoticePrefix, StringComparison.Ordinal) == true;

    private static Message DecodeRootChildNotice(MessageEntity row, string reference)
    {
        var stored = DurableKeyJson.Read<StoredChildNotice>(row.Start![RootChildNoticePrefix.Length..]);
        var notice = DurableKeyJson.Read<FoundationChildNotice>(stored.Payload);
        if (row.InboxScope != InboxKey.FoundationOccurrenceScope || row.Kind != Kinds.ChildFinished || row.Sender != notice.Child ||
            row.JobGeneration != 0 || row.Due != 0 || row.KeepUntil is not null || row.ReplyTo is not null || row.Payload is not null ||
            notice.Creation != 1 || notice.Status is not (InstanceStatus.Finished or InstanceStatus.Faulted or InstanceStatus.Cancelled) ||
            !notice.Final && notice.Status != InstanceStatus.Faulted || !CreationProtocol.IsTerminalHandle(stored.Id, CreationProtocol.Origin(notice.Child, 1)) ||
            row.Id != ChildNoticeOccurrence(reference, stored.Id))
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL child notice.");
        return (new Message(stored.Id, new(row.Target), Kinds.ChildFinished, stored.Payload,
            row.Sender, TargetCreation: 1, SenderCreation: 1) { FoundationRootChildNotice = true, StorageId = row.Id })
            .WithInboxScope(InboxKey.FoundationOccurrenceScope);
    }
}
