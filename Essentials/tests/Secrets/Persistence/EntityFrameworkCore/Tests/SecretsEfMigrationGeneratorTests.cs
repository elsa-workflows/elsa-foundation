using System.Diagnostics;
using System.Text.RegularExpressions;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// The shell tooling that adds a named migration to this module's historical chain:
/// <c>tools/ef/generate-ef-migrations.sh</c> and the <c>tools/ef/secrets-ef-lib.sh</c> it sources.
/// </summary>
/// <remarks>
/// Both properties here were proved through <c>dual-migrate.sh</c>, which shared that library, until #1878
/// retired it. They belong to the library and outlived the script: the generator still has to compile
/// migrations into the module assembly a host loads, and still has to run the EF version this repository
/// pins rather than whichever one happens to be installed globally.
/// </remarks>
public sealed class SecretsEfMigrationGeneratorTests
{
    /// <summary>
    /// <c>--project</c> is the provider-free module assembly, so generated migrations compile into the
    /// assembly a host loads; <c>--startup-project</c> is the shared design-time project that holds the
    /// provider engines (#1878), which must actually exist for the generator to run at all.
    /// </summary>
    [Fact]
    public void Generator_compiles_into_the_module_and_starts_from_the_shared_design_time_project()
    {
        var library = File.ReadAllText(RepoPath("tools", "ef", "secrets-ef-lib.sh"));

        Assert.Equal(
            "src/essentials/Secrets/Persistence/EntityFrameworkCore/Elsa.Secrets.Persistence.EntityFrameworkCore.csproj",
            Assignment(library, "secrets_ef_module"));
        var startup = Assignment(library, "secrets_ef_startup");
        Assert.Equal("tools/ef/Elsa.EntityFrameworkCore.Tooling/Elsa.EntityFrameworkCore.Tooling.csproj", startup);
        Assert.True(File.Exists(RepoPath(startup.Split('/'))), $"'{startup}' does not exist.");
    }

    /// <summary>
    /// The pinned <c>dotnet-ef</c> wins: an executable repository-local <c>.tools/dotnet-ef</c> first, then
    /// the repository manifest's <c>dotnet ef</c>, and only then a global tool on PATH. A stale global tool
    /// silently generating migrations against another EF version is the failure this ordering prevents.
    /// </summary>
    [SkippableTheory]
    [InlineData(true, true, "local")]
    [InlineData(false, true, "manifest")]
    [InlineData(false, false, "global")]
    public void Ef_tool_resolution_follows_the_documented_precedence(
        bool hasLocalTool,
        bool hasManifest,
        string expectedTool)
    {
        Skip.If(OperatingSystem.IsWindows(), "The recording shims require Unix executable permissions.");

        var shimRoot = Path.Join(Path.GetTempPath(), $"elsa-secrets-ef-precedence-{Guid.NewGuid():N}");
        var isolatedRoot = Path.Join(shimRoot, "repo");
        var isolatedScriptDirectory = Path.Join(isolatedRoot, "tools", "ef");
        var recordingLog = Path.Join(shimRoot, "calls.log");
        Directory.CreateDirectory(isolatedScriptDirectory);

        try
        {
            // The library under test, in a checkout of its own: secrets_ef_init derives the repository root
            // from the library's own location, so this probe resolves tools against this temporary tree.
            File.Copy(
                RepoPath("tools", "ef", "secrets-ef-lib.sh"),
                Path.Join(isolatedScriptDirectory, "secrets-ef-lib.sh"));
            var probe = WriteExecutableShim(isolatedScriptDirectory, "resolve-probe.sh", """
                #!/usr/bin/env bash
                set -euo pipefail
                source "$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd -P)/secrets-ef-lib.sh"
                secrets_ef_init
                secrets_ef --version
                """);
            WriteExecutableShim(shimRoot, "dotnet", """
                #!/usr/bin/env bash
                set -euo pipefail
                [[ "${1:-}" == "ef" ]] || exit 97
                printf 'manifest\n' >> "$ELSA_SECRETS_EF_RECORDING_LOG"
                """);
            WriteExecutableShim(shimRoot, "dotnet-ef", """
                #!/usr/bin/env bash
                set -euo pipefail
                printf 'global\n' >> "$ELSA_SECRETS_EF_RECORDING_LOG"
                """);

            if (hasManifest)
            {
                var configDirectory = Path.Join(isolatedRoot, ".config");
                Directory.CreateDirectory(configDirectory);
                File.WriteAllText(Path.Join(configDirectory, "dotnet-tools.json"), "{}");
            }

            if (hasLocalTool)
            {
                var localToolDirectory = Path.Join(isolatedRoot, ".tools");
                Directory.CreateDirectory(localToolDirectory);
                WriteExecutableShim(localToolDirectory, "dotnet-ef", """
                    #!/usr/bin/env bash
                    set -euo pipefail
                    printf 'local\n' >> "$ELSA_SECRETS_EF_RECORDING_LOG"
                    """);
            }

            var exitCode = RunBash(probe, shimRoot, recordingLog);

            Assert.Equal(0, exitCode);
            Assert.Equal([expectedTool], File.ReadAllLines(recordingLog));
        }
        finally
        {
            Directory.Delete(shimRoot, recursive: true);
        }
    }

    /// <summary>The value of a <c>name="value"</c> assignment the library makes exactly once.</summary>
    private static string Assignment(string script, string name)
    {
        var matches = Regex.Matches(script, $"^\\s*{Regex.Escape(name)}=\"([^\"]+)\"", RegexOptions.Multiline);
        return Assert.Single(matches).Groups[1].Value;
    }

    private static int RunBash(string script, string shimRoot, string recordingLog)
    {
        var startInfo = new ProcessStartInfo("bash")
        {
            WorkingDirectory = shimRoot,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        startInfo.ArgumentList.Add(script);
        // Only PATH, so the shims decide which tool is found; the inherited PATH keeps bash's own
        // utilities reachable.
        startInfo.Environment["PATH"] = $"{shimRoot}{Path.PathSeparator}{Environment.GetEnvironmentVariable("PATH") ?? ""}";
        startInfo.Environment["ELSA_SECRETS_EF_RECORDING_LOG"] = recordingLog;

        using var process = Process.Start(startInfo)
                            ?? throw new InvalidOperationException("bash could not be started.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        Assert.True(process.WaitForExit(60_000), "The resolution probe did not exit within 60s.");
        Task.WaitAll(output, error);
        return process.ExitCode;
    }

    private static string WriteExecutableShim(string directory, string name, string contents)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Executable recording shims require Unix permissions.");

        var path = Path.Join(directory, name);
        File.WriteAllText(path, contents);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }

    private static string RepoPath(params string[] segments) => Path.Join([RepositoryRoot, .. segments]);

    private static string RepositoryRoot
    {
        get
        {
            for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
            {
                if (File.Exists(Path.Join(directory.FullName, "Elsa.Server.slnx")))
                    return directory.FullName;
            }

            throw new DirectoryNotFoundException("Could not find repository root.");
        }
    }
}
