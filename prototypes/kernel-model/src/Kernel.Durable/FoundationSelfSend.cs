using System.Security.Cryptography;

namespace Kernel.Durable;

internal static class FoundationSelfSend
{
    internal static string Handle(byte[] issuer, string seed) =>
        "~" + Convert.ToHexString(HMACSHA256.HashData(issuer, DurableKeyJson.CodeUnits(seed)));

    internal static string Seed(string origin, long revision, int ordinal) =>
        FormattableString.Invariant($"send:{origin}#{revision}.{ordinal:D4}");

    internal static bool IsHandle(string id) => id.Length == 65 && id[0] == '~' && id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") < 0;

    internal static Message Create(string actor, long creation, string id, string kind, string? payload) =>
        new Message(id, new(actor), kind, payload, actor, TargetCreation: creation, SenderCreation: creation)
            { ActorAuthored = true, StorageId = CreationProtocol.Origin(actor, creation) + "#" + id }.AsIssued();

    internal static bool IsImmediate(Message message, string actor, long creation) =>
        IsHandle(message.Id) && FoundationMetadataLimits.IsOrdinaryKind(message.Kind) &&
        message == Create(actor, creation, message.Id, message.Kind, message.Payload);
}
