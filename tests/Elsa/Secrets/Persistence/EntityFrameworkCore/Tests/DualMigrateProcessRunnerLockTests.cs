using Elsa.Secrets.Persistence.EntityFrameworkCore.Tests.Support;
using Xunit;

namespace Elsa.Secrets.Persistence.EntityFrameworkCore.Tests;

/// <summary>
/// Proves that <see cref="DualMigrateProcessRunner.Run"/> serializes concurrent invocations
/// against the same Tooling output (#1692), the way parallel test classes in this assembly and
/// the PostgreSQL assembly's <c>PostgreSqlEfSecretRepositoryTests</c> do in CI.
/// </summary>
public sealed class DualMigrateProcessRunnerLockTests
{
    [SkippableFact]
    public async Task Concurrent_runs_against_the_same_tooling_output_never_overlap()
    {
        Skip.If(OperatingSystem.IsWindows(), "The stand-in shim requires Unix executable permissions.");

        var isolatedRoot = Path.Join(Path.GetTempPath(), $"elsa-dual-migrate-lock-{Guid.NewGuid():N}");
        var scriptDirectory = Path.Join(isolatedRoot, "tools", "ef");
        Directory.CreateDirectory(scriptDirectory);

        // A cheap stand-in for `dotnet ef`: each run marks itself present, holds that mark for a
        // window, then records whether the *other* run's mark was still there when it checked.
        // Real serialization means one run's marker is always gone before the other's script
        // body starts, so no invocation ever observes the other's marker.
        WriteExecutableShim(scriptDirectory, "dual-migrate.sh", """
            #!/usr/bin/env bash
            set -euo pipefail
            : > "$OWN_MARKER"
            sleep 0.3
            if [[ -e "$OTHER_MARKER" ]]; then
              printf 'overlap\n' >> "$OVERLAP_FILE"
            fi
            rm -f "$OWN_MARKER"
            """);

        var markerA = Path.Join(isolatedRoot, "running-a");
        var markerB = Path.Join(isolatedRoot, "running-b");
        var overlapFile = Path.Join(isolatedRoot, "overlap.log");

        try
        {
            var first = Task.Run(() => DualMigrateProcessRunner.Run(
                [],
                extraEnvironment: new Dictionary<string, string?>
                {
                    ["OWN_MARKER"] = markerA,
                    ["OTHER_MARKER"] = markerB,
                    ["OVERLAP_FILE"] = overlapFile
                },
                rootOverride: isolatedRoot));
            var second = Task.Run(() => DualMigrateProcessRunner.Run(
                [],
                extraEnvironment: new Dictionary<string, string?>
                {
                    ["OWN_MARKER"] = markerB,
                    ["OTHER_MARKER"] = markerA,
                    ["OVERLAP_FILE"] = overlapFile
                },
                rootOverride: isolatedRoot));

            var results = await Task.WhenAll(first, second);

            Assert.All(results, result => Assert.True(result.ExitCode == 0, result.Describe()));
            Assert.False(
                File.Exists(overlapFile),
                "A run observed the other run's marker still present, so the two dual-migrate.sh " +
                "invocations overlapped instead of being serialized by the Tooling lock.");
        }
        finally
        {
            Directory.Delete(isolatedRoot, recursive: true);
        }
    }

    private static string WriteExecutableShim(string directory, string name, string contents)
    {
        if (OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Executable shims require Unix permissions.");

        var path = Path.Join(directory, name);
        File.WriteAllText(path, contents);
        File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
}
