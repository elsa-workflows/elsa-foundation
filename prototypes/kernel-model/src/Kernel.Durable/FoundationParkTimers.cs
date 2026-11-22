using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

internal sealed record ParkTimerRecipe(string Id, string Kind, string? Payload, long Due);
internal sealed record ParkTimerRearm(Message Envelope, long? DoneSequence);
internal sealed record ParkTimerOpen(IReadOnlyList<ParkTimerRecipe> Recipes, IReadOnlyList<ParkTimerRearm> Rearms, string? Dispositions);

public sealed partial class InMemoryStore
{
    internal const int MaxRestoreWaits = 4096;
    private const int MaxRestoreBytes = 24 * 1024 * 1024;
    private const int TimerAllowance = 5406;
    private const byte SuppressedTimer = 1;
    private const byte RearmedTimer = 2;
    private const int TimerDispositionEntryBytes = 33;

    // Keep the existing wire shape. The first half distinguishes sends, and the second proves the
    // handle belongs to this creation even when there is no delivery row left to inspect.
    internal string FoundationTimerHandle(string origin, string issued)
    {
        var nonce = MessageHandle("timer:" + issued)[1..33];
        return "~" + nonce + TimerHandleTag(origin, nonce);
    }

    private string TimerHandleTag(string origin, string nonce) => MessageHandle(origin + "#timer:" + nonce)[1..33];

    private bool TimerHandleMatches(string origin, string id) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(id.AsSpan(33)), Convert.FromHexString(TimerHandleTag(origin, id[1..33])));

    internal ParkTimerOpen? PlanFoundationTimers(InstanceRow row, ParkRecoveryMode mode, IReadOnlyList<ParkTimerRecipe> recipes)
    {
        if (recipes.Count > MaxRestoreWaits) return null;
        var previous = ReadTimerDispositions(row.ParkTimerDispositions);
        var keys = new HashSet<InboxKey>();
        var dispositions = new SortedDictionary<string, byte>(StringComparer.Ordinal);
        var rearms = new List<ParkTimerRearm>();
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        var bytes = 0L;
        lock (_gate)
        {
            foreach (var timer in recipes)
            {
                if (timer.Id is not { Length: 65 } id || id[0] != '~' || id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") >= 0 ||
                    string.IsNullOrEmpty(timer.Kind) || timer.Kind.StartsWith("kernel.", StringComparison.Ordinal) ||
                    timer.Kind is Kinds.Start or Kinds.Event or Kinds.JobFinished or "child-finished")
                    return null;
                var physical = origin + "#" + id;
                var envelope = new Message(id, new(row.Id), timer.Kind, timer.Payload, row.Id, Due: timer.Due,
                    TargetCreation: row.Creation, SenderCreation: row.Creation)
                { ActorAuthored = true, StorageId = physical }.AsIssued();
                var key = envelope.InboxKey;
                if (!keys.Add(key) || !FoundationMetadataLimits.FitsComposed(envelope)) return null;
                bytes += TimerAllowance + (timer.Payload is null ? 0 : Encoding.UTF8.GetByteCount(timer.Payload));
                if (bytes > MaxRestoreBytes) return null;

                var stored = Messages.GetValueOrDefault(key);
                var digest = Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(physical)));
                var prior = previous.GetValueOrDefault(digest);
                if (stored is not null && !SameTimer(stored, envelope) ||
                    stored is null && prior == 0 && !TimerHandleMatches(origin, id)) return null;
                if (prior == SuppressedTimer)
                {
                    if (stored is { Done: false }) return null;
                    if (mode == ParkRecoveryMode.RetryAll) Rearm();
                    else dispositions.Add(digest, SuppressedTimer);
                }
                else if (prior == RearmedTimer)
                {
                    dispositions.Add(digest, RearmedTimer);
                }
                else if (stored is null or { Done: true, KeptBy: null })
                {
                    Rearm();
                }
                else if (stored.KeptBy == ParkKeepReason.Consumed && mode != ParkRecoveryMode.RetryAll)
                {
                    dispositions.Add(digest, SuppressedTimer);
                }

                void Rearm()
                {
                    rearms.Add(new(envelope, stored?.Seq));
                    dispositions.Add(digest, RearmedTimer);
                }
            }
        }
        return new(recipes, rearms, WriteTimerDispositions(dispositions));
    }

    private static bool SameTimer(MessageRow stored, Message envelope)
    {
        var message = stored.Message;
        return message.ActorAuthored && message.Id == envelope.Id && message.Kind == envelope.Kind && message.Due == envelope.Due &&
            message.Sender == envelope.Sender && message.SenderCreation == envelope.SenderCreation &&
            message.Target == envelope.Target && message.TargetCreation == envelope.TargetCreation && message.Start is null && message.ReplyTo is null &&
            (stored is { Done: true, KeptBy: null } || message.Payload == envelope.Payload);
    }

    private static Dictionary<string, byte> ReadTimerDispositions(string? value)
    {
        var result = new Dictionary<string, byte>(StringComparer.Ordinal);
        if (value is null) return result;
        var bytes = Convert.FromBase64String(value);
        for (var offset = 0; offset < bytes.Length; offset += TimerDispositionEntryBytes)
            result.Add(Convert.ToHexString(bytes.AsSpan(offset, 32)), bytes[offset + 32]);
        return result;
    }

    private static string? WriteTimerDispositions(SortedDictionary<string, byte> dispositions)
    {
        if (dispositions.Count == 0) return null;
        var bytes = new byte[dispositions.Count * TimerDispositionEntryBytes];
        var offset = 0;
        foreach (var (digest, disposition) in dispositions)
        {
            Convert.FromHexString(digest).CopyTo(bytes, offset);
            bytes[offset + 32] = disposition;
            offset += TimerDispositionEntryBytes;
        }
        return Convert.ToBase64String(bytes);
    }

    private void ApplyFoundationTimerOpen(ParkTimerOpen? timers)
    {
        if (timers is null) return;
        foreach (var rearm in timers.Rearms)
        {
            var key = rearm.Envelope.InboxKey;
            if (rearm.DoneSequence.HasValue) Messages.Remove(key);
            Messages.Add(key, new(rearm.Envelope, ++_seq, 0));
        }
    }
}
