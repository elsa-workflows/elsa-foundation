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
    public static void RejectUnsupported(CommitBatch batch)
    {
        if (batch.Turns.Any(t => t.Row?.CommitTimes is not null))
            throw new StoreException(Failure.Definite, "store has no qualified commit clock");
    }

    internal static InstanceRow Stamp(InstanceRow row, long now) => row.CommitTimes is null ? row : row with
    { CommitTimes = row.CommitTimes.ToDictionary(p => p.Key, p => p.Value ?? now, StringComparer.Ordinal) };
}

internal sealed class TurnClock(InstanceRow? row, long revision, ITurnServices services) : ICommitClock
{
    private Dictionary<string, long?>? _times = row?.CommitTimes is { } times ? new(times, StringComparer.Ordinal) : null;
    public bool Available => services.CommitClockAvailable;
    public void Require()
    {
        if (!Available) throw new NotSupportedException("store has no qualified commit clock");
        _times ??= new(StringComparer.Ordinal);
    }
    public string Capture()
    {
        Require();
        var reference = "turn:" + revision;
        _times!.TryAdd(reference, null);
        return reference;
    }
    public long? Resolve(string reference) => _times?.GetValueOrDefault(reference);
    public void Release(string reference)
    {
        Require();
        if (!reference.StartsWith("turn:", StringComparison.Ordinal))
            throw new ArgumentException("Only model-owned clock facts can be released.", nameof(reference));
        _times!.Remove(reference);
    }
    internal Dictionary<string, long?>? Snapshot() => _times is null ? null : new(_times, StringComparer.Ordinal);
}
