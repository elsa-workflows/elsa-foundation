using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// Runs the repository's real Secrets EF operator script without putting connection strings on
/// the child process command line. The PostgreSQL integration project links this file so its
/// Testcontainers proof exercises the exact same process boundary as the SQLite tests.
/// </summary>
internal static class DualMigrateProcessRunner
{
    // Every `dotnet ef` invocation copies its BuildHost into the Tooling project's output
    // folder. Concurrent dual-migrate.sh calls race on that copy: xUnit runs the Secrets EF
    // test classes in the same assembly in parallel, and a solution-filtered `dotnet test`
    // runs the PostgreSQL assembly (which links this file, see the class doc comment) alongside
    // this one. Serialize with a cross-process, cross-assembly lock on a file in the system temp
    // directory, named from a deterministic hash of the Tooling project's absolute path. That
    // keeps the lock out of `obj/` - disposable build output that a `dotnet clean` or cache reset
    // can delete out from under a holder, letting the next acquirer create a fresh, uncontended
    // file and silently defeat serialization - while still keying it to this checkout's Tooling
    // output, so two different worktrees never contend on each other's lock.
    private static readonly TimeSpan LockAcquisitionTimeout = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan LockRetryDelay = TimeSpan.FromMilliseconds(100);

    private static bool? dotnetEf;

    public static bool HasDotnetEf()
    {
        if (dotnetEf is { } cached)
            return cached;

        var root = RepositoryRoot();
        if (File.Exists(Path.Join(root, ".tools", "dotnet-ef")))
            return Remember(true);

        TryRestoreDotnetTools(root);
        var start = new ProcessStartInfo("dotnet", ["ef", "--version"])
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        CopyDotnetEnvironment(start.Environment);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return Remember(false);
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                KillProcessTree(process);
                return Remember(false);
            }

            Task.WaitAll(outputTask, errorTask);
            return Remember(process.ExitCode == 0);
        }
        catch (InvalidOperationException)
        {
            return Remember(false);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return Remember(false);
        }
    }

    public static ScriptResult Run(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null,
        string? rootOverride = null)
    {
        var root = rootOverride ?? RepositoryRoot();
        var script = Path.Join(root, "tools", "ef", "dual-migrate.sh");

        // Hold the lock for the whole process lifetime so no other caller can start a
        // conflicting `dotnet ef` invocation while this one runs.
        using var toolingLock = AcquireToolingLock(root);

        var start = new ProcessStartInfo("bash")
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        start.ArgumentList.Add(script);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        CopyDotnetEnvironment(start.Environment);
        if (extraEnvironment is not null)
        {
            foreach (var (key, value) in extraEnvironment)
            {
                if (value is null)
                    start.Environment.Remove(key);
                else
                    start.Environment[key] = value;
            }
        }

        using var process = Process.Start(start)
                            ?? throw new InvalidOperationException("bash could not be started.");
        var outputTask = process.StandardOutput.ReadToEndAsync();
        var errorTask = process.StandardError.ReadToEndAsync();
        // The 180s budget covers running the script, not waiting for the lock: start it now,
        // after the lock is already held.
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(180));
        try
        {
            process.WaitForExitAsync(timeout.Token).GetAwaiter().GetResult();
        }
        catch (OperationCanceledException)
        {
            KillProcessTree(process);
            throw new TimeoutException($"dual-migrate.sh {string.Join(' ', args)} did not exit within 180s.");
        }

        return new ScriptResult(
            process.ExitCode,
            outputTask.GetAwaiter().GetResult(),
            errorTask.GetAwaiter().GetResult());
    }

    public static ScriptResult RunFromExistingBuild(
        IReadOnlyList<string> args,
        IReadOnlyDictionary<string, string?>? extraEnvironment = null)
    {
        var environment = extraEnvironment is null
            ? new Dictionary<string, string?>()
            : new Dictionary<string, string?>(extraEnvironment);
        var configuration = typeof(DualMigrateProcessRunner).Assembly
                                .GetCustomAttribute<AssemblyConfigurationAttribute>()?.Configuration
                            ?? throw new InvalidOperationException("The test assembly does not declare its build configuration.");
        environment["ELSA_SECRETS_EF_CONFIGURATION"] = configuration;
        environment["ELSA_SECRETS_EF_SKIP_BUILD"] = "1";
        return Run(args, environment);
    }

    private static bool Remember(bool value)
    {
        dotnetEf = value;
        return value;
    }

    private static void TryRestoreDotnetTools(string root)
    {
        var start = new ProcessStartInfo("dotnet", ["tool", "restore"])
        {
            WorkingDirectory = root,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        CopyDotnetEnvironment(start.Environment);
        try
        {
            using var process = Process.Start(start);
            if (process is null)
                return;
            var outputTask = process.StandardOutput.ReadToEndAsync();
            var errorTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(60_000))
            {
                KillProcessTree(process);
                return;
            }

            Task.WaitAll(outputTask, errorTask);
        }
        catch (InvalidOperationException)
        {
            // The caller will skip when the repository-local tool is unavailable.
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // The caller will skip when the repository-local tool is unavailable.
        }
    }

    private static FileStream AcquireToolingLock(string root) => AcquireLock(ComputeToolingLockPath(root));

    // The lock file name is a truncated SHA-256 of the Tooling project's absolute path (not
    // string.GetHashCode, which is randomized per process and would give every process its own
    // lock). That makes the lock deterministic per checkout: every process targeting the same
    // Tooling output - this assembly's parallel test classes and the linked PostgreSQL assembly -
    // shares one lock file, while a different worktree's checkout hashes to a different file and
    // never contends with this one.
    internal static string ComputeToolingLockPath(string root)
    {
        var toolingPath = Path.Join(
            root, "src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "Tooling");
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(toolingPath)))[..16];
        return Path.Join(Path.GetTempPath(), $"elsa-dual-migrate-{hash}.lock");
    }

    // Exposed so DualMigrateProcessRunnerLockTests can prove that a non-contention failure (an
    // unusable lock location) surfaces immediately instead of being retried for the full
    // lock-acquisition timeout, by passing a lock path this helper cannot create a directory for.
    internal static FileStream AcquireLock(string lockPath)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (IsLockContention(ex) && stopwatch.Elapsed < LockAcquisitionTimeout)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (IOException ex) when (IsLockContention(ex))
            {
                throw new TimeoutException(
                    $"Could not acquire the dual-migrate Tooling lock at '{lockPath}' within " +
                    $"{LockAcquisitionTimeout}. Another dual-migrate.sh invocation is holding it " +
                    "for longer than expected.",
                    ex);
            }
        }
    }

    // A contended `FileShare.None` open must be the only IOException this loop retries; anything
    // else (a missing/unusable lock directory, a full disk, a permissions problem) is a real
    // failure and must surface immediately with its original cause, not be misreported as
    // contention after a five-minute wait. .NET only ever raises the plain IOException type for
    // this - DirectoryNotFoundException, PathTooLongException, etc. are subclasses and never
    // sharing violations - and it carries the platform's sharing/lock-violation code: Windows
    // reports ERROR_SHARING_VIOLATION/ERROR_LOCK_VIOLATION; Unix surfaces the raw errno, but the
    // two candidate values swap meaning across kernels, so they must not both be accepted on the
    // same OS: on Linux, EAGAIN/EWOULDBLOCK is 11 and EDEADLK is 35; on macOS/FreeBSD it is the
    // reverse (EAGAIN/EWOULDBLOCK is 35, EDEADLK is 11). Accepting both on one OS would let a
    // deadlock error be silently retried as contention.
    private static bool IsLockContention(IOException ex)
    {
        if (ex.GetType() != typeof(IOException))
            return false;

        if (OperatingSystem.IsWindows())
            return ex.HResult is unchecked((int)0x80070020) or unchecked((int)0x80070021);

        return OperatingSystem.IsLinux() ? ex.HResult is 11 : ex.HResult is 35;
    }

    private static void KillProcessTree(Process process)
    {
        try
        {
            process.Kill(entireProcessTree: true);
        }
        catch (InvalidOperationException)
        {
            // The process may already have exited; cleanup is best-effort.
        }
    }

    private static void CopyDotnetEnvironment(IDictionary<string, string?> environment)
    {
        environment["PATH"] = Environment.GetEnvironmentVariable("PATH") ?? "";
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrWhiteSpace(dotnetRoot))
            environment["DOTNET_ROOT"] = dotnetRoot;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                return directory.FullName;
            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not find repository root.");
    }

    internal sealed record ScriptResult(int ExitCode, string Output, string Error)
    {
        public string Describe()
        {
            var text = new StringBuilder();
            text.Append("exit ").Append(ExitCode);
            if (!string.IsNullOrWhiteSpace(Output))
                text.AppendLine().Append(Output);
            if (!string.IsNullOrWhiteSpace(Error))
                text.AppendLine().Append(Error);
            return text.ToString();
        }
    }
}
