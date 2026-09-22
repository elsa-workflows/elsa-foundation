using Acme.Widgets;
using Elsa.Cli.Worker;
using System.IO.Compression;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The opt-in <c>--restore</c> pass (spec 171 FR-083 to FR-086, ADR 0076 D10's exception): a host whose
/// package install root has never been populated can be scripted with one command, and every direction that
/// could quietly produce a partial artifact instead refuses.
/// </summary>
/// <remarks>
/// Each test works against its own private copy of the <c>NuplaneHost</c> fixture's build output, because a
/// restore writes under the host it is given — a state file, a store lock file, and extracted packages —
/// and the shared fixture output must stay the never-reconciled host every other test reads it as.
/// </remarks>
public sealed class NuplaneRestoreCliTests : IDisposable
{
    private const string Module = "Acme.Widgets";
    private const string Version = "1.4.2";

    private readonly RestoreHost host = new();
    private readonly TempDirectory output = new("elsa-cli-restore-artifact-");

    public void Dispose()
    {
        host.Dispose();
        output.Dispose();
    }

    /// <summary>
    /// Without the flag nothing changes, which is the whole of ADR 0076 D10. What does change is the
    /// message: a host that has never reconciled used to be reported as a missing provider engine, naming a
    /// package the operator had pinned correctly and never naming the state file that is actually absent.
    /// </summary>
    [Fact]
    public void A_never_reconciled_host_with_no_flag_is_refused_as_one_rather_than_as_a_missing_engine()
    {
        var run = DotnetElsa.Run("persistence", "plan", "--host", host.Path, "--provider", "SqlServer", "--all");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("packages-never-reconciled", run.Error, StringComparison.Ordinal);
        Assert.Contains(host.StateFile, run.Error, StringComparison.Ordinal);
        Assert.Contains("--restore", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.NuplaneDirectory), "A run without --restore wrote under the host.");
    }

    /// <summary>
    /// A <c>--packages</c> root is an already-assembled set, so there is nothing for a restore to populate.
    /// Refused as a usage error rather than accepted with one of the two flags quietly ignored (D7's rule
    /// for <c>--connection</c>, applied here).
    /// </summary>
    [Fact]
    public void A_restore_combined_with_a_packages_root_is_a_usage_error()
    {
        host.Feed(Module, Version);

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore", "--packages", host.FeedDirectory);

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("restore-with-packages", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.NuplaneDirectory), "A refused run wrote under the host.");
    }

    /// <summary>
    /// The case the flag exists for: a fresh host with a configured directory feed and an empty install
    /// root produces the same artifact it would produce once populated. The second run is the proof — it
    /// carries no flag at all, so it reads the state file the restore wrote through exactly the path a
    /// started host's state file is read through, and the two artifacts are compared byte for byte.
    /// </summary>
    [Fact]
    public void A_restored_host_produces_the_artifact_its_own_populated_state_file_produces()
    {
        host.Feed(Module, Version);
        host.Configure(DirectoryFeed("packages"));

        var restored = Script(output.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, restored.ExitCode);
        Assert.Contains("restore: 1 package(s) installed under", restored.Error, StringComparison.Ordinal);
        Assert.True(File.Exists(host.StateFile), $"'{host.StateFile}' was not written.");
        Assert.True(
            File.Exists(Path.Join(host.InstallRoot, "local-packages", Module, Version, NuplaneInstallRoot.ReadyMarker)),
            "The package was not extracted under the host's own install root.");

        using var again = new TempDirectory("elsa-cli-restore-populated-");
        var populated = Script(again.Path);

        Assert.Equal(ToolExitCode.Success, populated.ExitCode);
        Assert.DoesNotContain("restore:", populated.Error, StringComparison.Ordinal);
        AssertSameBytes(output.Path, again.Path);
    }

    /// <summary>
    /// A second <c>--restore</c> against a host that already records a set does nothing and says so, rather
    /// than reconciling over a package set the operator already has.
    /// </summary>
    [Fact]
    public void A_restore_against_a_host_that_already_records_a_set_does_nothing_and_says_so()
    {
        host.Feed(Module, Version);
        host.Configure(DirectoryFeed("packages"));
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore").ExitCode);
        var written = File.GetLastWriteTimeUtc(host.StateFile);

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("already records 1 package(s); --restore did nothing", run.Error, StringComparison.Ordinal);
        Assert.Equal(written, File.GetLastWriteTimeUtc(host.StateFile));
    }

    /// <summary>
    /// Pinned only (decision 4): a request that names more than one version would make the artifact depend
    /// on what the feed resolved to at the moment the tool ran, which is exactly the determinism D6 exists
    /// to protect. Every offender is named, and the refusal happens before a byte is downloaded.
    /// </summary>
    [Fact]
    public void A_request_that_names_more_than_one_version_is_refused_before_anything_is_written()
    {
        host.Configure(
            """
              "remote": {
                "ServiceIndex": "https://example.invalid/v3/index.json",
                "IncludePatterns": [ "Acme.Widgets", "Acme.Gadgets [1.0.0,2.0.0)" ]
              }
            """);

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("restore-unpinned", run.Error, StringComparison.Ordinal);
        Assert.Contains("Acme.Widgets <no version> (remote)", run.Error, StringComparison.Ordinal);
        Assert.Contains("Acme.Gadgets [1.0.0,2.0.0) (remote)", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.NuplaneDirectory), "A refused restore wrote under the host.");
    }

    /// <summary>
    /// Nuplane has no credential resolver, so it drops such a feed before the first network call. Restoring
    /// from whatever feeds remain would assemble a partial set and script it as if it were the whole one,
    /// so the run is refused instead, naming every feed at fault.
    /// </summary>
    /// <remarks>
    /// The second half is D7's rule applied to the other kind of secret this tool can now see: the
    /// credential value itself is configured in the host's <c>appsettings.json</c>, which only the worker
    /// reads, so a sentinel embedded in it must appear on neither stream.
    /// </remarks>
    [Fact]
    public void A_feed_that_configures_credentials_is_refused_by_name_and_its_credential_never_appears()
    {
        const string sentinel = "SENTINEL-FEED-CREDENTIAL-9d31ab";
        host.Configure(
            $$"""
                "private-feed": {
                  "ServiceIndex": "https://example.invalid/v3/index.json",
                  "Credentials": "{{sentinel}}",
                  "IncludePatterns": [ "Acme.Widgets [1.4.2]" ]
                }
              """);

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-feed-credentials", run.Error, StringComparison.Ordinal);
        Assert.Contains("private-feed", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, run.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.NuplaneDirectory), "A refused restore wrote under the host.");
    }

    /// <summary>
    /// The store lock is the whole of the running-host defense (decision 6): every reconcile cycle, a
    /// host's and a restore's alike, holds an exclusive handle on a file beside the state file, so a
    /// restore that cannot take it does nothing rather than becoming a second writer. This holds that exact
    /// handle the way <c>StoreLock</c> does — <c>FileShare.None</c> on <c>&lt;state file&gt;.lock</c>.
    /// </summary>
    [Fact]
    public void A_store_lock_held_elsewhere_refuses_rather_than_becoming_a_second_writer()
    {
        host.Feed(Module, Version);
        host.Configure(DirectoryFeed("packages"));
        Directory.CreateDirectory(host.NuplaneDirectory);
        using var held = new FileStream(host.LockFile, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-store-locked", run.Error, StringComparison.Ordinal);
        Assert.Contains("this host is running", run.Error, StringComparison.Ordinal);
        Assert.False(File.Exists(host.StateFile), "A restore that could not take the store lock wrote a state file anyway.");
    }

    /// <summary>
    /// The worker reads the host's configuration the way the front end already reads <c>shells.json</c>:
    /// the base file, then the <c>--environment</c> overlay layered on top, defaulting to
    /// <c>Production</c>. The base file here names a directory that does not exist, so only a run that
    /// actually read the overlay can restore anything.
    /// </summary>
    [Fact]
    public void The_environment_overlay_is_layered_over_the_hosts_own_appsettings()
    {
        host.Feed(Module, Version);
        host.Configure(DirectoryFeed("no-such-directory"));
        host.ConfigureOverlay("Production", DirectoryFeed("packages"));

        var run = DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains(Module, run.Output, StringComparison.Ordinal);
    }

    /// <summary>The keyed feed shape Nuplane recommends, pointed at a path relative to the host itself.</summary>
    private static string DirectoryFeed(string directory) =>
        $$"""
            "local-packages": {
              "DirectoryPath": "{{directory}}",
              "IncludePatterns": [ "Acme.*" ]
            }
          """;

    private CliRun Script(string target, params string[] extra) =>
        DotnetElsa.Run(
        [
            "persistence", "script",
            "--host", host.Path,
            "--provider", "PostgreSql",
            "--modules", Module,
            "--output", target,
            .. extra
        ]);

    private static void AssertSameBytes(string expected, string actual)
    {
        var expectedFiles = Files(expected);
        var actualFiles = Files(actual);

        Assert.Equal(expectedFiles.Keys, actualFiles.Keys);
        foreach (var (name, bytes) in expectedFiles)
            Assert.True(bytes.AsSpan().SequenceEqual(actualFiles[name]), $"{name} differs between the restored and the already-populated run.");
    }

    private static SortedDictionary<string, byte[]> Files(string directory) =>
        new(Directory.EnumerateFiles(directory).ToDictionary(Path.GetFileName, File.ReadAllBytes), StringComparer.Ordinal);
}

/// <summary>
/// A private copy of the <c>NuplaneHost</c> fixture's build output, with whatever Nuplane configuration and
/// feed contents one test needs. A restore writes under the host it is given, so every test gets its own.
/// </summary>
internal sealed class RestoreHost : IDisposable
{
    private readonly TempDirectory root = new("elsa-cli-restore-host-");

    public RestoreHost() => Copy(DotnetElsa.Host("NuplaneHost"), Path);

    /// <summary>The <c>--host</c> directory: a runtimeconfig, a deps file, and the assemblies beside them.</summary>
    public string Path => root.Path;

    /// <summary>Everything a restore may write lives here, and a test asserting "nothing was written" asserts on it.</summary>
    public string NuplaneDirectory => System.IO.Path.Join(Path, ".nuplane");

    public string StateFile => NuplaneInstallRoot.DefaultStateFile(Path);

    /// <summary>The store lock, named the way <c>Nuplane.Store.State.StoreLock</c> names it: the state file plus <c>.lock</c>.</summary>
    public string LockFile => StateFile + ".lock";

    public string InstallRoot => System.IO.Path.Join(NuplaneDirectory, "packages");

    /// <summary>The drop folder a directory feed reads, relative to the host exactly as a real one is.</summary>
    public string FeedDirectory => System.IO.Path.Join(Path, "packages");

    public void Dispose() => root.Dispose();

    /// <summary>Writes the host's own <c>appsettings.json</c>, carrying one or more feed declarations.</summary>
    public void Configure(string feeds) => File.WriteAllText(System.IO.Path.Join(Path, "appsettings.json"), Settings(feeds));

    /// <summary>Writes the <c>appsettings.&lt;environment&gt;.json</c> overlay the host would layer on top.</summary>
    public void ConfigureOverlay(string environment, string feeds) =>
        File.WriteAllText(System.IO.Path.Join(Path, $"appsettings.{environment}.json"), Settings(feeds));

    /// <summary>
    /// Packs the fixture module into the host's own drop folder as a real <c>.nupkg</c> — a zip carrying a
    /// nuspec and one <c>lib/net10.0</c> assembly, which is all Nuplane extracts and reads. No fixture
    /// <c>.nupkg</c> is committed, because the assembly it wraps is a build output.
    /// </summary>
    public void Feed(string packageId, string version)
    {
        Directory.CreateDirectory(FeedDirectory);
        using var archive = ZipFile.Open(System.IO.Path.Join(FeedDirectory, $"{packageId}.{version}.nupkg"), ZipArchiveMode.Create);
        using (var nuspec = new StreamWriter(archive.CreateEntry($"{packageId}.nuspec").Open()))
        {
            nuspec.Write(
                $"""
                 <?xml version="1.0" encoding="utf-8"?>
                 <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                   <metadata>
                     <id>{packageId}</id>
                     <version>{version}</version>
                     <authors>Acme</authors>
                     <description>The fixture module, packed by the test that needs a feed to restore from.</description>
                     <dependencies />
                   </metadata>
                 </package>
                 """);
        }

        archive.CreateEntryFromFile(typeof(WidgetsDbContext).Assembly.Location, $"lib/net10.0/{packageId}.dll");
    }

    private static string Settings(string feeds) =>
        $$"""
          {
            "Nuplane": {
              "Setup": {
                "Feeds": {
          {{feeds}}
                }
              }
            }
          }
          """;

    private static void Copy(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
            Directory.CreateDirectory(System.IO.Path.Join(target, System.IO.Path.GetRelativePath(source, directory)));
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
            File.Copy(file, System.IO.Path.Join(target, System.IO.Path.GetRelativePath(source, file)), overwrite: true);
    }
}
