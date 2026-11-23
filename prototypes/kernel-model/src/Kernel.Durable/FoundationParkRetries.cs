using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace Kernel.Durable;

internal sealed record ParkRetryOpen(IReadOnlyList<ParkPageCursor> Marked, IReadOnlyList<InboxKey> TimerRearms,
    IReadOnlyList<InboxKey> AlreadyRearmed);

public sealed partial class InMemoryStore
{
    internal const int MaxRetryHandles = 500;
    internal const int MaxRetryHandleBytes = 256 * 1024;
    private const int MaxRetryKeyUnits = 2 * FoundationMetadataLimits.NameBytes + 160;
    private const int MaxRetryMatchUnits = 64;
    private const ushort DigestKey = ushort.MaxValue - 1;
    private const int RetryHeaderBytes = 2 * sizeof(long) + 2 * sizeof(ushort);
    private const int MaxRetryPlainBytes = RetryHeaderBytes + 2 * (MaxRetryKeyUnits + MaxRetryMatchUnits);
    internal const int MaxRetryTokenChars = (MaxRetryPlainBytes + CursorNonceBytes + CursorTagBytes + 2) / 3 * 4;

    // The stored Park and exact row incarnation qualify a handle for a later operator retry.
    internal string? SealFoundationKeptHandle(string tenant, string reference, InboxKey key)
    {
        lock (_gate)
        {
            if (BoundActor(tenant, reference) is not { } actor || actor.Row.Status != InstanceStatus.Faulted ||
                actor.Row.ParkDiscard is not null || !_foundationParks.TryGetValue(actor.Binding.Address, out var park))
                return null;
            var row = FoundationParkDelivery(actor.Row, key);
            var disposition = ReadTimerDispositions(actor.Row.ParkTimerDispositions).GetValueOrDefault(FoundationTimerDigest(key));
            var timer = IsFoundationTimerKey(actor.Row, key, row, disposition);
            if (row is not { Done: true, KeptBy: not null } &&
                !(timer && disposition != 0))
                return null;

            var (id, scope, match) = key;
            if (id.Length == 0) return null;
            // A re-arm replaces a timer row at the same minted key. Every other handle names one row incarnation.
            var useDigest = id.Length > MaxRetryKeyUnits || match?.Length > MaxRetryMatchUnits;
            if (useDigest && timer) return null;
            var idBytes = useDigest ? FoundationEventIdentity.RetryKey(key) : DurableKeyJson.CodeUnits(id);
            var matchBytes = useDigest || match is null ? [] : DurableKeyJson.CodeUnits(match);
            var plain = new byte[RetryHeaderBytes + idBytes.Length + matchBytes.Length];
            BinaryPrimitives.WriteInt64LittleEndian(plain, timer ? 0 : row!.Seq);
            BinaryPrimitives.WriteInt64LittleEndian(plain.AsSpan(sizeof(long)), scope);
            BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(2 * sizeof(long)), (ushort)idBytes.Length);
            BinaryPrimitives.WriteUInt16LittleEndian(plain.AsSpan(RetryHeaderBytes - sizeof(ushort)),
                useDigest ? DigestKey : match is null ? ushort.MaxValue : (ushort)matchBytes.Length);
            idBytes.CopyTo(plain, RetryHeaderBytes);
            matchBytes.CopyTo(plain, RetryHeaderBytes + idBytes.Length);
            return SealReadCursor(RetryHandleContext(tenant, reference, actor.Row, park), plain);
        }
    }

    internal static bool FitsFoundationRetryHandles(IReadOnlyList<string>? handles)
    {
        if (handles is not { Count: > 0 and <= MaxRetryHandles }) return false;
        var bytes = 0;
        foreach (var handle in handles)
        {
            if (handle is not { Length: > 0 and <= MaxRetryTokenChars }) return false;
            bytes += Encoding.UTF8.GetByteCount(handle);
            if (bytes > MaxRetryHandleBytes) return false;
        }
        return true;
    }

    internal ParkRetryOpen? PlanFoundationRetry(InstanceRow actor, string tenant, string reference, FoundationParkMark park,
        IReadOnlyList<string>? handles, IReadOnlyList<ParkTimerRecipe> timerRecipes)
    {
        if (!FitsFoundationRetryHandles(handles) || timerRecipes.Count > MaxRestoreWaits) return null;
        lock (_gate)
        {
            var address = new CreationAddress(actor.Id, actor.Creation);
            if (_foundationParks.GetValueOrDefault(address) != park ||
                BoundActor(tenant, reference) is not { } bound || bound.Binding.Address != address ||
                bound.Row.Status != InstanceStatus.Faulted || bound.Row.ParkDiscard is not null ||
                bound.Row.Revision != actor.Revision)
                return null;

            var context = RetryHandleContext(tenant, reference, actor, park);
            var dispositions = ReadTimerDispositions(actor.ParkTimerDispositions);
            var origin = CreationProtocol.Origin(actor.Id, actor.Creation);
            var timers = timerRecipes.Where(recipe => IsTimerHandleShape(recipe.Id))
                .Select(recipe => InboxKey.Issued(origin + "#" + recipe.Id)).ToHashSet();
            var jobs = CurrentParkJobKeys(actor);
            var seen = new HashSet<InboxKey>();
            var marked = new List<ParkPageCursor>();
            var rearms = new List<InboxKey>();
            var alreadyRearmed = new List<InboxKey>();
            var decoded = new List<(ParkPageCursor Cursor, byte[]? Digest)>(handles!.Count);
            var indirectSequences = new HashSet<long>();
            foreach (var handle in handles!)
            {
                if (!TryOpenRetryHandle(context, handle, out var cursor, out var digest)) return null;
                if (digest is not null) indirectSequences.Add(cursor.Seq);
                decoded.Add((cursor, digest));
            }
            var keptBySequence = indirectSequences.Count == 0 ? null : Messages.Values.Where(row =>
                    indirectSequences.Contains(row.Seq) && row is { Done: true, KeptBy: ParkKeepReason.Consumed } &&
                    row.Message.Target.Id == actor.Id && row.Message.TargetCreation == actor.Creation)
                .ToLookup(row => row.Seq);
            foreach (var (decodedCursor, digest) in decoded)
            {
                var cursor = decodedCursor;
                if (digest is not null)
                {
                    var matches = keptBySequence![cursor.Seq].Select(row => row.Message.InboxKey)
                        .Where(key => key.Scope == cursor.Key.Scope && FoundationEventIdentity.RetryKey(key).AsSpan().SequenceEqual(digest))
                        .Take(2).ToArray();
                    if (matches is not [var resolvedKey]) return null;
                    cursor = cursor with { Key = resolvedKey };
                }
                if (!seen.Add(cursor.Key)) return null;
                var row = FoundationParkDelivery(actor, cursor.Key);
                if (cursor.Seq == 0)
                {
                    var disposition = dispositions.GetValueOrDefault(FoundationTimerDigest(cursor.Key));
                    if (!IsFoundationTimerKey(actor, cursor.Key, row, disposition)) return null;
                    if (disposition != 0 && !timers.Contains(cursor.Key)) return null;
                    if (disposition == SuppressedTimer)
                    {
                        if (row is { Done: false }) return null;
                        rearms.Add(cursor.Key);
                        continue;
                    }
                    if (disposition == RearmedTimer && row is { Done: false } or { KeptBy: ParkKeepReason.Arrived })
                    {
                        alreadyRearmed.Add(cursor.Key);
                        continue;
                    }
                }
                else if (row?.Seq != cursor.Seq) return null;

                if (row is not { Done: true, KeptBy: ParkKeepReason.Consumed } ||
                    IsObsoleteParkJob(row.Message, cursor.Key, jobs))
                    return null;
                marked.Add(cursor with { Seq = row.Seq });
            }
            return new(marked, rearms, alreadyRearmed);
        }
    }

    private MessageRow? FoundationParkDelivery(InstanceRow actor, InboxKey key) =>
        Messages.GetValueOrDefault(key) is { } row && row.Message.Target.Id == actor.Id &&
        row.Message.TargetCreation == actor.Creation ? row : null;

    private static byte[] RetryHandleContext(string tenant, string reference, InstanceRow actor, FoundationParkMark park) =>
        Encoding.UTF8.GetBytes(DurableKeyJson.Write(new
        {
            Read = "park-kept-v1", Tenant = tenant, Reference = reference, Actor = actor.Id, actor.Creation, park.Revision, park.At
        }));

    private bool TryOpenRetryHandle(ReadOnlySpan<byte> context, string token, out ParkPageCursor cursor, out byte[]? digest)
    {
        cursor = default;
        digest = null;
        if (token.Length % 4 != 0) return false;
        var padding = token.EndsWith("==", StringComparison.Ordinal) ? 2 : token.EndsWith('=') ? 1 : 0;
        var size = token.Length / 4 * 3 - padding - CursorNonceBytes - CursorTagBytes;
        if (size <= RetryHeaderBytes || size > MaxRetryPlainBytes) return false;
        Span<byte> plain = stackalloc byte[size];
        if (!TryOpenReadCursor(context, token, plain)) return false;

        var sequence = BinaryPrimitives.ReadInt64LittleEndian(plain);
        var scope = BinaryPrimitives.ReadInt64LittleEndian(plain[sizeof(long)..]);
        var idSize = BinaryPrimitives.ReadUInt16LittleEndian(plain[(2 * sizeof(long))..]);
        var matchSize = BinaryPrimitives.ReadUInt16LittleEndian(plain[(RetryHeaderBytes - sizeof(ushort))..]);
        if (matchSize == DigestKey)
        {
            if (sequence <= 0 || idSize != SHA256.HashSizeInBytes || size != RetryHeaderBytes + idSize) return false;
            digest = plain[RetryHeaderBytes..].ToArray();
            cursor = new(sequence, new("", scope));
            return true;
        }
        var hasMatch = matchSize != ushort.MaxValue;
        if (sequence < 0 || (idSize & 1) != 0 || idSize is 0 or > 2 * MaxRetryKeyUnits ||
            hasMatch && (matchSize > 2 * MaxRetryMatchUnits || (matchSize & 1) != 0) ||
            size != RetryHeaderBytes + idSize + (hasMatch ? matchSize : 0))
            return false;
        var id = DurableKeyJson.FromCodeUnits(plain.Slice(RetryHeaderBytes, idSize).ToArray());
        var match = hasMatch ? DurableKeyJson.FromCodeUnits(plain[(RetryHeaderBytes + idSize)..].ToArray()) : null;
        cursor = new(sequence, new(id, scope, match));
        return true;
    }

    private bool IsFoundationTimerKey(InstanceRow row, InboxKey key, MessageRow? stored, byte disposition)
    {
        var origin = CreationProtocol.Origin(row.Id, row.Creation);
        if (key.Scope != InboxKey.Issued("").Scope || key.Match is not null ||
            key.Id.Length != origin.Length + 66 || key.Id[origin.Length] != '#' ||
            !key.Id.StartsWith(origin, StringComparison.Ordinal)) return false;
        var id = key.Id[(origin.Length + 1)..];
        if (!IsTimerHandleShape(id)) return false;
        if (TimerHandleMatches(origin, id) || disposition != 0) return true;
        // Pre-tag handles still have the stored issued timer as creation evidence before sweep.
        return stored?.Message is { ActorAuthored: true, Due: not null, Start: null, ReplyTo: null } message &&
            message.Id == id && message.Sender == row.Id && message.SenderCreation == row.Creation &&
            !message.Kind.StartsWith("kernel.", StringComparison.Ordinal) &&
            message.Kind is not (Kinds.Start or Kinds.Event or Kinds.JobFinished or Kinds.ChildFinished);
    }

    private static bool IsTimerHandleShape(string? id) => id is { Length: 65 } && id[0] == '~' &&
        id.AsSpan(1).IndexOfAnyExcept("0123456789ABCDEF") < 0;

    private static string FoundationTimerDigest(InboxKey key) =>
        Convert.ToHexString(SHA256.HashData(DurableKeyJson.CodeUnits(key.Id)));

    private void ApplyFoundationRetryOpen(ParkRetryOpen? plan)
    {
        if (plan is null) return;
        foreach (var row in plan.Marked)
            Messages[row.Key].KeptBy = ParkKeepReason.Selected;
    }

    private void RevertFoundationRetryMarks(InstanceRow row)
    {
        // Only Open can mark Selected, and its validated selection has at most MaxRetryHandles entries.
        foreach (var selected in Messages.Values.Where(message => message.KeptBy == ParkKeepReason.Selected &&
                     message.Message.Target.Id == row.Id && message.Message.TargetCreation == row.Creation).Take(MaxRetryHandles))
            selected.KeptBy = ParkKeepReason.Consumed;
    }
}
