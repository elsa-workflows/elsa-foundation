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
using Contract = Kernel.Foundation.Contracts;

namespace Kernel.Foundation.Sqlite;

/// <summary>Starts one Embedded node against an installed SQLite kernel qualification store.</summary>
public static partial class SqliteEmbeddedHosting
{
    // A fault or kill may leave work running. Its runtime lease remains held through process exit.
    private static readonly object FileGate = new();
    private static readonly Dictionary<string, ActiveFile> ActiveFiles = new(StringComparer.Ordinal);

    /// <summary>Starts a node over a local database file. The process policy must terminate the process if the watchdog
    /// cannot clear a stuck owner call. A clean stop releases the file for another host.</summary>
    public static Task<RuntimeNode> StartAsync(string databasePath, string node, IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, bool fenceInBatch = true,
        CancellationToken cancellationToken = default, Action<Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        return StartCoreAsync(databasePath, node, _ => actor, shipped: false, endProcess, options, fenceInBatch, cancellationToken, onFault);
    }

    /// <summary>Starts an Embedded node on the shipped format for root turns and timed jobs.</summary>
    internal static Task<RuntimeNode> StartFoundationAsync(string databasePath, string node, Contract.IActorType actor,
        Action<string> endProcess, LoopOptions? options = null, bool fenceInBatch = true,
        CancellationToken cancellationToken = default, Action<Exception>? onFault = null)
    {
        ArgumentNullException.ThrowIfNull(actor);
        RequireFoundationActorType(actor.Type);
        return StartCoreAsync(databasePath, node, store => new ActorAdapter(new EfStore.RootHeaders(store.BindSqliteWorkerNode()), actor,
                rootJobHandle: store.RootJobHandle, rootTimedJobClock: store.ReadRootTimedJobPublicationTimeAsync),
            shipped: true, endProcess, options, fenceInBatch, cancellationToken, onFault);
    }

    private static async Task<RuntimeNode> StartCoreAsync(string databasePath, string node, Func<EfStore, IActorType> createActor,
        bool shipped, Action<string> endProcess, LoopOptions? options, bool fenceInBatch, CancellationToken cancellationToken,
        Action<Exception>? onFault)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(node);
        ArgumentNullException.ThrowIfNull(endProcess);
        options ??= new LoopOptions();
        var millisecond = TimeSpan.FromMilliseconds(1);
        if (options.BalanceEvery < millisecond || options.BalanceEvery > TimeSpan.FromMilliseconds(int.MaxValue) ||
            options.PollInterval < millisecond || options.Window < options.BalanceEvery * 2 ||
            RuntimeNode.Cadence(Defaults.Lease, options) < millisecond)
            throw new ArgumentException("Configure positive millisecond loop intervals and a membership window of at least two balance rounds.", nameof(options));

        cancellationToken.ThrowIfCancellationRequested();
        var file = AcquireFile(databasePath, forRuntime: true);
        try
        {
            KernelDb Open() => shipped ? KernelDb.ForSqliteRootRuntime(file.Options, file.AdmissionDefinitions) : new(file.Options);

            int partitions;
            await using (var db = Open())
            {
                if (shipped)
                {
                    var admission = new SqliteRootAdmission(Open, TimeSpan.Zero, file.AdmissionDefinitions);
                    partitions = await admission.VerifyAsync(cancellationToken);
                }
                else
                {
                    await EfStore.RequireStoreVersionsAsync(db, cancellationToken);
                    partitions = await db.Partitions.CountAsync(cancellationToken);
                    if (partitions == 0)
                        throw new InvalidOperationException("The SQLite kernel database has no installed partitions.");
                }

                await db.Database.OpenConnectionAsync(cancellationToken);
                await using var wal = db.Database.GetDbConnection().CreateCommand();
                wal.CommandText = "PRAGMA journal_mode = WAL";
                if (await wal.ExecuteScalarAsync(cancellationToken) is not string mode ||
                    !mode.Equals("wal", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("The SQLite kernel database could not enable WAL.");
            }

            var store = new EfStore(Open, postgres: false, fenceInBatch: fenceInBatch) { CommitSql = CommitSql.Owned };
            if (shipped) await store.EnableRootJobsAsync(cancellationToken);
            var watchdog = new Watchdog(Defaults.Lease, endProcess);
            var watched = watchdog.Watch(store);
            RuntimeNode? reporter = null;
            var workers = new WorkerOptions
            {
                Partitions = partitions,
                OnDrainStoreError = error => Volatile.Read(ref reporter)?.RecordLoopError(error)
            };
            var host = DurableHost.ForNode(node, watched, createActor(store), partitions, workers);
            var runtime = new RuntimeNode(node, watched, partitions, options, beside: watchdog.RunAsync,
                beforeRelease: host.DrainEnteredTurnsAsync, onFault: onFault)
            {
                AfterCleanStop = file.Dispose
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
            file.Dispose();
            throw;
        }
    }

    private static FileLease AcquireFile(string databasePath, bool forRuntime = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var path = Path.GetFullPath(databasePath);
        if (!File.Exists(path))
            throw new FileNotFoundException("An installed SQLite kernel database is required.", path);
        path = OperatingSystem.IsWindows() ? ResolveWindowsPath(path) : ResolveUnixPath(path);

        lock (FileGate)
        {
            if (!ActiveFiles.TryGetValue(path, out var file))
            {
                var identity = ReadLinuxIdentity(path);
                if (identity is not null)
                    file = ActiveFiles.Values.FirstOrDefault(open => open.LinuxIdentity == identity);
                if (file is null)
                {
                    var connectionString = new SqliteConnectionStringBuilder
                    {
                        DataSource = path,
                        Mode = SqliteOpenMode.ReadWrite,
                        DefaultTimeout = 2,
                        ForeignKeys = true
                    }.ToString();
                    var options = new DbContextOptionsBuilder<KernelDb>().UseSqlite(connectionString).Options;
                    file = new(path, connectionString, options, AcquireLock(path + ".host-lock"), identity);
                    ActiveFiles.Add(path, file);
                }
            }
            if (forRuntime && file.Runtime)
                throw new SqliteHostLockException(file.Lock.Name);

            if (forRuntime) file.Runtime = true;
            file.Users++;
            return new(file, forRuntime);
        }
    }

    private sealed class ActiveFile(string path, string connectionString, DbContextOptions<KernelDb> options, FileStream sidecar,
        LinuxFileIdentity? linuxIdentity)
    {
        internal string Path => path;
        internal string ConnectionString => connectionString;
        internal DbContextOptions<KernelDb> Options => options;
        internal SqliteAdmissionDefinitions AdmissionDefinitions { get; } = new();
        internal FileStream Lock => sidecar;
        internal LinuxFileIdentity? LinuxIdentity => linuxIdentity;
        internal int Users { get; set; }
        internal bool Runtime { get; set; }
    }

    private sealed class FileLease(ActiveFile file, bool forRuntime) : IDisposable
    {
        private ActiveFile? _file = file;
        internal string Path => Current.Path;
        internal DbContextOptions<KernelDb> Options => Current.Options;
        internal SqliteAdmissionDefinitions AdmissionDefinitions => Current.AdmissionDefinitions;
        private ActiveFile Current => _file ?? throw new ObjectDisposedException(nameof(FileLease));

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _file, null) is not { } held) return;
            lock (FileGate)
            {
                if (forRuntime) held.Runtime = false;
                if (--held.Users != 0) return;

                ActiveFiles.Remove(held.Path);
                try
                {
                    using var connection = new SqliteConnection(held.ConnectionString);
                    SqliteConnection.ClearPool(connection);
                }
                finally { held.Lock.Dispose(); }
            }
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

    private static string ResolveUnixPath(string path)
    {
        // Resolve symlinks. Darwin also supplies the filesystem's spelling for case aliases.
        var resolved = UnixRealPath(path, IntPtr.Zero);
        if (resolved == IntPtr.Zero)
            throw new IOException($"Cannot resolve the SQLite database path '{path}'.", new Win32Exception(Marshal.GetLastPInvokeError()));
        try { return Marshal.PtrToStringUTF8(resolved)!; }
        finally { FreeUnixPath(resolved); }
    }

    [DllImport("libc", EntryPoint = "realpath", SetLastError = true)]
    private static extern IntPtr UnixRealPath([MarshalAs(UnmanagedType.LPUTF8Str)] string path, IntPtr resolved);

    [DllImport("libc", EntryPoint = "free")]
    private static extern void FreeUnixPath(IntPtr path);

    private readonly record struct LinuxFileIdentity(uint DeviceMajor, uint DeviceMinor, ulong Inode);

    // The Linux statx ABI has the same layout across architectures. Only the identity fields are used here.
    [StructLayout(LayoutKind.Explicit, Size = 256)]
    private struct LinuxFileStat
    {
        [FieldOffset(0)] internal uint Mask;
        [FieldOffset(32)] internal ulong Inode;
        [FieldOffset(136)] internal uint DeviceMajor;
        [FieldOffset(140)] internal uint DeviceMinor;
    }

    private static LinuxFileIdentity? ReadLinuxIdentity(string path)
    {
        if (!OperatingSystem.IsLinux()) return null;
        // Linux canonicalizes links but can preserve the lookup spelling on casefold filesystems.
        // Join an already-open inode through its original SQLite filename and pool.
        const int currentDirectory = -100;
        const uint inode = 0x100;
        try
        {
            if (LinuxStatx(currentDirectory, path, 0, inode, out var status) == 0 && (status.Mask & inode) != 0)
                return new(status.DeviceMajor, status.DeviceMinor, status.Inode);
        }
        catch (EntryPointNotFoundException) { } // An older libc can still use its canonical path.
        return null;
    }

    [DllImport("libc", EntryPoint = "statx")]
    private static extern int LinuxStatx(int directory, [MarshalAs(UnmanagedType.LPUTF8Str)] string path,
        int flags, uint mask, out LinuxFileStat status);

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

            return stream;
        }
        catch
        {
            stream.Dispose();
            throw;
        }
    }

}

internal sealed class SqliteHostLockException(string path, Exception? cause = null)
    : IOException($"Cannot acquire the SQLite host lock '{path}'. Only one host may use the database file.", cause);
