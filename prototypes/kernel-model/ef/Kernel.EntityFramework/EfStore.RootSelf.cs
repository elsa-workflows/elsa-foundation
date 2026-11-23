using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // A private physical envelope, scoped to issued rows. The application kind is carried inside.
    private const string RootSelfWire = "!foundation-self/1";
    private sealed record RootSelfHeader(long Revision, int Ordinal, string Kind, bool Escaped, bool Binary = false);
    private string RootInboxSource => RootSelfIssuer is null ? "\"Messages\"" : "\"FoundationRootMessages\"";
    internal Func<string, string>? RootSelfIssuer { get; private set; }

    private sealed record SelfInstallation(byte[] Key, bool Installed);

    internal async Task EnableRootSelfSendsAsync(CancellationToken token)
    {
        await using var db = create();
        var install = await db.Database.SqlQueryRaw<SelfInstallation>("""
            SELECT "Key", to_regclass('"FoundationRootMessages"') IS NOT NULL AS "Installed" FROM "FoundationRootIssuer"
            """).SingleAsync(token);
        if (!install.Installed) await new PostgresRootAdmission(create, TimeSpan.Zero).InstallAsync(token);
        RootSelfIssuer = seed => FoundationSelfSend.Handle(install.Key, seed);
    }

    private static Task QualifySelfWriterAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("SELECT set_config('kernel.qualified_self_writer', 'on', true)", token);

    // The ordinary writer transports an exceptional payload as base64. Only the compact bytea is stored.
    // Ordinary updates and deletes on -1 keys stay available for every other persisted message kind.
    internal static Task InstallRootSelfAsync(KernelDb db, CancellationToken token) =>
        db.Database.ExecuteSqlRawAsync("""
            ALTER TABLE "Messages" ADD COLUMN IF NOT EXISTS "FoundationSelfContent" bytea;
            CREATE OR REPLACE FUNCTION kernel_root_self_insert() RETURNS trigger LANGUAGE plpgsql AS $fn$
            DECLARE head text := split_part(NEW."Payload", chr(10), 1);
            BEGIN
                IF (head::jsonb ->> 'Binary')::boolean THEN
                    NEW."FoundationSelfContent" := decode(substr(NEW."Payload", length(head) + 2), 'base64');
                    NEW."Payload" := head;
                END IF;
                RETURN NEW;
            END $fn$;
            CREATE OR REPLACE TRIGGER kernel_root_self_insert BEFORE INSERT ON "Messages" FOR EACH ROW
            WHEN (NEW."InboxScope" = -1 AND NEW."Kind" = '!foundation-self/1') EXECUTE FUNCTION kernel_root_self_insert();
            CREATE OR REPLACE FUNCTION kernel_require_self_writer() RETURNS trigger LANGUAGE plpgsql AS $fn$
            BEGIN
                IF current_setting('kernel.qualified_self_writer', true) IS DISTINCT FROM 'on' THEN
                    RAISE EXCEPTION 'Scoped root input requires its checkpoint writer' USING ERRCODE = '23514';
                END IF;
                IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                RETURN NEW;
            END $fn$;
            CREATE OR REPLACE TRIGGER kernel_require_self_writer BEFORE UPDATE OR DELETE ON "Messages" FOR EACH ROW
            WHEN (OLD."InboxScope" = -1 AND OLD."Kind" = '!foundation-self/1') EXECUTE FUNCTION kernel_require_self_writer();
            CREATE OR REPLACE VIEW "FoundationRootMessages" AS
            SELECT "Id", "InboxScope", "EventMatch", "Seq", "Partition", "Target", "Kind", "Start", "Sender", "Due",
                   "Seen", "Trial", "Attempts", "KeepUntil", "Done", "ReplyTo",
                   CASE WHEN "FoundationSelfContent" IS NULL THEN "Payload"
                        ELSE "Payload" || chr(10) || encode("FoundationSelfContent", 'base64') END AS "Payload"
              FROM "Messages";
            """, token);

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

    private Message EncodeRootSelf(WorkerRoot root, long revision, int ordinal, Message m)
    {
        if (RootSelfIssuer is null || !FoundationSelfSend.IsImmediate(m, root.Actor, 1) ||
            m.Id != RootSelfIssuer(FoundationSelfSend.Seed(CreationProtocol.Origin(root.Actor, 1), revision, ordinal))) throw RootShape();
        // Leave ordinary payloads verbatim, including embedded newlines. A missing separator encodes null.
        var escaped = m.Payload is not null && (m.Payload.Contains('\0') || DurableKeyJson.HasUnpairedSurrogate(m.Payload));
        var header = DurableKeyJson.Write(new RootSelfHeader(revision, ordinal, m.Kind, escaped, Binary: escaped));
        if (!FoundationMetadataLimits.FitsContent(m.Payload) || Encoding.UTF8.GetByteCount(header) > 24 * 1024)
            throw new StoreException(Failure.Definite, "Encoded self input exceeds the PostgreSQL row budget.");
        var content = escaped ? Convert.ToBase64String(RootSelfContent.Encode(m.Payload!)) : m.Payload;
        var body = content is null ? header : string.Concat(header, "\n", content);
        // The existing writer returns the insert's sequence for same-owner handover.
        return new Message(m.InboxKey.Id, m.Target, RootSelfWire, body, m.Sender).AsIssued();
    }

    private static Message DecodeRootSelf(MessageEntity row, Func<string, string>? issue)
    {
        var actor = SelfActor(new(row.Id, row.InboxScope));
        if (issue is null || actor != row.Target || row.Sender != actor || row.Kind != RootSelfWire ||
            row.Payload is null || row.Start is not null || row.KeepUntil is not null || row.ReplyTo is not null || row.Due != 0)
            throw RootShape();
        var cut = row.Payload.IndexOf('\n');
        var body = DurableKeyJson.Read<RootSelfHeader>(cut < 0 ? row.Payload : row.Payload[..cut]);
        var payload = cut < 0 ? null : row.Payload[(cut + 1)..];
        if (body.Binary) payload = RootSelfContent.Decode(Convert.FromBase64String(payload ?? throw RootShape()));
        else if (body.Escaped) payload = DurableKeyJson.Read<string>(payload ?? throw RootShape());
        var id = issue(FoundationSelfSend.Seed(CreationProtocol.Origin(actor, 1), body.Revision, body.Ordinal));
        var message = FoundationSelfSend.Create(actor, 1, id, body.Kind, payload);
        if (body.Revision < 1 || body.Ordinal < 0 || !FoundationMetadataLimits.IsOrdinaryKind(body.Kind) ||
            message.InboxKey.Id != row.Id) throw RootShape();
        return message;
    }
}
