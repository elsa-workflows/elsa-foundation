using System.Security.Cryptography;

namespace Kernel.Durable;

internal static class FoundationSelfSend
{
    internal static string Handle(byte[] issuer, string seed) =>
        "~" + Convert.ToHexString(HMACSHA256.HashData(issuer, DurableKeyJson.CodeUnits(seed)));

    internal static string Issue(Func<string, string> handle, string origin, long revision, int ordinal, long? due)
    {
        var issued = FormattableString.Invariant($"{origin}#{revision}.{ordinal:D4}");
        if (due is null) return handle("send:" + issued);
        var nonce = handle("timer:" + issued)[1..33];
        return "~" + nonce + TimerTag(handle, origin, nonce);
    }

    // The tag lets recovery verify a timer belongs to this creation even after the input has been consumed.
    internal static string TimerTag(Func<string, string> handle, string origin, string nonce) =>
        handle(origin + "#timer:" + nonce)[1..33];

    internal static bool IsHandle(string id) => id.Length == 65 && id[0] == '~' && id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") < 0;

    internal static Message Create(string actor, long creation, string id, string kind, string? payload, long? due = null) =>
        new Message(id, new(actor), kind, payload, actor, Due: due, TargetCreation: creation, SenderCreation: creation)
            { ActorAuthored = true, StorageId = CreationProtocol.Origin(actor, creation) + "#" + id }.AsIssued();

    internal static bool IsSelfInput(Message message, string actor, long creation) =>
        IsHandle(message.Id) && FoundationMetadataLimits.IsOrdinaryKind(message.Kind) &&
        message == Create(actor, creation, message.Id, message.Kind, message.Payload, message.Due);
}
