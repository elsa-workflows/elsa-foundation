namespace Kernel.Durable;

/// <summary>Generic facts about this actor's commits; resolving never creates or restamps one.</summary>
public interface ICommitClock
{
    bool Available { get; }
    void Require();
    string Capture();
    long? Resolve(string reference);
    void Release(string reference);
}

public static class CommitClockFacts
{
    public const string ModelPrefix = "turn:";
    public static void RejectUnsupported(CommitBatch batch)
    {
        if (batch.Turns.Any(t => t.Row?.CommitTimes is not null))
            throw new StoreException(Failure.Definite, "store has no qualified commit clock");
    }

    public static Dictionary<string, long?>? Read(InstanceRow row) => row.CommitTimes is { } json
        ? Json.Read<Dictionary<string, long?>>(json) : null;

    internal static void Validate(InstanceRow row, InstanceRow? before)
    {
        var next = Read(row);
        var old = before is null ? null : Read(before);
        if (old is not null && next is null) throw new StoreException(Failure.Definite, "commit clock opt-in cannot be removed");
        foreach (var (key, value) in next ?? [])
            if (old is not null && old.TryGetValue(key, out var kept) ? value != kept : value is not null)
                throw new StoreException(Failure.Definite, "commit clock fact must be new/unstamped or retained unchanged");
        if (old?.Keys.Any(key => !key.StartsWith(CommitClockFacts.ModelPrefix, StringComparison.Ordinal) && next?.ContainsKey(key) != true) == true)
            throw new StoreException(Failure.Definite, "actor-owned commit clock fact cannot be released");
    }

    internal static InstanceRow Stamp(InstanceRow row, long now) => Read(row) is not { } times ? row : row with
    { CommitTimes = Json.Write(times.ToDictionary(p => p.Key, p => p.Value ?? now, StringComparer.Ordinal)) };
}

internal sealed class TurnClock(InstanceRow? row, long revision, ITurnServices services) : ICommitClock
{
    private Dictionary<string, long?>? _times = row is null ? null : CommitClockFacts.Read(row);
    public bool Available => services.CommitClockAvailable;
    public void Require()
    {
        if (!Available) throw new NotSupportedException("store has no qualified commit clock");
        _times ??= new(StringComparer.Ordinal);
    }
    public string Capture()
    {
        Require();
        var reference = CommitClockFacts.ModelPrefix + revision;
        _times!.TryAdd(reference, null);
        return reference;
    }
    public long? Resolve(string reference) => _times?.GetValueOrDefault(reference);
    public void Release(string reference)
    {
        Require();
        if (!reference.StartsWith(CommitClockFacts.ModelPrefix, StringComparison.Ordinal))
            throw new ArgumentException("Only model-owned clock facts can be released.", nameof(reference));
        _times!.Remove(reference);
    }
    internal string? Snapshot() => _times is null ? null : Json.Write(_times);
}
