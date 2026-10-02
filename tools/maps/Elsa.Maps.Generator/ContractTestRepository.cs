using System.Diagnostics;

namespace Elsa.Maps.Generator;

/// <summary>The scratch repository the contract tests build their fixtures in.</summary>
internal static class ContractTestRepository
{
    /// <summary>Runs <paramref name="test"/> against a fresh scratch directory and removes it afterwards.</summary>
    public static void Use(string name, Action<string> test)
    {
        var root = Path.Join(Path.GetTempPath(), $"{name}-{Environment.ProcessId}-{Guid.NewGuid():N}");

        try
        {
            Directory.CreateDirectory(root);
            test(root);
        }
        finally
        {
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // Best-effort cleanup.
            }
        }
    }

    /// <summary>Writes a project file named after the last segment of <paramref name="relativeDirectory"/>.</summary>
    public static void WriteProject(string root, string relativeDirectory, string contents)
    {
        var directory = Path.Join(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var name = relativeDirectory[(relativeDirectory.LastIndexOf('/') + 1)..];
        File.WriteAllText(Path.Join(directory, $"{name}.csproj"), contents);
    }

    /// <summary>Makes the scratch directory a git repository, which <see cref="RepoContext.ListFiles"/> requires.</summary>
    public static void GitInit(string root)
    {
        RunGit(root, "init", "-q");
        RunGit(root, "config", "user.email", "test@example.com");
        RunGit(root, "config", "user.name", "Test");
    }

    private static void RunGit(string root, params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("git") { WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Failed to start git.");
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"git {string.Join(' ', arguments)} failed: {process.StandardError.ReadToEnd()}");
    }
}
