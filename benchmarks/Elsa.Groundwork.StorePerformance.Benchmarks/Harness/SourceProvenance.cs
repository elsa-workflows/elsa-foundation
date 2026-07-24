using System.Diagnostics;
using System.Reflection;
using System.Security.Cryptography;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Harness;

public static class SourceProvenance
{
    public static string FindRepositoryRoot()
    {
        for (var directory = new DirectoryInfo(Directory.GetCurrentDirectory()); directory is not null; directory = directory.Parent)
            if (File.Exists(Path.Combine(directory.FullName, "AGENTS.md")))
                return directory.FullName;
        throw new PerformanceContractException("Repository root was not found.");
    }

    public static void RequireCleanHead(string repositoryRoot, string requestedCommitSha)
    {
        var actualCommitSha = Git(repositoryRoot, "rev-parse", "HEAD").Trim();
        Validate(requestedCommitSha, actualCommitSha, string.IsNullOrWhiteSpace(Git(repositoryRoot, "status", "--porcelain=v1", "--untracked-files=all")));
    }

    public static void Validate(string requestedCommitSha, string actualCommitSha, bool isClean)
    {
        if (!string.Equals(actualCommitSha, requestedCommitSha, StringComparison.Ordinal))
            throw new PerformanceContractException($"matrix --commit must equal the current repository HEAD ({actualCommitSha}).");
        if (!isClean)
            throw new PerformanceContractException("matrix requires a clean repository so retained evidence identifies an exact source snapshot.");
    }

    public static string HarnessAssemblySha256()
    {
        var path = typeof(SourceProvenance).Assembly.Location;
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            throw new PerformanceContractException("The executing harness assembly cannot be fingerprinted.");
        return Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path))).ToLowerInvariant();
    }

    public static void RequireHarnessAssembly(string expectedSha256)
    {
        if (!string.Equals(HarnessAssemblySha256(), expectedSha256, StringComparison.Ordinal))
            throw new PerformanceContractException("The executing adapter host does not use the matrix harness assembly.");
    }

    private static string Git(string repositoryRoot, params string[] arguments)
    {
        var start = new ProcessStartInfo("git")
        {
            WorkingDirectory = repositoryRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new PerformanceContractException("Could not start git to verify benchmark source provenance.");
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0) throw new PerformanceContractException($"Could not verify benchmark source provenance: {error.Trim()}");
        return output;
    }
}
