using System.Diagnostics;
using System.Reflection;
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
    // this one. Serialize with a cross-process, cross-assembly lock on a file scoped to this
    // checkout's Tooling output, so two different worktrees never contend on each other's lock.
    private static readonly string ToolingLockRelativePath = Path.Join(
        "src", "Elsa", "Secrets", "Persistence", "EntityFrameworkCore", "Tooling", "obj", "dual-migrate.lock");

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

    private static FileStream AcquireToolingLock(string root)
    {
        var lockPath = Path.Join(root, ToolingLockRelativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(lockPath)!);

        var stopwatch = Stopwatch.StartNew();
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (stopwatch.Elapsed < LockAcquisitionTimeout)
            {
                Thread.Sleep(LockRetryDelay);
            }
            catch (IOException ex)
            {
                throw new TimeoutException(
                    $"Could not acquire the dual-migrate Tooling lock at '{lockPath}' within " +
                    $"{LockAcquisitionTimeout}. Another dual-migrate.sh invocation is holding it " +
                    "for longer than expected.",
                    ex);
            }
        }
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
