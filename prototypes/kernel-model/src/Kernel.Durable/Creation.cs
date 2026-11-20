using Kernel.Runtime;

namespace Kernel.Durable;

/// <summary>An exact actor creation. Zero is the explicitly unqualified legacy protocol.</summary>
public sealed record CreationAddress(string Id, long Creation);

public sealed record CreationResult(string Actor, string Result, long? Requested, long? Current);

/// <summary>Prototype shared lifecycle rules. The execution model never owns the creation header.</summary>
public static class CreationProtocol
{
    // Length-prefix the full physical address: neither user punctuation nor a different parent partition aliases it.
    public static string Origin(string actor, long creation) => creation > 0 ? FormattableString.Invariant($"c{actor.Length}_{actor}_{creation}") : actor;

    public static bool HasTerminalNoticeId(Message message, string child, long creation) =>
        !string.IsNullOrEmpty(child) && creation > 0 && message.InboxKey == InboxKey.Issued(message.Id) &&
        IsCompatibleTerminalHandle(message.Id, Origin(child, creation));

    internal static bool IsTerminalHandle(string id, string origin)
    {
        if (!id.StartsWith(origin, StringComparison.Ordinal) || !id.EndsWith(".end", StringComparison.Ordinal) ||
            id.Length <= origin.Length + 5 || id[origin.Length] != '#') return false;
        var revision = id.AsSpan(origin.Length + 1, id.Length - origin.Length - 5);
        return long.TryParse(revision, System.Globalization.NumberStyles.AllowLeadingSign,
            System.Globalization.CultureInfo.InvariantCulture, out var value) &&
            revision.SequenceEqual(value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    // Older writers used CurrentCulture without persisting its negative sign. Keep the issued
    // key unchanged. This is a reader compatibility grammar, never a spelling for new emission.
    internal static bool IsCompatibleTerminalHandle(string id, string origin)
    {
        if (IsTerminalHandle(id, origin)) return true;
        if (!id.StartsWith(origin + "#", StringComparison.Ordinal) || !id.EndsWith(".end", StringComparison.Ordinal)) return false;
        var value = id.AsSpan(origin.Length + 1, id.Length - origin.Length - 5);
        var sign = 0;
        while (sign < value.Length && !char.IsDigit(value[sign])) sign++;
        var magnitude = value[sign..];
        return sign > 0 && magnitude.Length > 0 && magnitude[0] != '0' &&
            ulong.TryParse(magnitude, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var number) &&
            number <= 9223372036854775808UL && magnitude.SequenceEqual(number.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    public static string TerminalNoticeId(string actor, long creation, long revision) => creation > 0
        ? FormattableString.Invariant($"{Origin(actor, creation)}#{revision}.end") : $"{actor}#{revision}.end";

    public static string? Fingerprint(StartSpec? start, string kind = Kinds.Start, string? payload = null) => start?.Creation is null ? null
        : "v1:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json.Write(new { Start = start, Kind = kind, Payload = payload }))));
    public static void RejectUnsupported(CommitBatch batch, bool physicalRetirement = false)
    {
        if (batch.Turns.Any(t => t.Row is { } row && (row.Creation != 0 || row.Retired && !(physicalRetirement && IsPhysicalRetirement(t)) || row.StartIntent is not null || row.ParentCreation != 0 || row.PendingStart is not null) ||
            t.Sent.Any(Qualified) || t.Subscribed.Concat(t.Unsubscribed).Any(Qualified)))
            throw new StoreException(Failure.Definite, "store has no qualified creation identity");
    }

    // A physical marker retains no executable state or positive-generation protocol. Caller named legacy
    // addresses keep their explicit unqualified behavior; generated physical addresses are never recycled.
    public static bool IsPhysicalAddress(string id)
    {
        var slash=id.IndexOf('/');
        if(slash<2 || !int.TryParse(id.AsSpan(1,slash-1),out var partition) || partition<0 ||
            id[1..slash]!=partition.ToString(System.Globalization.CultureInfo.InvariantCulture))return false;
        var suffix=id[(slash+1)..];
        static bool Positive(string value)=>long.TryParse(value,out var serial) && serial>0 &&
            value==serial.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if(id[0]=='e')return Positive(suffix);
        if(id[0]!='g')return false;
        if(suffix.StartsWith("key/",StringComparison.Ordinal))return Positive(suffix[4..]);
        if(!suffix.StartsWith("child/",StringComparison.Ordinal))return false;
        var parts=suffix[6..].Split('.');
        return parts.Length==3 && int.TryParse(parts[0],out var issuer) && issuer>=0 &&
            parts[0]==issuer.ToString(System.Globalization.CultureInfo.InvariantCulture) && Positive(parts[1]) && Positive(parts[2]);
    }

    private static bool IsPhysicalRetirement(TurnCommit turn) => turn.Row is { Creation: 0, Retired: true } row &&
        StringComparer.Ordinal.Equals(turn.Instance,row.Id) && IsPhysicalAddress(row.Id) && row.Status != InstanceStatus.Running && turn.Delete &&
        turn.Expected is { } expected && expected < long.MaxValue && row.Revision == expected + 1 &&
        row == new InstanceRow(row.Id, "", "", row.Status, "null", row.Revision, null, History: History.Minimal, Retired: true);

    private static bool Qualified(StartSpec? spec) => spec?.Creation is not null || (spec?.ParentCreation ?? 0) != 0;
    public static bool Qualified(Message message) => message.SenderCreation != 0 || message.TargetCreation is not null || Qualified(message.Start);
    public static bool Qualified(SubscriptionRow subscription) => subscription.Creation != 0 || Qualified(subscription.Start);
    public static void RejectUnsupported(Message message)
    { if (Qualified(message)) throw new StoreException(Failure.Definite, "store has no qualified creation identity"); }
    public static void RejectUnsupported(SubscriptionRow subscription)
    { if (Qualified(subscription)) throw new StoreException(Failure.Definite, "store has no qualified creation identity"); }

    internal static string? StartError(InstanceRow? previous, StartSpec start, string kind, string? payload, InboxKey message)
    {
        if (start.Creation is not { } creation) return previous?.Creation > 0 ? "IdentityRequired" : null;
        if (creation.Intent is not { Length: > 0 and <= 128 } || creation.Generation < 1 || creation.Previous < 0)
            return "InvalidCreation";
        if (start.Parent is null ? start.ParentCreation != 0 : start.ParentCreation <= 0) return "InvalidParentCreation";
        if (previous is { Retired: false })
            return previous.Creation == creation.Generation && previous.StartIntent == Fingerprint(start, kind, payload)
                ? previous.PendingStart == message ? null : "ExistingCreation" : "StartConflict";
        var high = previous?.Creation ?? 0;
        if (high == long.MaxValue) return "CreationExhausted";
        return creation.Previous == high && creation.Generation == high + 1 ? null : "RetiredOrConflictingCreation";
    }

    internal static string? TargetError(InstanceRow? row, Message message)
    {
        if (row is { Retired: true }) return "RetiredCreation";
        if (message.TargetCreation is { } requested)
            return row is not null && requested == row.Creation && requested > 0 ? null : "WrongCreation";
        return row?.Creation > 0 ? "IdentityRequired" : null;
    }

    public static TurnOutcome Refuse(string actor, InstanceRow? row, IReadOnlyList<Delivery> taken, string reason)
    {
        var outcome = new TurnOutcome(actor) { Consumed = [.. taken], Status = row?.Status, CreationRefused = true };
        foreach (var item in taken)
            outcome.ReplyTo(item, new CreationResult(actor, reason, item.Message.TargetCreation ?? item.Message.Start?.Creation?.Generation, row?.Creation), creationRefusal: true);
        return outcome;
    }

    public static InstanceRow Retire(InstanceRow row) => new(row.Id, "", "", row.Status, "null", row.Revision + 1, null,
        History: History.Minimal, Creation: row.Creation, Retired: true);

    /// <summary>
    /// The creation-header facts a committed row satisfies on its own, with no creator, predecessor or turn in view.
    /// <see cref="Validate"/> refuses every write that would break one, so an observer may refuse a cold row that breaks
    /// one without inventing its history, and the two cannot drift apart: this is the one definition. The first four
    /// rules hold at every write. The last, an active qualified row carries its start intent, is a birth rule: the
    /// new-actor and successor rules establish it and the immutable-header rule carries it to every later revision,
    /// which <see cref="Validate"/> checks against the predecessor rather than re-deriving it (<paramref name="born"/>
    /// is false for a continuation). An observer judging a cold row applies it as the invariant it is: no row the store
    /// has born lacks it, so <paramref name="born"/> is true there. The rules that compare a row with its predecessor
    /// or its turn stay in <see cref="Validate"/>: the immutable header, the advancing high-water mark, a held start
    /// that must be the predecessor's or held in this turn, a held start cleared only by consuming its delivery, and
    /// the retirement shape (judged against the predecessor, so a marker's header is null here).
    /// </summary>
    public static string? HeaderError(InstanceRow row, bool born)
    {
        if (row.Creation < 0 || row.ParentCreation < 0) return "negative creation identity";
        if (row.Creation == 0 && (row.StartIntent is not null || row.ParentCreation != 0 || row.PendingStart is not null)) return "legacy row cannot carry a partial creation header";
        if (row.Creation > 0 && (row.Parent is null ? row.ParentCreation != 0 : row.ParentCreation <= 0)) return "incomplete exact parent address";
        if (row.PendingStart is { } pending && (string.IsNullOrEmpty(pending.Id) || row.Status != InstanceStatus.Running)) return "invalid held start continuation";
        if (born && row is { Retired: false, Creation: > 0 } && string.IsNullOrEmpty(row.StartIntent)) return "qualified row has no start intent";
        return null;
    }

    internal static void Validate(TurnCommit turn, InstanceRow? previous)
    {
        if (previous is { Creation: > 0 } && turn.Delete && turn.Row is not { Retired: true })
            RefuseWrite("qualified Delete must retain the lifecycle high-water mark");
        if (turn.Row is not { } row) return;
        if (HeaderError(row, born: previous is null or { Retired: true }) is { } header) RefuseWrite(header);
        if (row is { Creation: 0, Retired: true } && !IsPhysicalRetirement(turn)) RefuseWrite("legacy row cannot carry a partial creation header");
        if (row.PendingStart is { } pending && (previous is { Retired: false } && previous.PendingStart != pending ||
            previous?.PendingStart != pending && turn.Held?.Contains(pending) != true)) RefuseWrite("invalid held start continuation");
        if (previous?.PendingStart is { } retained && row.PendingStart is null && row.Status == InstanceStatus.Running &&
            !turn.Consumed.Concat(turn.Done).Contains(retained)) RefuseWrite("held start cleared without consuming its delivery");
        if (row.Retired)
        {
            if (!turn.Delete || previous is not { Retired: false } || previous.Creation == 0 && !IsPhysicalAddress(previous.Id) || row.Status == InstanceStatus.Running || row != Retire(previous))
                RefuseWrite("invalid lifecycle retirement");
            return;
        }
        if (previous is { Retired: true, Creation: 0 }) RefuseWrite("physical run identity is retired");
        if (previous is { Retired: true })
        {
            if (previous.Creation == long.MaxValue || row.Creation != previous.Creation + 1)
                RefuseWrite("creation did not advance the retained high-water mark");
        }
        else if (previous is { })
        {
            if (row.Creation != previous.Creation || row.StartIntent != previous.StartIntent || row.ParentCreation != previous.ParentCreation ||
                row.Creation > 0 && row.Parent != previous.Parent)
                RefuseWrite("creation header is immutable");
        }
        else if (row.Creation > 0 && row.Creation != 1)
            RefuseWrite("new actor must begin at creation one");
    }

    private static void RefuseWrite(string error) => throw new StoreException(Failure.Definite, error);
}
