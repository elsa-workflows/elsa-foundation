using System.Collections.Concurrent;
using Kernel.Store.Conformance;
using MongoDB.Bson;
using MongoDB.Driver;
using MongoDB.Driver.Core.Events;

namespace Kernel.Mongo.Run;

/// <summary>
/// The conformance suite's plug for MongoDB. Each store is its own database on one replica set. The workers use one
/// client (application name kernel-worker), whose sessions the connection killer ends; the outside world and the
/// checks use two other clients, as two other nodes.
/// </summary>
public sealed class MongoProvider(string connection, MongoStoreOptions options) : IStoreProvider, IDisposable
{
    private ChaosProxy? _proxy;
    private readonly Lazy<IMongoClient> _worker = new(() => Client(Proxied(connection, out var proxy), "kernel-worker", proxy: proxy));
    private readonly Lazy<IMongoClient[]> _outside = new(() => [Client(connection, "kernel-outside-1"), Client(connection, "kernel-outside-2")]);
    private readonly Lazy<IMongoClient> _admin = new(() => Client(connection, "kernel-admin"));

    private static readonly ConcurrentDictionary<string, ChaosProxy> Proxies = new();
    private ChaosProxy Proxy => _proxy ??= Proxies["kernel-worker"];
    private long _cuts, _ended;

    public string Name => "mongodb" + (options.FenceInBatch ? ", fence in batch" : ", checked fence");

    // The workers' client reaches the server through a proxy that can cut its connections.
    private static string Proxied(string connection, out ChaosProxy proxy)
    {
        var url = new MongoUrlBuilder(connection);
        proxy = new ChaosProxy(url.Server.Host, url.Server.Port);
        url.Server = new MongoServerAddress("127.0.0.1", proxy.Port);
        return url.ToString();
    }

    public static IMongoClient Client(string connection, string application, Action<CommandStartedEvent>? started = null, ChaosProxy? proxy = null)
    {
        if (proxy is not null) Proxies[application] = proxy;
        var settings = MongoClientSettings.FromConnectionString(connection);
        settings.ApplicationName = application;
        settings.ReadConcern = ReadConcern.Majority;
        settings.WriteConcern = WriteConcern.WMajority;
        settings.MaxConnectionPoolSize = 200;
        if (started is not null) settings.ClusterConfigurator = cluster => cluster.Subscribe(started);
        return new MongoClient(settings);
    }

    public async Task<ProvidedStore> CreateAsync(int partitions, StoreSetup setup)
    {
        var database = "kconf_" + Guid.NewGuid().ToString("N")[..12];
        var storeOptions = options with { BrokenFence = setup.BrokenFence };
        var counter = setup.Counting ? new CommandCounter() : null;
        var worker = counter is null ? _worker.Value : Client(connection, "kernel-cost", counter.Started);
        var store = new MongoStore(worker, database, storeOptions);
        await new MongoStore(_admin.Value, database).CreateAsync(partitions);
        return new Provided(this, store, database, counter, worker == _worker.Value ? null : worker);
    }

    public void Dispose()
    {
        if (_worker.IsValueCreated) _worker.Value.Dispose();
        _proxy?.DisposeAsync().AsTask().Wait();
        if (_outside.IsValueCreated) foreach (var c in _outside.Value) c.Dispose();
        if (_admin.IsValueCreated) _admin.Value.Dispose();
    }

    private sealed class Provided(MongoProvider provider, MongoStore store, string database, CommandCounter? counter, IMongoClient? own) : ProvidedStore
    {
        private int _next;

        public override IKernelStore Store => store;
        public override ICostCounter? Counter => counter;

        public override IKernelStore NewClient() => new MongoStore(provider._outside.Value[Interlocked.Increment(ref _next) % 2], database);

        // A document over MongoDB's 16 MiB limit.
        public override InstanceRow? Refused(InstanceRow row) => row with { State = new string('x', 17 * 1024 * 1024) };

        public override async Task<IAsyncDisposable?> HoldPartitionAsync(int partition)
        {
            var session = await provider._admin.Value.StartSessionAsync();
            session.StartTransaction(new TransactionOptions(ReadConcern.Snapshot, ReadPreference.Primary, WriteConcern.WMajority));
            await provider._admin.Value.GetDatabase(database).GetCollection<BsonDocument>("partitions")
                .UpdateOneAsync(session, new BsonDocument("_id", partition), new BsonDocument("$set", new BsonDocument("held", true)));
            return new Hold(session);
        }

        /// <summary>
        /// The counterpart of pg_terminate_backend: end the session of a worker operation that is running, or of a
        /// worker transaction that is open between two of its statements. Its transaction is aborted.
        /// </summary>
        public override async Task<int> KillAsync(Random random)
        {
            var admin = provider._admin.Value.GetDatabase("admin", new MongoDatabaseSettings { ReadConcern = ReadConcern.Local });     // $currentOp reads only with local
            BsonDocument[] pipeline =
            [
                new("$currentOp", new BsonDocument { { "allUsers", true }, { "idleSessions", true } }),
                new("$match", new BsonDocument { { "appName", "kernel-worker" }, { "$or", new BsonArray { new BsonDocument("active", true), new BsonDocument("transaction", new BsonDocument("$exists", true)) } } }),
                new("$project", new BsonDocument { { "_id", 0 }, { "lsid", 1 } }),     // the rest may hold duplicate field names
            ];
            // Half the time a connection is cut on the wire instead.
            if (random.Next(2) == 0 && provider.Proxy.CutBusy(random) > 0)
            {
                Interlocked.Increment(ref provider._cuts);
                return 1;
            }
            var ops = await (await admin.AggregateAsync<BsonDocument>(pipeline)).ToListAsync();
            var sessions = ops.Where(o => o.Contains("lsid")).Select(o => o["lsid"]["id"]).Distinct().ToList();
            if (sessions.Count == 0) return 0;
            var victim = sessions[random.Next(sessions.Count)];
            await admin.RunCommandAsync<BsonDocument>(new BsonDocument("killSessions", new BsonArray { new BsonDocument("id", victim) }));
            Interlocked.Increment(ref provider._ended);
            return 1;
        }

        public override string Chaos => $"{provider._cuts} connections cut on the wire, {provider._ended} sessions ended by killSessions";

        public override async ValueTask DisposeAsync()
        {
            await provider._admin.Value.DropDatabaseAsync(database);
            own?.Dispose();
        }
    }

    private sealed class Hold(IClientSessionHandle session) : IAsyncDisposable
    {
        public async ValueTask DisposeAsync()
        {
            await session.AbortTransactionAsync();
            session.Dispose();
        }
    }
}

/// <summary>
/// Counts the driver's command events. A round trip is one command the driver sends and waits for: every
/// CommandStartedEvent, whatever its name. Connection handshakes and the server monitor's heartbeats are not command
/// events and are not counted; neither are commands the driver did not send because a connection was already open.
/// </summary>
public sealed class CommandCounter : ICostCounter
{
    private readonly ConcurrentDictionary<string, int> _names = new();

    public void Started(CommandStartedEvent e) => _names.AddOrUpdate(e.CommandName, 1, (_, n) => n + 1);

    public void Reset() => _names.Clear();

    public CostReading Read()
    {
        var names = _names.ToArray();
        return new CostReading(names.Sum(n => n.Value), names.Where(n => n.Key == "commitTransaction").Sum(n => n.Value),
            string.Join(", ", names.OrderBy(n => n.Key, StringComparer.Ordinal).Select(n => $"{n.Key} {n.Value}")));
    }
}
