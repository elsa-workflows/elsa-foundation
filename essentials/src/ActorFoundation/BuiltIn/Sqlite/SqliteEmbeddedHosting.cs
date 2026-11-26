using System.Collections.Concurrent;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace Kernel.Foundation.Sqlite;

/// <summary>Starts one Embedded node against an installed SQLite kernel qualification store.</summary>
public static class SqliteEmbeddedHosting
{
    // A fault or kill may leave work running after StopAsync returns. Hold the lock through process exit in that case.
    private static readonly ConcurrentDictionary<string, FileStream> HeldLocks = new(StringComparer.Ordinal);

    /// <summary>Starts a node over a local database file. The process policy must terminate the process if the watchdog
    /// cannot clear a stuck owner call. A clean stop releases the file for another host.</summary>
    public static async Task<RuntimeNode> StartAsync(string databasePath, string node, IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, bool fenceInBatch = false,
        CancellationToken cancellationToken = default, Action<Exception>? onFault = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(endProcess);
        options ??= new LoopOptions();
        var millisecond = TimeSpan.FromMilliseconds(1);
        if (options.BalanceEvery < millisecond || options.BalanceEvery > TimeSpan.FromMilliseconds(int.MaxValue) ||
            options.PollInterval < millisecond || options.Window < options.BalanceEvery * 2 ||
            RuntimeNode.Cadence(Defaults.Lease, options) < millisecond)
            throw new ArgumentException("Configure positive millisecond loop intervals and a membership window of at least two balance rounds.", nameof(options));

        var path = ResolvePath(new FileInfo(Path.GetFullPath(databasePath)));
        if (!File.Exists(path))
            throw new FileNotFoundException("An installed SQLite kernel database is required.", path);
        cancellationToken.ThrowIfCancellationRequested();
        var fileLock = AcquireLock(path + ".host-lock");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWrite,
                DefaultTimeout = 2,
                ForeignKeys = true
            }.ToString();
            var dbOptions = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connectionString).Options;
            KernelDb Open() => new(dbOptions);

            int partitions;
            await using (var db = Open())
            {
                await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
                partitions = await db.Partitions.CountAsync(cancellationToken);
                if (partitions == 0)
                    throw new InvalidOperationException("The SQLite kernel database has no installed partitions.");

                await db.Database.OpenConnectionAsync(cancellationToken);
                await using var wal = db.Database.GetDbConnection().CreateCommand();
                wal.CommandText = "PRAGMA journal_mode = WAL";
                if (await wal.ExecuteScalarAsync(cancellationToken) is not string mode ||
                    !mode.Equals("wal", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The SQLite kernel database could not enable WAL.");
            }

            var store = new EfStore(Open, postgres: false, fenceInBatch: fenceInBatch) { CommitSql = CommitSql.Owned };
            var watchdog = new Watchdog(Defaults.Lease, endProcess);
            var watched = watchdog.Watch(store);
            RuntimeNode? reporter = null;
            var workers = new WorkerOptions
            {
                Partitions = partitions,
                OnDrainStoreError = error => Volatile.Read(ref reporter)?.RecordLoopError(error)
            };
            var host = DurableHost.ForNode(node, watched, actor, partitions, workers);
            var runtime = new RuntimeNode(node, watched, partitions, options, beside: watchdog.RunAsync,
                beforeRelease: host.DrainEnteredTurnsAsync, onFault: onFault)
            {
                AfterCleanStop = () =>
                {
                    using var connection = new SqliteConnection(connectionString);
                    SqliteConnection.ClearPool(connection);
                    ReleaseLock(fileLock);
                }
            };
            Volatile.Write(ref reporter, runtime);
            try
            {
                await runtime.StartAsync(host, cancellationToken);
                return runtime;
            }
            catch
            {
                await runtime.KillAsync();
                throw;
            }
        }
        catch
        {
            ReleaseLock(fileLock);
            throw;
        }
    }

    private static string ResolvePath(FileSystemInfo entry)
    {
        var parent = entry is DirectoryInfo directory ? directory.Parent : ((FileInfo)entry).Directory;
        if (parent is null) return entry.FullName;

        var path = Path.Combine(ResolvePath(parent), entry.Name);
        FileSystemInfo canonical = entry is DirectoryInfo ? new DirectoryInfo(path) : new FileInfo(path);
        return canonical.ResolveLinkTarget(returnFinalTarget: true) is { } target ? ResolvePath(target) : path;
    }

    private static FileStream AcquireLock(string path)
    {
        // Keep the sidecar on disk after closing it. Unlinking a locked inode would let another host lock a new inode.
        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (IOException error)
        {
            throw new InvalidOperationException($"Cannot acquire the SQLite host lock '{path}'. Only one host may use the database file.", error);
        }
        if (HeldLocks.TryAdd(path, stream)) return stream;
        stream.Dispose();
        throw new InvalidOperationException($"The SQLite host lock '{path}' is already held in this process.");
    }

    private static void ReleaseLock(FileStream stream)
    {
        HeldLocks.TryRemove(stream.Name, out _);
        stream.Dispose();
    }
}
