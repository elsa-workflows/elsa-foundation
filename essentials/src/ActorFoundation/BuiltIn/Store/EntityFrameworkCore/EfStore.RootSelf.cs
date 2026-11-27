using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // A private physical envelope, scoped to issued rows. The application kind is carried inside.
    internal const long RootSelfScope = -5; // provider storage only; the Worker receives InboxKey.Issued (-1)
    private const int RootSelfHeaderBytes = 24 * 1024;
    private const string PostgresPrivateInboxView = "\"FoundationRootMessages\"";
    private const string RootSelfWire = "!foundation-self/1";
    private const string RootSelfTimerWire = "!foundation-self-timer/1";
    private sealed record RootSelfHeader(long Revision, int Ordinal, string Kind,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)] long? Due = null,
        [property: System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault)] bool Rearmed = false);
    private string RootInboxSource => !postgres || _ordinaryRoots is null || RootSelfIssuer is null ? "\"Messages\"" : "\"FoundationRootMessages\"";
    internal FoundationSelfIssuer? RootSelfIssuer { get; private set; }

    private sealed record SelfInstallation(byte[] Key, bool Installed);

    internal async Task EnableRootSelfSendsAsync(CancellationToken token)
    {
        await using var db = create();
        if (!db.UsesRootSelfProtocol || !(db.Database.IsNpgsql() || db.UsesSqliteRootRuntime))
            throw new StoreFormatException("Generated-root self inputs require a qualified root runtime and continuation protocol.");
        if (db.UsesSqliteRootRuntime)
        {
            await RequireSqliteOperationalFormatAsync(db, token);
            var key = await SqliteRootAdmission.ReadRootIssuerKeyAsync(db, token);
            RootSelfIssuer = new(seed => FoundationSelfSend.Handle(key, seed), db.UsesRootTimers, db.UsesRootPrivateParkInputs,
                db.UsesRootTimerRearm ? CheckRootTimerRearmsAsync : null);
            return;
        }
        SelfInstallation install;
        await using (var pinned = await BeginPinnedRootOperationAsync(db, token))
        {
            if (pinned is null) await RequireStoreVersionsAsync(db, token);
            install = await db.Database.SqlQueryRaw<SelfInstallation>("""
                SELECT "Key", EXISTS (
                    SELECT 1 FROM pg_catalog.pg_trigger hook
                    JOIN pg_catalog.pg_class messages ON messages.oid = hook.tgrelid
                    JOIN pg_catalog.pg_proc writer ON writer.oid = hook.tgfoid
                    JOIN pg_catalog.pg_class inbox ON inbox.oid = pg_catalog.to_regclass('"FoundationRootMessages"')
                    WHERE messages.oid = pg_catalog.to_regclass('"Messages"')
                        AND messages.relnamespace = issuer.relnamespace AND messages.relkind = 'r'
                        AND inbox.relnamespace = issuer.relnamespace AND inbox.relkind = 'v'
                        AND hook.tgname = 'kernel_root_self_insert' AND hook.tgenabled IN ('O', 'A')
                        AND writer.pronamespace = issuer.relnamespace AND writer.proname = 'kernel_root_self_insert_v3'
                        AND writer.pronargs = 0) AS "Installed"
                FROM "FoundationRootIssuer" root_key
                JOIN pg_catalog.pg_class issuer ON issuer.oid = root_key.tableoid
                """).SingleAsync(token);
        }
        if (!install.Installed)
        {
            if (db.RequiredFormat == StoreFormat.RootShipped || db.RequiredFormat == StoreFormat.RootAuthoredEventsQualification)
                throw new StoreFormatException("The shipped store requires its installed private inbox view and insert trigger.");
            await new PostgresRootAdmission(create, TimeSpan.Zero).InstallAsync(token);
        }
        RootSelfIssuer = new(seed => FoundationSelfSend.Handle(install.Key, seed), db.UsesRootTimers, db.UsesRootPrivateParkInputs,
            db.UsesRootTimerRearm ? CheckRootTimerRearmsAsync : null);
    }

    // The ordinary writer transports an exceptional payload as base64. Only the compact bytea is stored.
    // Ordinary updates and deletes on -1 keys stay available for every other persisted message kind.
    internal static async Task InstallRootSelfAsync(KernelDb db, CancellationToken token)
    {
        await db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FoundationSelfContent" bytea
            """, token);
        if (await db.Database.SqlQuery<bool>($"""
            SELECT EXISTS (SELECT 1 FROM "Messages" WHERE "InboxScope" = {RootSelfScope}
                AND "EventMatch" IS NOT NULL AND "FoundationSelfContent" IS NOT NULL) AS "Value"
            """).SingleAsync(token))
            throw SplitEventPayload();

        var parkColumns = db.UsesRootPrivateParkInputs ? """, "FoundationKeptBy", "FoundationReleasedBy" """ : "";
        await db.Database.ExecuteSqlRawAsync($"""
            CREATE OR REPLACE FUNCTION kernel_root_self_insert_v3() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE head text := split_part(NEW."Payload", chr(10), 1);
            BEGIN
                IF NEW."EventMatch" IS NULL AND left(head, 1) = 'B' THEN
                    NEW."FoundationSelfContent" := decode(substr(NEW."Payload", length(head) + 2), 'base64');
                    NEW."Payload" := head;
                END IF;
                RETURN NEW;
            END $fn$;
            CREATE OR REPLACE TRIGGER kernel_root_self_insert BEFORE INSERT ON "Messages" FOR EACH ROW
            WHEN (NEW."InboxScope" = {RootSelfScope} AND NEW."EventMatch" IS NULL) EXECUTE FUNCTION kernel_root_self_insert_v3();
            CREATE OR REPLACE VIEW "FoundationRootMessages" AS
            SELECT "Id", "InboxScope", "EventMatch", "Seq", "Partition", "Target", "Kind", "Start", "Sender", "Due",
                   "Seen", "Trial", "Attempts", "KeepUntil", "Done", "ReplyTo",
                   CASE WHEN "FoundationSelfContent" IS NULL THEN "Payload"
                        ELSE "Payload" || chr(10) || encode("FoundationSelfContent", 'base64') END AS "Payload", "JobGeneration"{parkColumns}
              FROM "Messages";
            """, token);
        if (db.UsesRootPrivateParkInputs)
            await db.Database.ExecuteSqlRawAsync($"""
                CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_ParkTimers" ON "Messages" {ParkTimerIndex};
                CREATE INDEX IF NOT EXISTS "IX_FoundationRootInbox_TerminalSelf" ON "Messages" {TerminalRootSelfIndex(db)};
                """, token);
    }

    private static StoreFormatException SplitEventPayload() => new(
        "A matched event has a split payload from an older private inbox trigger. Restore its exact original envelope from the event source before resuming.");

    private static string? SelfActor(InboxKey key)
    {
        if (key.Scope != -1 || key.Match is not null || !key.Id.StartsWith('c')) return null;
        var cut = key.Id.IndexOf('_');
        if (cut < 2 || !int.TryParse(key.Id.AsSpan(1, cut - 1), NumberStyles.None, CultureInfo.InvariantCulture, out var length) ||
            length <= 0 || length > key.Id.Length - cut - 1) return null;
        var actor = key.Id.Substring(cut + 1, length);
        var prefix = CreationProtocol.Origin(actor, 1) + "#";
        return key.Id.StartsWith(prefix, StringComparison.Ordinal) && FoundationSelfSend.IsHandle(key.Id[prefix.Length..]) ? actor : null;
    }

    internal Message EncodeRootSelf(string actor, long revision, int ordinal, Message m)
    {
        if (RootSelfIssuer is null || !RootSelfIssuer.Allows(m.Due) || !FoundationSelfSend.IsSelfInput(m, actor, 1) ||
            m.Id != FoundationSelfSend.Issue(RootSelfIssuer.Handle, CreationProtocol.Origin(actor, 1), revision, ordinal, m.Due)) throw RootShape();
        return EncodeRootSelfEnvelope(RootSelfIssuer, m, new(revision, ordinal, m.Kind, m.Due));
    }

    private static Message EncodeRootSelfEnvelope(FoundationSelfIssuer issuer, Message message, RootSelfHeader envelope)
    {
        // Leave ordinary payloads verbatim, including embedded newlines. A missing separator encodes null.
        var escaped = message.Payload is not null && (message.Payload.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(message.Payload));
        var header = (escaped ? "B" : "T") + DurableKeyJson.Write(envelope);
        if (!FoundationMetadataLimits.FitsContent(message.Payload) || Encoding.UTF8.GetByteCount(header) > RootSelfHeaderBytes)
            throw new StoreException(Failure.Definite, "Encoded self input exceeds the root row budget.");
        var content = escaped ? Convert.ToBase64String(RootSelfContent.Encode(message.Payload!)) : message.Payload;
        var body = content is null ? header : string.Concat(header, "\n", content);
        // The existing writer returns the insert's sequence for same-owner handover.
        return new Message(message.InboxKey.Id, message.Target, SelfWire(issuer, message.Due), body, message.Sender, Due: message.Due)
            .WithInboxScope(RootSelfScope);
    }

    private static Message DecodeRootSelf(MessageEntity row, FoundationSelfIssuer? issue)
    {
        var actor = row.InboxScope == RootSelfScope && row.EventMatch is null ? SelfActor(InboxKey.Issued(row.Id)) : null;
        if (issue is null || row.JobGeneration != 0 || actor != row.Target || row.Sender != actor ||
            row.Payload is null || row.Start is not null || row.KeepUntil is not null || row.ReplyTo is not null)
            throw RootShape();
        var cut = row.Payload.IndexOf('\n');
        var header = cut < 0 ? row.Payload : row.Payload[..cut];
        var body = ReadRootSelfHeader(header);
        var id = AuthenticateRootSelfId(issue, actor, row.Id, row.Kind, row.Due, body);
        var payload = cut < 0 ? null : row.Payload[(cut + 1)..];
        if (header[0] == 'B') payload = RootSelfContent.Decode(Convert.FromBase64String(payload ?? throw RootShape()));
        return FoundationSelfSend.Create(actor, 1, id, body.Kind, payload, body.Due);
    }

    private static RootSelfHeader ReadRootSelfHeader(string header)
    {
        if (header.Length < 2 || header[0] is not ('B' or 'T')) throw RootShape();
        return DurableKeyJson.Read<RootSelfHeader>(header[1..]) ?? throw RootShape();
    }

    private static string AuthenticateRootSelfId(FoundationSelfIssuer issue, string actor, string storageId, string kind, long due,
        RootSelfHeader body)
    {
        var origin = CreationProtocol.Origin(actor, 1);
        var prefix = origin + "#";
        if (!storageId.StartsWith(prefix, StringComparison.Ordinal) || !issue.Allows(body.Due) ||
            due != (body.Due ?? 0) || kind != SelfWire(issue, body.Due) ||
            body.Revision < 1 || body.Ordinal < 0 || body.Kind is null || !FoundationMetadataLimits.IsOrdinaryKind(body.Kind)) throw RootShape();
        var id = storageId[prefix.Length..];
        if (body.Rearmed
                ? !issue.RearmsTimers || body.Ordinal != 0 || body.Due is null ||
                    !FoundationSelfSend.HasTimerTag(issue.Handle, origin, id)
                : id != FoundationSelfSend.Issue(issue.Handle, origin, body.Revision, body.Ordinal, body.Due))
            throw RootShape();
        return id;
    }

    private static string SelfWire(FoundationSelfIssuer issue, long? due) =>
        issue.DistinguishesTimers && due is not null ? RootSelfTimerWire : RootSelfWire;
}
