using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace Kernel.Durable;

internal sealed record FoundationSelfIssuer(Func<string, string> Handle, bool SupportsTimers, bool DistinguishesTimers = false,
    Func<string, int, IReadOnlyList<ParkTimerRecipe>, CancellationToken, ValueTask<bool>>? CheckTimerRearms = null)
{
    internal bool RearmsTimers => CheckTimerRearms is not null;
    internal bool Allows(long? due) => due is null || SupportsTimers;
}

internal static class FoundationSelfSend
{
    private sealed record AuthoredSend(long Revision, int Ordinal);
    private static readonly ConditionalWeakTable<Message, AuthoredSend> AuthoredSends = new();

    internal static void RememberIssuance(Message message, long revision, int ordinal) => AuthoredSends.Add(message, new(revision, ordinal));

    // Issuance belongs to the original turn output. It does not change the delivery envelope or its equality.
    internal static int? AuthoredOrdinal(Message message) =>
        AuthoredSends.TryGetValue(message, out var authored) ? authored.Ordinal : null;

    internal static long? AuthoredRevision(Message message) =>
        AuthoredSends.TryGetValue(message, out var authored) ? authored.Revision : null;

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

    internal static bool HasTimerTag(Func<string, string> handle, string origin, string id) =>
        IsHandle(id) && CryptographicOperations.FixedTimeEquals(Convert.FromHexString(id.AsSpan(33)),
            Convert.FromHexString(TimerTag(handle, origin, id[1..33])));

    internal static bool IsHandle(string id) => id.Length == 65 && id[0] == '~' && id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") < 0;

    internal static Message Create(string actor, long creation, string id, string kind, string? payload, long? due = null) =>
        new Message(id, new(actor), kind, payload, actor, Due: due, TargetCreation: creation, SenderCreation: creation)
            { ActorAuthored = true, StorageId = CreationProtocol.Origin(actor, creation) + "#" + id }.AsIssued();

    internal static bool IsSelfInput(Message message, string actor, long creation) =>
        IsHandle(message.Id) && FoundationMetadataLimits.IsOrdinaryKind(message.Kind) &&
        message == Create(actor, creation, message.Id, message.Kind, message.Payload, message.Due);
}
