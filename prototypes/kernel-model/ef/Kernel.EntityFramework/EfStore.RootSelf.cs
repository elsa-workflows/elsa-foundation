using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Kernel.EntityFramework;

public sealed partial class EfStore
{
    // A private physical envelope, scoped to issued rows. The application kind is carried inside.
    private const string RootSelfWire = "!foundation-self/1";
    private sealed record RootSelfHeader(long Revision, int Ordinal, string Kind, bool Escaped);
    internal Func<string, string>? RootSelfIssuer { get; private set; }

    internal async Task EnableRootSelfSendsAsync(CancellationToken token)
    {
        await using var db = create();
        var key = await db.Database.SqlQueryRaw<byte[]>("""SELECT "Key" AS "Value" FROM "FoundationRootIssuer" """).SingleAsync(token);
        RootSelfIssuer = seed => FoundationSelfSend.Handle(key, seed);
    }

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
        var header = DurableKeyJson.Write(new RootSelfHeader(revision, ordinal, m.Kind, escaped));
        var body = m.Payload is null ? header : string.Concat(header, "\n", escaped ? DurableKeyJson.Write(m.Payload) : m.Payload);
        if (Encoding.UTF8.GetByteCount(body) > FoundationMetadataLimits.MessageContentBytes + 24 * 1024)
            throw new StoreException(Failure.Definite, "Encoded self input exceeds the PostgreSQL row budget.");
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
        if (body.Escaped) payload = DurableKeyJson.Read<string>(payload ?? throw RootShape());
        var id = issue(FoundationSelfSend.Seed(CreationProtocol.Origin(actor, 1), body.Revision, body.Ordinal));
        var message = FoundationSelfSend.Create(actor, 1, id, body.Kind, payload);
        if (body.Revision < 1 || body.Ordinal < 0 || !FoundationMetadataLimits.IsOrdinaryKind(body.Kind) ||
            message.InboxKey.Id != row.Id) throw RootShape();
        return message;
    }
}
