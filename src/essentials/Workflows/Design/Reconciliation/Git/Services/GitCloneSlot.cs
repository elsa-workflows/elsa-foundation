using System.Security.Cryptography;
using System.Text;
using Elsa.Workflows.Design.Reconciliation.Git.Options;
using Microsoft.Extensions.Options;

namespace Elsa.Workflows.Design.Reconciliation.Git.Services;

/// <summary>
/// Where a shell's workflows clone lives (#2197). An explicit <see cref="GitReconciliationOptions.LocalCachePath"/> is
/// used as given. Otherwise the clone lives in a clone slot, <c>{temp}/elsa-gitops/{source hash}/slot-{n}/clone</c>, the
/// hash covering the remote, the branch and the role: the lowest slot whose lock file this instance can open exclusively,
/// held from the first use of <see cref="RepositoryPath"/> until dispose. The feature registers one per shell, so two
/// processes, or two shells of one process, never share a clone and never collide on its <c>index.lock</c>. The operating
/// system frees the slot of a process however the process ends, so the next process to start takes the slot with its
/// clone: a Writer clone keeps its unpushed export commits across a restart, and there are never more slots than
/// processes that ran at once.
/// </summary>
/// <remarks>
/// On Unix each directory from <c>elsa-gitops</c> down to the slot is created owner-only (0700) and set so again before
/// use. Only a directory's owner may change its mode, so one that belongs to another user, or is a symbolic link, is
/// refused rather than used: whoever owns it could read or replace the clone, and with its configuration and hooks choose
/// what git runs. A process running as root may change any directory's mode, so for it the check proves less. Exclusive
/// opens rely on the file locks the runtime takes, which <c>DOTNET_SYSTEM_IO_DISABLEFILELOCKING</c> turns off; a host
/// that sets it gives each process its own <see cref="GitReconciliationOptions.LocalCachePath"/>.
/// </remarks>
public sealed class GitCloneSlot : IDisposable
{
    /// <summary>How many slots one source may have on a machine before the search gives up.</summary>
    public const int MaxSlots = 64;

    private const UnixFileMode OwnerOnly = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;

    private readonly GitReconciliationOptions _options;
    private readonly string _slotsRoot;
    private readonly Lock _gate = new();
    private FileStream? _slotLock;
    private string? _repositoryPath;
    private bool _disposed;

    /// <summary>The clone location of <paramref name="options"/>, with slots under <c>{temp}/elsa-gitops</c>.</summary>
    public GitCloneSlot(IOptions<GitReconciliationOptions> options) : this(options.Value, Path.Join(Path.GetTempPath(), "elsa-gitops"))
    {
    }

    /// <summary>The clone location of <paramref name="options"/>, with slots under <paramref name="slotsRoot"/>.</summary>
    public GitCloneSlot(GitReconciliationOptions options, string slotsRoot)
    {
        _options = options;
        _slotsRoot = slotsRoot;
    }

    /// <summary>The absolute path of the clone. The first use takes the slot.</summary>
    public string RepositoryPath
    {
        get
        {
            lock (_gate)
            {
                ObjectDisposedException.ThrowIf(_disposed, this);
                return _repositoryPath ??= Resolve();
            }
        }
    }

    /// <summary>Releases the slot, so another process or shell may take it with its clone.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _slotLock?.Dispose();
            _slotLock = null;
        }
    }

    private string Resolve()
    {
        if (!string.IsNullOrWhiteSpace(_options.LocalCachePath))
            return Path.GetFullPath(_options.LocalCachePath);

        var sourceDirectory = Path.Join(_slotsRoot, SourceKey($"{_options.ResolvedSourceId}|{_options.Role}"));
        CreatePrivateDirectory(_slotsRoot);
        CreatePrivateDirectory(sourceDirectory);

        IOException? lastTaken = null;
        for (var slot = 0; slot < MaxSlots; slot++)
        {
            var slotDirectory = Path.Join(sourceDirectory, $"slot-{slot}");
            CreatePrivateDirectory(slotDirectory);
            try
            {
                _slotLock = new FileStream(Path.Join(slotDirectory, ".lock"), LockFileOptions());
                return Path.Join(slotDirectory, "clone");
            }
            catch (IOException exception) when (exception.GetType() == typeof(IOException))
            {
                lastTaken = exception; // another process or shell holds this slot
            }
        }

        throw new InvalidOperationException(
            $"No free workflows clone slot for '{_options.ResolvedSourceId}' among the {MaxSlots} under '{sourceDirectory}'. " +
            $"Set {nameof(GitReconciliationOptions.LocalCachePath)} to give this process a clone of its own.", lastTaken);
    }

    /// <summary>
    /// A hash of the source identity and the role, so distinct remotes and branches never share a slot, and a Consumer,
    /// which resets its clone hard onto the remote, never takes over a Writer's clone and the commits it keeps.
    /// </summary>
    private static string SourceKey(string sourceAndRole) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sourceAndRole)))[..16].ToLowerInvariant();

    private static FileStreamOptions LockFileOptions()
    {
        var options = new FileStreamOptions { Mode = FileMode.OpenOrCreate, Access = FileAccess.ReadWrite, Share = FileShare.None };
        if (!OperatingSystem.IsWindows())
            options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        return options;
    }

    private static void CreatePrivateDirectory(string path)
    {
        if (OperatingSystem.IsWindows())
        {
            Directory.CreateDirectory(path); // under the user profile, whose ACL already keeps other users out
            return;
        }

        if (Directory.CreateDirectory(path, OwnerOnly).LinkTarget is not null)
            throw NotPrivate(path, "it is a symbolic link", null);

        try
        {
            File.SetUnixFileMode(path, OwnerOnly);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw NotPrivate(path, "it belongs to another user", exception);
        }
    }

    private static InvalidOperationException NotPrivate(string path, string reason, Exception? inner) => new(
        $"The workflows clone directory '{path}' was not used because {reason}, so the clone in it would not be this user's alone. " +
        $"Remove it, or set {nameof(GitReconciliationOptions.LocalCachePath)} to a directory of this user's.",
        inner);
}
