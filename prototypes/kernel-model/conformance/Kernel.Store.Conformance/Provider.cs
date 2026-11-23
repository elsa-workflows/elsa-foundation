using System.Diagnostics;

namespace Kernel.Store.Conformance;

/// <summary>
/// What a storage provider plugs into the conformance suite. The suite reads and writes only through
/// <see cref="IKernelStore"/>; everything here is the part it cannot do in a provider-neutral way.
/// </summary>
public interface IStoreProvider
{
    string Name { get; }

    /// <summary>
    /// A fresh, empty store with <paramref name="partitions"/> partitions, owned by nobody, that shares nothing with
    /// any other store this provider created. Disposing it removes it.
    /// </summary>
    Task<ProvidedStore> CreateAsync(int partitions, StoreSetup setup);
}

/// <param name="BrokenFence">MUTATION: the commit ignores epoch and position. The stress test must fail with it.</param>
/// <param name="Counting">The provider should count what goes on the wire, for <see cref="ProvidedStore.Counter"/>.</param>
/// <param name="Flaws">A defect switched on in a store that has it, for a mutation's witness (the in-memory store's; others ignore it).</param>
public sealed record StoreSetup(bool BrokenFence = false, bool Counting = false, Flaw Flaws = Flaw.None);

public abstract class ProvidedStore : IAsyncDisposable
{
    /// <summary>The store the workers use. Its connections are the ones <see cref="KillAsync"/> breaks.</summary>
    public abstract IKernelStore Store { get; }

    public virtual bool CaseEquivalentTopic => false;
    public virtual bool ProbeUnpairedTopicUnits => Store is InMemoryStore;
    public virtual bool LargeEventKey => false;
    public virtual string? LargestInboxId => null;

    /// <summary>
    /// A store on its own client (its own connections), for the outside world and for reading the end state. Each call
    /// may return a new client, so that adds come "from another node".
    /// </summary>
    public virtual IKernelStore NewClient() => Store;

    /// <summary>Break one connection of <see cref="Store"/> that is in use, whatever it is doing. Returns how many broke.</summary>
    public virtual Task<int> KillAsync(Random random) => Task.FromResult(0);

    /// <summary>How this provider reports a missing consumed row, when it has a specific expected outcome.</summary>
    public virtual string? MissingConsumeOutcome => null;

    /// <summary>A variant of <paramref name="row"/> that this store refuses every time (a value too long), or null if it cannot make one.</summary>
    public virtual InstanceRow? Refused(InstanceRow row) => null;

    /// <summary>
    /// Another writer holds the partition record inside an open transaction until the returned handle is disposed, or
    /// null if the provider cannot do that.
    /// </summary>
    public virtual Task<IAsyncDisposable?> HoldPartitionAsync(int partition) => Task.FromResult<IAsyncDisposable?>(null);

    /// <summary>A client for the commit that contends with <see cref="HoldPartitionAsync"/>.</summary>
    public virtual IKernelStore PartitionContender() => Store;

    /// <summary>Wait for the attempt to finish or the held lock to block it. Return true with an engine-observed wait.</summary>
    public virtual async Task<bool> ObservePartitionContentionAsync(Task attempt)
    {
        await Task.WhenAny(attempt, Task.Delay(500));
        return false;
    }

    /// <summary>
    /// Open the same database again, as a node that starts does, with <c>partitions</c> partitions (the store's create),
    /// or null when the store has no database to reopen (the in-memory store is made with its count).
    /// </summary>
    public virtual Func<int, Task>? Reopen => null;

    /// <summary>
    /// Remove the partition rows from this id on, as a first open cut short after writing the ones before would leave
    /// them; null when the store writes its partition rows in one transaction, so that cannot happen.
    /// </summary>
    public virtual Func<int, Task>? CutPartitionRows => null;

    /// <summary>
    /// K1b: delete a process's node record, as a store holding a partition row whose owner has no node record would
    /// have it; null when the provider cannot.
    /// </summary>
    public virtual Func<string, Task>? DeleteNodeRecord => null;

    /// <summary>Things the store itself saw go wrong (the in-memory store's ghost list), checked after a stress run.</summary>
    public virtual IReadOnlyList<string> Ghosts => [];

    /// <summary>What the provider's connection killer did, in words.</summary>
    public virtual string Chaos => "";

    /// <summary>What went on the wire, when <see cref="StoreSetup.Counting"/> was asked for.</summary>
    public virtual ICostCounter? Counter => null;

    public abstract ValueTask DisposeAsync();
}

/// <summary>Counts the store's round trips. <see cref="Read"/> says what was counted since <see cref="Reset"/>.</summary>
public interface ICostCounter
{
    void Reset();
    CostReading Read();
}

/// <param name="RoundTrips">Requests that waited for an answer from the store.</param>
/// <param name="Commits">Transactions committed.</param>
/// <param name="Detail">What the round trips were, for instance "find 1, commitTransaction 2".</param>
public sealed record CostReading(int RoundTrips, int Commits, string Detail);

/// <summary>The reference: <see cref="InMemoryStore"/> on a real clock.</summary>
public sealed class InMemoryProvider : IStoreProvider
{
    private static readonly long Origin = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    private static readonly Stopwatch Watch = Stopwatch.StartNew();

    /// <summary>Milliseconds that never go back, starting at the wall clock.</summary>
    public static long Clock() => Origin + Watch.ElapsedMilliseconds;

    public string Name => "in-memory";

    public Task<ProvidedStore> CreateAsync(int partitions, StoreSetup setup) =>
        Task.FromResult<ProvidedStore>(new Provided(new InMemoryStore(partitions, Clock, (setup.BrokenFence ? Flaw.FenceWithoutEpoch | Flaw.FenceWithoutPosition : Flaw.None) | setup.Flaws)));

    private sealed class Provided(InMemoryStore store) : ProvidedStore
    {
        private const string Refuse = "\u0000refuse";

        public override IKernelStore Store => store;
        public override string MissingConsumeOutcome => "unknown";
        public override IReadOnlyList<string> Ghosts { get { lock (store.Ghost) return store.Ghost.ToList(); } }
        public override Func<string, Task>? DeleteNodeRecord => node => { store.Nodes.Remove(node); return Task.CompletedTask; };   // the cases call it with no store call in flight

        public override InstanceRow? Refused(InstanceRow row)
        {
            store.Refuses = r => r.State.StartsWith(Refuse, StringComparison.Ordinal);
            return row with { State = Refuse + row.State };
        }

        public override ValueTask DisposeAsync() => default;
    }
}
