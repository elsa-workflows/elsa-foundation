using Elsa.Persistence.Schema.SchemaFinalization;
using Microsoft.Extensions.Primitives;

namespace Elsa.Persistence.EntityFramework.Tests;

/// <summary>
/// One member of a <see cref="FakeFleetState"/>: what it reads of each family, for which database, and whether a reader
/// counts it. It is counted only once it has published, exactly as a real provider shows a member only after its
/// report is visible.
/// </summary>
internal sealed class FakeMember(string hostId)
{
    public string HostId { get; } = hostId;

    public string Incarnation { get; set; } = Guid.NewGuid().ToString("N");

    public Dictionary<string, (string[] Readable, string? Identity)> Reads { get; } = new(StringComparer.Ordinal);

    public bool Published { get; set; }

    public bool Live { get; set; } = true;

    public bool Lapsed { get; set; }

    public bool ReportUnknown { get; set; }

    /// <summary>
    /// The finalized version this member reports having observed, per family, where a test states it (spec 186, MR-001);
    /// otherwise what <see cref="Observations"/>, the member's own gates' record, says.
    /// </summary>
    public Dictionary<string, string?> Observed { get; } = new(StringComparer.Ordinal);

    /// <summary>What this member's gates observed, which its report carries unless <see cref="Observed"/> says otherwise.</summary>
    public EfSchemaFinalizationObservations? Observations { get; set; }

    public FakeMember Reading(string family, params string[] readable)
    {
        Reads[family] = (readable, null);
        return this;
    }

    public string? ObservedVersionOf(string family) =>
        Observed.TryGetValue(family, out var observed) ? observed : Observations?.Find(family).ObservedFinalizedVersion;

    public override string ToString() => $"{HostId} ({Incarnation[..6]})";
}

/// <summary>The one fleet every <see cref="FakeFleet"/> of a test reads, as a durable provider's store would be.</summary>
internal sealed class FakeFleetState
{
    private readonly object _lock = new();
    private readonly List<FakeMember> _members = [];
    private CancellationTokenSource _changes = new();

    public FakeMember Add(FakeMember member)
    {
        lock (_lock)
            _members.Add(member);
        return member;
    }

    /// <summary>Whether every counted member reads <paramref name="version"/>, as spec 183's FR-023 counts them.</summary>
    public EfSchemaFleetAnswer Count(string family, string version, string databaseIdentity) =>
        Answer(family, databaseIdentity, (member, readable) =>
            readable.Contains(version, StringComparer.Ordinal) ? null : $"{member} reads [{string.Join(", ", readable)}]");

    /// <summary>The backfill's settle condition (spec 186, FR-012): every counted member reports an observed finalized version among <paramref name="versions"/>.</summary>
    public EfSchemaFleetAnswer CountObserving(string family, IReadOnlyList<string> versions, string databaseIdentity) =>
        Answer(family, databaseIdentity, (member, _) =>
            member.ObservedVersionOf(family) is { } observed && versions.Contains(observed, StringComparer.Ordinal)
                ? null
                : $"{member} has observed [{member.ObservedVersionOf(family) ?? "nothing"}]");

    public void SignalChange()
    {
        CancellationTokenSource previous;
        lock (_lock)
            (previous, _changes) = (_changes, new CancellationTokenSource());
        previous.Cancel();
    }

    public IChangeToken ChangeToken()
    {
        lock (_lock)
            return new CancellationChangeToken(_changes.Token);
    }

    /// <summary>
    /// Asks <paramref name="blocker"/> of every published, live member whose report speaks for the database, and lists
    /// what each that fails it says. A member whose report is unknown fails whatever is asked; one whose report has no
    /// entry for the family, or only one for another database, is not counted.
    /// </summary>
    private EfSchemaFleetAnswer Answer(string family, string databaseIdentity, Func<FakeMember, string[], string?> blocker)
    {
        lock (_lock)
        {
            var blockers = new List<string>();
            foreach (var member in _members.Where(member => member is { Published: true, Live: true }))
            {
                if (member.ReportUnknown)
                    blockers.Add($"{member} (unknown)");
                else if (member.Reads.TryGetValue(family, out var entry) && (entry.Identity is null || entry.Identity == databaseIdentity) &&
                         blocker(member, entry.Readable) is { } failed)
                    blockers.Add(failed);
            }

            return new EfSchemaFleetAnswer(blockers.Count == 0, blockers);
        }
    }
}

/// <summary>
/// One member's view of a <see cref="FakeFleetState"/>. Every read is fresh. <see cref="BeforeCount"/> and
/// <see cref="BeforePublish"/> let a test run something at exactly the point the gate reads or publishes.
/// </summary>
internal sealed class FakeFleet(FakeFleetState state, FakeMember self) : IEfSchemaFleet
{
    public FakeMember Self { get; } = self;

    public Func<int, Task>? BeforeCount { get; set; }

    public Func<Task>? BeforePublish { get; set; }

    public Func<Task>? AfterCount { get; set; }

    public int Counts { get; private set; }

    public int Publishes { get; private set; }

    public bool FailPublish { get; set; }

    public TimeSpan SettleMargin { get; set; } = TimeSpan.Zero;

    public EfSchemaFleetStanding GetLocalStanding() => new(new SchemaFinalizationMember(Self.HostId, Self.Incarnation), Self.Lapsed);

    public async ValueTask PublishAsync(CancellationToken cancellationToken = default)
    {
        if (BeforePublish is { } before)
            await before().WaitAsync(cancellationToken);
        if (FailPublish)
            throw new InvalidOperationException($"{Self} has not joined.");
        Publishes++;
        Self.Published = true;
    }

    public async ValueTask<EfSchemaFleetAnswer> CountAsync(string family, string version, string databaseIdentity, CancellationToken cancellationToken = default)
    {
        var count = ++Counts;
        if (BeforeCount is { } before)
            await before(count).WaitAsync(cancellationToken);
        var answer = state.Count(family, version, databaseIdentity);
        if (AfterCount is { } after)
            await after();
        return answer;
    }

    public ValueTask<EfSchemaFleetAnswer> CountObservingAsync(string family, IReadOnlyList<string> versions, string databaseIdentity, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(state.CountObserving(family, versions, databaseIdentity));

    public IChangeToken GetChangeToken() => state.ChangeToken();
}
