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

    public static string? Fingerprint(StartSpec? start, string kind = Kinds.Start, string? payload = null) => start?.Creation is null ? null
        : "v1:" + Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(Json.Write(new { Start = start, Kind = kind, Payload = payload }))));
    public static void RejectUnsupported(CommitBatch batch)
    {
        if (batch.Turns.Any(t => t.Row is { } row && (row.Creation != 0 || row.Retired || row.StartIntent is not null || row.ParentCreation != 0) ||
            t.Sent.Any(Qualified) || t.Subscribed.Concat(t.Unsubscribed).Any(s => s.Creation != 0 || s.Start?.Creation is not null)))
            throw new StoreException(Failure.Definite, "store has no qualified creation identity");
    }

    public static bool Qualified(Message message) => message.TargetCreation is not null || message.Start?.Creation is not null || (message.Start?.ParentCreation ?? 0) != 0;
    public static void RejectUnsupported(Message message)
    { if (Qualified(message)) throw new StoreException(Failure.Definite, "store has no qualified creation identity"); }
    public static void RejectUnsupported(SubscriptionRow subscription)
    { if (subscription.Creation != 0 || subscription.Start?.Creation is not null) throw new StoreException(Failure.Definite, "store has no qualified creation identity"); }

    internal static string? StartError(InstanceRow? previous, StartSpec start, string kind, string? payload)
    {
        if (start.Creation is not { } creation) return previous?.Creation > 0 ? "IdentityRequired" : null;
        if (creation.Intent is not { Length: > 0 and <= 128 } || creation.Generation < 1 || creation.Previous < 0)
            return "InvalidCreation";
        if (start.Parent is null ? start.ParentCreation != 0 : start.ParentCreation <= 0) return "InvalidParentCreation";
        if (previous is { Retired: false })
            return previous.Creation == creation.Generation && previous.StartIntent == Fingerprint(start, kind, payload) ? "ExistingCreation" : "StartConflict";
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
        var outcome = new TurnOutcome(actor) { Consumed = [.. taken], Status = row?.Status };
        foreach (var item in taken)
            if (item.Message.ReplyTo is { } to)
                outcome.Replied.Add(new(item.Message.Id, to, Request.Response,
                    new CreationResult(actor, reason, item.Message.TargetCreation ?? item.Message.Start?.Creation?.Generation, row?.Creation)));
        return outcome;
    }

    public static InstanceRow Retire(InstanceRow row) => new(row.Id, "", "", row.Status, "null", row.Revision + 1, null,
        History: History.Minimal, Creation: row.Creation, Retired: true);

    internal static void Validate(TurnCommit turn, InstanceRow? previous)
    {
        if (previous is { Creation: > 0 } && turn.Delete && turn.Row is not { Retired: true })
            RefuseWrite("qualified Delete must retain the lifecycle high-water mark");
        if (turn.Row is not { } row) return;
        if (row.Creation < 0 || row.ParentCreation < 0) RefuseWrite("negative creation identity");
        if (row.Creation == 0 && (row.Retired || row.StartIntent is not null || row.ParentCreation != 0)) RefuseWrite("legacy row cannot carry a partial creation header");
        if (row.Creation > 0 && (row.Parent is null ? row.ParentCreation != 0 : row.ParentCreation <= 0)) RefuseWrite("incomplete exact parent address");
        if (row.Retired)
        {
            if (!turn.Delete || previous is not { Creation: > 0, Retired: false } || row.Status == InstanceStatus.Running || row != Retire(previous))
                RefuseWrite("invalid lifecycle retirement");
            return;
        }
        if (previous is { Retired: true })
        {
            if (previous.Creation == long.MaxValue || row.Creation != previous.Creation + 1 || string.IsNullOrEmpty(row.StartIntent))
                RefuseWrite("creation did not advance the retained high-water mark");
        }
        else if (previous is { })
        {
            if (row.Creation != previous.Creation || row.StartIntent != previous.StartIntent || row.ParentCreation != previous.ParentCreation ||
                row.Creation > 0 && row.Parent != previous.Parent)
                RefuseWrite("creation header is immutable");
        }
        else if (row.Creation > 0 && (row.Creation != 1 || string.IsNullOrEmpty(row.StartIntent)))
            RefuseWrite("new actor must begin at creation one");
    }

    private static void RefuseWrite(string error) => throw new StoreException(Failure.Definite, error);
}
