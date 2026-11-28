using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    private const string RootChildCascadeWire = "!kernel-child-cascade/1";
    private static readonly string RootChildCascadeRow =
        FormattableString.Invariant($"""(m."InboxScope" = {InboxKey.FoundationOccurrenceScope} AND m."Start" = '{RootChildCascadeWire}')""");
    private static readonly string RootChildCascadePollFilter = $"""
        AND ({RootChildCascadeRow} OR NOT EXISTS (
            SELECT 1 FROM "FoundationRootChildCascades" children WHERE children."Reference" = b."Reference"))
        """;
    private static readonly string RootChildPriorityPoll = $"""
        {RootChildCascadeRow} OR EXISTS (SELECT 1 FROM "Instances" child
            WHERE child."Id" = m."Target" AND child."Partition" = m."Partition" AND child."Suspended"
            AND {RootChildTerminatePredicate("m", "child")}) OR
        """;
    private sealed record RootChildCascade(WorkerRoot Parent, long Revision, int Cursor, CreationAddress[] Children);
    private sealed record StoredChildCascade(long Revision, int Cursor, string Children);

    private static RootChildEffects WithPendingChildCascade(WorkerRoot root, InstanceRow next, RootImage current,
        CascadeCommit cascade, RootChildEffects? effects)
    {
        if (effects is null || !ChildRegistry.Read(current.Children).SequenceEqual(cascade.Entries.Children)) throw RootShape();
        var dispatched = Math.Min(cascade.Entries.Children.Length, CascadeProtocol.PageLimit - cascade.Entries.Jobs.Length);
        return dispatched == cascade.Entries.Children.Length
            ? effects : effects with { Cascade = new(root, next.Revision, dispatched, cascade.Entries.Children.ToArray()) };
    }

    internal static Task InstallRootChildCascadeAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            CREATE TABLE IF NOT EXISTS "FoundationRootChildCascades" (
                "Reference" text COLLATE "C" PRIMARY KEY REFERENCES "FoundationRootBirths" ("Reference"),
                "Revision" bigint NOT NULL CHECK ("Revision" > 0),
                "Cursor" integer NOT NULL CHECK ("Cursor" > 0),
                "Children" jsonb NOT NULL CHECK ("Cursor" < jsonb_array_length("Children")));
            """, token);

    private static string ChildCascadePrefix(string reference) => reference + "/child/cascade/";

    private static string ChildCascadeKey(string reference, long revision, int cursor) =>
        ChildCascadePrefix(reference) + FormattableString.Invariant($"{revision}.{cursor}");

    private static Message DecodeChildCascadeControl(MessageEntity row, string reference)
    {
        if (row.Kind != Kinds.Cascade || row.Sender != row.Target || row.InboxScope != InboxKey.FoundationOccurrenceScope ||
            !row.Id.StartsWith(ChildCascadePrefix(reference), StringComparison.Ordinal) || row.Due != 0 ||
            row.Payload is not null || row.KeepUntil is not null || row.ReplyTo is not null || row.JobGeneration != 0)
            throw new StoreException(Failure.Definite, "Invalid PostgreSQL child cascade control.");
        return new Message(row.Id, new(row.Target), Kinds.Cascade, Sender: row.Sender, TargetCreation: 1, SenderCreation: 1)
            .WithInboxScope(InboxKey.FoundationOccurrenceScope);
    }

    private static async Task<bool> OpenRootChildCascadeAsync(KernelDb db, RootChildCascade cascade, CancellationToken token)
    {
        FormattableString insert = $"""
            INSERT INTO "FoundationRootChildCascades" ("Reference", "Revision", "Cursor", "Children")
            VALUES ({cascade.Parent.Reference}, {cascade.Revision}, {cascade.Cursor}, {Json.Write(cascade.Children)}/*json-cast*/)
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(RootChildDialectQuery(db, insert), token) == 1 &&
            await EnqueueRootChildCascadeAsync(db, cascade.Parent, cascade.Revision, cascade.Cursor, token);
    }

    private static async Task<bool> EnqueueRootChildCascadeAsync(KernelDb db, WorkerRoot parent, long revision, int cursor,
        CancellationToken token)
    {
        FormattableString insert = $"""
            INSERT INTO "Messages" ("Id","InboxScope","Target","Partition","Kind","Due","Start","Sender","Done","Seen","Attempts","Trial","JobGeneration"/*seq-column*/)
            VALUES ({ChildCascadeKey(parent.Reference, revision, cursor)}, {InboxKey.FoundationOccurrenceScope}, {parent.Actor}, {parent.Partition},
                {Kinds.Cascade}, 0, {RootChildCascadeWire}, {parent.Actor}, false, 0, 0, 0, 0/*seq*/)
            """;
        return await db.Database.ExecuteSqlInterpolatedAsync(RootChildDialectQuery(db, insert), token) == 1;
    }

    private static async Task<bool> DrainRootChildCascadeAsync(KernelDb db, WorkerRoot parent, IReadOnlyList<InboxKey> keys,
        Func<string, CreationAddress, string>? reference, Func<string, string>? handle, CancellationToken token)
    {
        var controls = keys.Where(key => key.Scope == InboxKey.FoundationOccurrenceScope &&
            key.Id.StartsWith(ChildCascadePrefix(parent.Reference), StringComparison.Ordinal)).ToArray();
        if (controls.Length == 0) return true;
        if (controls is not [var control] || reference is null || handle is null) return false;
        FormattableString select = $"""
            SELECT "Revision", "Cursor", "Children"/*text-cast*/ AS "Children" FROM "FoundationRootChildCascades"
            WHERE "Reference" = {parent.Reference}/*lock*/
            """;
        var pending = await db.Database.SqlQuery<StoredChildCascade>(RootChildDialectQuery(db, select)).SingleOrDefaultAsync(token);
        if (pending is null || control.Id != ChildCascadeKey(parent.Reference, pending.Revision, pending.Cursor)) return false;
        var children = Json.Read<CreationAddress[]>(pending.Children);
        var count = Math.Min(CascadeProtocol.PageLimit, children.Length - pending.Cursor);
        if (count <= 0) return false;
        var page = new Message[count];
        var origin = CreationProtocol.Origin(parent.Actor, 1);
        for (var index = 0; index < count; index++)
        {
            var child = children[pending.Cursor + index];
            if (child.Creation != 1) return false;
            var id = handle(FormattableString.Invariant($"cascade:{origin}#cascade-child:{pending.Revision}:{pending.Cursor + index}"));
            page[index] = new Message(id, new(child.Id), Kinds.Terminate, Sender: parent.Actor, TargetCreation: 1, SenderCreation: 1)
                { StorageId = ChildOccurrence(reference(parent.Tenant, child), id) };
        }
        await BindRootChildInputsAsync(db, parent.Tenant, page, token);

        var cursor = pending.Cursor + count;
        if (cursor == children.Length)
            return await db.Database.ExecuteSqlInterpolatedAsync($"""
                DELETE FROM "FoundationRootChildCascades"
                WHERE "Reference" = {parent.Reference} AND "Revision" = {pending.Revision} AND "Cursor" = {pending.Cursor}
                """, token) == 1;
        return await db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE "FoundationRootChildCascades" SET "Cursor" = {cursor}
            WHERE "Reference" = {parent.Reference} AND "Revision" = {pending.Revision} AND "Cursor" = {pending.Cursor}
            """, token) == 1 && await EnqueueRootChildCascadeAsync(db, parent, pending.Revision, cursor, token);
    }
}
