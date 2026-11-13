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
public sealed record StoreSetup(bool BrokenFence = false, bool Counting = false);

public abstract class ProvidedStore : IAsyncDisposable
{
    /// <summary>The store the workers use. Its connections are the ones <see cref="KillAsync"/> breaks.</summary>
    public abstract IKernelStore Store { get; }

    /// <summary>
    /// A store on its own client (its own connections), for the outside world and for reading the end state. Each call
    /// may return a new client, so that adds come "from another node".
    /// </summary>
    public virtual IKernelStore NewClient() => Store;

    /// <summary>Break one connection of <see cref="Store"/> that is in use, whatever it is doing. Returns how many broke.</summary>
    public virtual Task<int> KillAsync(Random random) => Task.FromResult(0);

    /// <summary>A variant of <paramref name="row"/> that this store refuses every time (a value too long), or null if it cannot make one.</summary>
    public virtual InstanceRow? Refused(InstanceRow row) => null;

    /// <summary>
    /// Another writer holds the partition record inside an open transaction until the returned handle is disposed, or
    /// null if the provider cannot do that.
    /// </summary>
    public virtual Task<IAsyncDisposable?> HoldPartitionAsync(int partition) => Task.FromResult<IAsyncDisposable?>(null);

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
        Task.FromResult<ProvidedStore>(new Provided(new InMemoryStore(partitions, Clock, setup.BrokenFence ? Flaw.FenceWithoutEpoch | Flaw.FenceWithoutPosition : Flaw.None)));

    private sealed class Provided(InMemoryStore store) : ProvidedStore
    {
        private const string Refuse = "\u0000refuse";

        public override IKernelStore Store => store;
        public override IReadOnlyList<string> Ghosts { get { lock (store.Ghost) return store.Ghost.ToList(); } }

        public override InstanceRow? Refused(InstanceRow row)
        {
            store.Refuses = r => r.State.StartsWith(Refuse, StringComparison.Ordinal);
            return row with { State = Refuse + row.State };
        }

        public override ValueTask DisposeAsync() => default;
    }
}
