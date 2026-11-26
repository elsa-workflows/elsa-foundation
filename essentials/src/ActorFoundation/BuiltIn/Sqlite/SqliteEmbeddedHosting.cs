using System.Collections.Concurrent;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;
using Kernel.Durable;
using Kernel.EntityFramework;
using Kernel.Runtime;
using Kernel.Runtime.Hosting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Win32.SafeHandles;

namespace Kernel.Foundation.Sqlite;

/// <summary>Starts one Embedded node against an installed SQLite kernel qualification store.</summary>
public static class SqliteEmbeddedHosting
{
    // A fault or kill may leave work running after StopAsync returns. Hold the lock through process exit in that case.
    private static readonly ConcurrentDictionary<string, FileStream> HeldLocks = new(StringComparer.Ordinal);

    /// <summary>Starts a node over a local database file. The process policy must terminate the process if the watchdog
    /// cannot clear a stuck owner call. A clean stop releases the file for another host.</summary>
    public static async Task<RuntimeNode> StartAsync(string databasePath, string node, IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, bool fenceInBatch = true,
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

        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("An installed SQLite kernel database is required.", path);
        path = OperatingSystem.IsWindows() ? ResolveWindowsPath(path) : ResolvePath(new FileInfo(path));
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWrite,
            DefaultTimeout = 2,
            ForeignKeys = true
        }.ToString();

        cancellationToken.ThrowIfCancellationRequested();
        var fileLock = AcquireLock(path + ".host-lock");
        try
        {
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
                AfterCleanStop = Release
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
            Release();
            throw;
        }

        void Release()
        {
            try
            {
                using var connection = new SqliteConnection(connectionString);
                SqliteConnection.ClearPool(connection);
            }
            finally { ReleaseLock(fileLock); }
        }
    }

    private static string ResolveWindowsPath(string file)
    {
        // A Windows short name and its long name can point to the same file. Use the opened file's normalized path.
        using var handle = File.OpenHandle(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var path = new StringBuilder(512);
        while (true)
        {
            var length = GetFinalPathNameByHandle(handle, path, (uint)path.Capacity, 0);
            if (length == 0)
                throw new IOException($"Cannot resolve the SQLite database path '{file}'.", new Win32Exception(Marshal.GetLastPInvokeError()));
            if (length < path.Capacity) return path.ToString();
            path.Capacity = checked((int)length);
        }
    }

    [DllImport("kernel32.dll", EntryPoint = "GetFinalPathNameByHandleW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFinalPathNameByHandle(SafeFileHandle handle, [Out] StringBuilder path, uint capacity, uint flags);

    private static string ResolvePath(FileSystemInfo entry)
    {
        var parent = entry is DirectoryInfo directory ? directory.Parent : ((FileInfo)entry).Directory;
        if (parent is null) return entry.FullName;

        var path = Path.Combine(ResolvePath(parent), entry.Name);
        FileSystemInfo canonical = entry is DirectoryInfo ? new DirectoryInfo(path) : new FileInfo(path);
        return canonical.ResolveLinkTarget(returnFinalTarget: true) is { } target ? ResolvePath(target) : path;
    }

    // Use an explicit Unix flock: the CLR can be configured to skip the implicit lock from FileShare.None.
    [DllImport("libc", EntryPoint = "flock", SetLastError = true)]
    private static extern int UnixLockFile(int descriptor, int operation);

    private static FileStream AcquireLock(string path)
    {
        // Keep the sidecar on disk after closing it. Unlinking a locked inode would let another host lock a new inode.
        FileStream stream;
        try { stream = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException error) { throw new SqliteHostLockException(path, error); }

        try
        {
            const int exclusiveNonBlocking = 2 | 4;
            if (!OperatingSystem.IsWindows() && UnixLockFile(stream.SafeFileHandle.DangerousGetHandle().ToInt32(), exclusiveNonBlocking) != 0)
                throw new SqliteHostLockException(path, new Win32Exception(Marshal.GetLastPInvokeError()));

            if (!HeldLocks.TryAdd(path, stream))
                throw new SqliteHostLockException(path);
            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

    private static void ReleaseLock(FileStream stream)
    {
        HeldLocks.TryRemove(stream.Name, out _);
        stream.Dispose();
    }
}

internal sealed class SqliteHostLockException(string path, Exception? cause = null)
    : IOException($"Cannot acquire the SQLite host lock '{path}'. Only one host may use the database file.", cause);
