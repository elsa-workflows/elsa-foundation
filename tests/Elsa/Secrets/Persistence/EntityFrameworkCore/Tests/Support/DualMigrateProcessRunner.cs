using System.Diagnostics;
using System.Text;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;

/// <summary>
/// Runs the repository's real Secrets EF operator script without putting connection strings on
/// the child process command line. The PostgreSQL integration project links this file so its
/// Testcontainers proof exercises the exact same process boundary as the SQLite tests.
/// </summary>
internal static class DualMigrateProcessRunner
{
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
