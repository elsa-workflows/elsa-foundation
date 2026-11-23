using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

internal sealed record ParkTimerRecipe(string Id, string Kind, string? Payload, long Due);
internal sealed record ParkTimerRearm(Message Envelope, long? DoneSequence);
internal sealed record ParkTimerOpen(IReadOnlyList<ParkTimerRecipe> Recipes, IReadOnlyList<ParkTimerRearm> Rearms,
    IReadOnlyList<InboxKey> Preserved, string? Dispositions, long Bytes);

public sealed partial class InMemoryStore
{
    internal const int MaxRestoreWaits = 4096;
    private const int MaxRestoreBytes = 24 * 1024 * 1024;
    private const int RestoreWaitAllowance = 5406;
    private const byte SuppressedTimer = 1;
    private const byte RearmedTimer = 2;
    private const int TimerDispositionEntryBytes = 33;

    private bool TimerHandleMatches(string origin, string id) => CryptographicOperations.FixedTimeEquals(
        Convert.FromHexString(id.AsSpan(33)), Convert.FromHexString(FoundationSelfSend.TimerTag(MessageHandle, origin, id[1..33])));

    internal ParkTimerOpen? PlanFoundationTimers(InstanceRow row, ParkRecoveryMode mode, IReadOnlyList<ParkTimerRecipe> recipes, ParkRetryOpen? retry = null)
    {
        if (recipes.Count > MaxRestoreWaits) return null;
        var previous = ReadTimerDispositions(row.ParkTimerDispositions);
        var keys = new HashSet<InboxKey>();
        var rearmSelection = retry?.TimerRearms.ToHashSet();
        var markedSelection = retry?.Marked.Select(row => row.Key).ToHashSet();
        var dispositions = new SortedDictionary<string, byte>(StringComparer.Ordinal);
        var rearms = new List<ParkTimerRearm>();
        var preserved = new List<InboxKey>();
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        var bytes = 0L;
        lock (_gate)
        {
            foreach (var timer in recipes)
            {
                var id = timer.Id;
                if (!IsTimerHandleShape(id) || timer.Kind is null || timer.Kind.StartsWith("kernel.", StringComparison.Ordinal) ||
                    timer.Kind is Kinds.Start or Kinds.Event or Kinds.JobFinished or "child-finished")
                    return null;
                var physical = origin + "#" + id;
                var envelope = new Message(id, new(row.Id), timer.Kind, timer.Payload, row.Id, Due: timer.Due,
                    TargetCreation: row.Creation, SenderCreation: row.Creation)
                { ActorAuthored = true, StorageId = physical }.AsIssued();
                var key = envelope.InboxKey;
                if (!keys.Add(key) || !FoundationMetadataLimits.FitsComposed(envelope)) return null;
                bytes += RestoreWaitAllowance + (timer.Payload is null ? 0 : Encoding.UTF8.GetByteCount(timer.Payload));
                if (bytes > MaxRestoreBytes) return null;

                var stored = Messages.GetValueOrDefault(key);
                var beforeRearms = rearms.Count;
                var digest = FoundationTimerDigest(key);
                var prior = previous.GetValueOrDefault(digest);
                if (stored is not null && !SameTimer(stored, envelope) ||
                    stored is null && prior == 0 && !TimerHandleMatches(origin, id)) return null;
                if (prior == SuppressedTimer)
                {
                    if (stored is { Done: false }) return null;
                    if (mode == ParkRecoveryMode.RetryAll || rearmSelection?.Contains(key) == true) Rearm();
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
                else if (stored.KeptBy == ParkKeepReason.Consumed && mode != ParkRecoveryMode.RetryAll &&
                    markedSelection?.Contains(key) != true)
                {
                    dispositions.Add(digest, SuppressedTimer);
                }
                // Suppression and re-arm history also depend on the absence of a row.
                if (rearms.Count == beforeRearms) preserved.Add(key);

                void Rearm()
                {
                    rearms.Add(new(envelope, stored?.Seq));
                    dispositions.Add(digest, RearmedTimer);
                }
            }
        }
        return new(recipes, rearms, preserved, WriteTimerDispositions(dispositions), bytes);
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
        if (timers is not null) ApplyFoundationRearms(timers.Rearms);
    }

    private void ApplyFoundationRearms(IReadOnlyList<ParkTimerRearm> rearms)
    {
        foreach (var rearm in rearms)
        {
            var key = rearm.Envelope.InboxKey;
            if (rearm.DoneSequence.HasValue) Messages.Remove(key);
            Messages.Add(key, StoreMessage(rearm.Envelope, ++_seq, 0));
        }
    }
}
