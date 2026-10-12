using Acme.Widgets;
using Elsa.Cli.Worker;
using System.IO.Compression;
using System.Text.Json;
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
        ArtifactAssert.SameBytes(output.Path, again.Path);
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
    /// Nuplane's built-in <c>env</c> provider cannot resolve a reference to an environment variable this
    /// process never set, so it drops such a feed before the first network call. Restoring from whatever
    /// feeds remain would assemble a partial set and script it as if it were the whole one, so the run is
    /// refused instead, naming every feed at fault.
    /// </summary>
    /// <remarks>
    /// The second half is D7's rule applied to the other kind of secret this tool can now see: the
    /// referenced environment variable's <em>name</em> is configured in the host's <c>appsettings.json</c>,
    /// which only the worker reads, so a sentinel embedded in it must appear on neither stream. What it
    /// deliberately does not cover is a credential <em>value</em>, which never exists on this path at all —
    /// <see cref="A_resolvable_credential_value_never_appears_when_no_provider_claims_its_reference"/> is
    /// the test for that.
    /// </remarks>
    [Fact]
    public void A_feed_that_configures_credentials_is_refused_by_name_and_its_credential_never_appears()
    {
        const string sentinel = "SENTINEL-FEED-CREDENTIAL-9d31ab";
        host.Configure(
            $$"""
                "private-feed": {
                  "ServiceIndex": "https://example.invalid/v3/index.json",
                  "Credentials": "secrets://env/{{sentinel}}",
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
    /// The never-echo-a-value guarantee, with a value that really exists. The variable is set to a sentinel
    /// and the reference names a provider nothing claims, so the run is refused for the provider rather than
    /// for the value — and the value, which this process is holding the whole time and Nuplane's own
    /// built-in provider could have read, is on neither stream.
    /// </summary>
    /// <remarks>
    /// An unknown provider rather than an absent variable on purpose: with <c>secrets://env/NAME</c> and the
    /// variable set, the restore would succeed as far as contacting <c>example.invalid</c>, and the refusal
    /// under test would never be reached. This way the value is resolvable by every mechanism except the one
    /// the reference names, which is the only arrangement where "the value was available and still never
    /// printed" means anything.
    /// </remarks>
    [Fact]
    public void A_resolvable_credential_value_never_appears_when_no_provider_claims_its_reference()
    {
        const string variable = "ELSA_CLI_RESTORE_FEED_TOKEN";
        const string sentinel = "SENTINEL-FEED-CREDENTIAL-VALUE-51c07f";
        host.Configure(
            $$"""
                "private-feed": {
                  "ServiceIndex": "https://example.invalid/v3/index.json",
                  "Credentials": "secrets://nosuch/{{variable}}",
                  "IncludePatterns": [ "Acme.Widgets [1.4.2]" ]
                }
              """);

        var run = DotnetElsa.Run(
            new Dictionary<string, string> { [variable] = sentinel },
            "persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-feed-credentials", run.Error, StringComparison.Ordinal);
        Assert.Contains("private-feed", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain(sentinel, run.Text, StringComparison.Ordinal);
        Assert.False(Directory.Exists(host.NuplaneDirectory), "A refused restore wrote under the host.");
    }

    /// <summary>
    /// The proof that the pass no longer moves this process's current directory: a relative
    /// <c>DirectoryPath</c> is anchored to <c>--host</c> by <c>NuplaneRestoreOptions.BasePath</c> alone.
    /// </summary>
    /// <remarks>
    /// The discriminating part is the decoy. The tool runs from a directory that has a <c>packages</c> folder
    /// of its own, holding a file the feed would index as the same id and version but that is not a readable
    /// package — so a run that resolved <c>"DirectoryPath": "packages"</c> against its own current directory
    /// finds a candidate it cannot acquire and refuses (<c>restore-incomplete</c>), rather than finding
    /// nothing and refusing for some other reason. Only a run anchored to the host reaches the real package.
    /// </remarks>
    [Fact]
    public void A_relative_directory_feed_resolves_under_the_host_rather_than_under_the_current_directory()
    {
        host.Feed(Module, Version);
        host.Configure(DirectoryFeed("packages"));
        using var elsewhere = new TempDirectory("elsa-cli-restore-elsewhere-");
        Directory.CreateDirectory(Path.Join(elsewhere.Path, "packages"));
        File.WriteAllBytes(Path.Join(elsewhere.Path, "packages", $"{Module}.{Version}.nupkg"), [0x00, 0x01, 0x02, 0x03]);

        var run = DotnetElsa.RunIn(elsewhere.Path, "persistence", "list", "--host", host.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("restore: 1 package(s) installed under", run.Error, StringComparison.Ordinal);
        Assert.Contains(Module, run.Output, StringComparison.Ordinal);
        Assert.True(
            File.Exists(Path.Join(host.InstallRoot, "local-packages", Module, Version, NuplaneInstallRoot.ReadyMarker)),
            "The package was not extracted from the feed directory beside the host.");
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

    /// <summary>
    /// "Anything less than the whole set is a refusal" (FR-086): a package the feed pins but cannot actually
    /// hand over degrades the cycle, and a degraded cycle is refused rather than scripted from, the same as
    /// a skipped one. This is a directory feed's own failure mode, not a network one, so it needs no remote
    /// service to reach — the pinned <c>.nupkg</c> the feed indexes is unreadable, which is what a package a
    /// feed "pins but does not actually contain a usable copy of" looks like for a local, file-backed feed.
    /// </summary>
    [Fact]
    public void A_package_the_feed_cannot_actually_hand_over_refuses_rather_than_scripting_a_partial_set()
    {
        host.FeedCorrupt(Module, Version);
        host.Configure(DirectoryFeed("packages"));

        var run = Script(output.Path, "--restore");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-incomplete", run.Error, StringComparison.Ordinal);
        Assert.Contains(Module, run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-host-version-unsatisfied", run.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("restore-capability-unresolved", run.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
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

}

/// <summary>
/// A private copy of the <c>NuplaneHost</c> fixture's build output, with whatever Nuplane configuration and
/// feed contents one test needs. A restore writes under the host it is given, so every test gets its own.
/// </summary>
internal sealed class RestoreHost : IDisposable
{
    private readonly TempDirectory root = new("elsa-cli-restore-host-");

    public RestoreHost(string fixture = "NuplaneHost") => Copy(DotnetElsa.Host(fixture), Path);

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

    /// <summary>
    /// Writes the host's own <c>appsettings.json</c>, carrying one or more feed declarations and, when given,
    /// a <c>Nuplane:Capabilities</c> section (the shape spec 172 D3 defines) and a
    /// <c>Nuplane:HostProvidedPackages</c> list — each written verbatim rather than through a helper, so a
    /// test reads as the operator's file.
    /// </summary>
    public void Configure(string feeds, string? capabilities = null, string? hostProvidedPackages = null) =>
        File.WriteAllText(System.IO.Path.Join(Path, "appsettings.json"), Settings(feeds, capabilities, hostProvidedPackages));

    /// <summary>Writes the <c>appsettings.&lt;environment&gt;.json</c> overlay the host would layer on top.</summary>
    public void ConfigureOverlay(string environment, string feeds) =>
        File.WriteAllText(System.IO.Path.Join(Path, $"appsettings.{environment}.json"), Settings(feeds));

    /// <summary>
    /// The host's own dependency file — the one the worker is launched with (<c>--depsfile</c>), and so the
    /// one Nuplane reads the host's package versions from during a restore.
    /// </summary>
    public HostDepsFile Deps => HostDepsFile.Read(Directory.EnumerateFiles(Path, "*.deps.json").Single());

    /// <summary>
    /// Packs the fixture module into the host's own drop folder as a real <c>.nupkg</c> — a zip carrying a
    /// nuspec and one <c>lib/net10.0</c> assembly, which is all Nuplane extracts and reads. No fixture
    /// <c>.nupkg</c> is committed, because the assembly it wraps is a build output.
    /// </summary>
    public void Feed(string packageId, string version) =>
        Pack(packageId, version, [typeof(WidgetsDbContext).Assembly.Location], capabilities: null);

    /// <summary>
    /// The fixture module packed with one nuspec dependency — the edge a module built against a newer host
    /// declares, as <c>&lt;dependency id="…" version="…" /&gt;</c>, and the only thing Nuplane reads to decide
    /// whether the host it lands on can satisfy it.
    /// </summary>
    public void FeedDependingOn(string packageId, string version, string dependencyId, string versionRange) =>
        Pack(packageId, version, [typeof(WidgetsDbContext).Assembly.Location], capabilities: null, (dependencyId, versionRange));

    /// <summary>
    /// The same module packed the way a real EF module package ships since spec 172 FR-001: with a
    /// package-root <c>nuplane.json</c> declaring the <c>ef-provider</c> capability, one option per engine
    /// it can bind, each pinned to the version this repository builds against.
    /// </summary>
    public void FeedWithEngineCapability(string packageId, string version) =>
        Pack(packageId, version, [typeof(WidgetsDbContext).Assembly.Location], EngineCapabilityDeclaration);

    /// <summary>
    /// The engine package one <c>ef-provider</c> option names, packed from this test project's own resolved
    /// copy of it — the same "copy what is already on disk into a nuspec and one <c>lib/net10.0</c> folder"
    /// the module above is packed with. The runtime assembly travels beside the EF provider because a
    /// generated script goes through the provider's real type mappings, which need it.
    /// </summary>
    public void FeedEngine(string option) =>
        Pack(EngineId(option), EngineVersion(option), EngineAssemblies(option), capabilities: null);

    /// <summary>The package id an <c>ef-provider</c> option names, as every module's declaration names it.</summary>
    public static string EngineId(string option) => option switch
    {
        "Sqlite" => "Microsoft.EntityFrameworkCore.Sqlite",
        "SqlServer" => "Microsoft.EntityFrameworkCore.SqlServer",
        "PostgreSql" => "Npgsql.EntityFrameworkCore.PostgreSQL",
        "MySql" => "MySql.EntityFrameworkCore",
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Not an ef-provider option.")
    };

    /// <summary>The version an <c>ef-provider</c> option is pinned to, matching <c>Directory.Packages.props</c>.</summary>
    public static string EngineVersion(string option) => option switch
    {
        "Sqlite" or "SqlServer" => "10.0.10",
        "PostgreSql" => "10.0.0",
        "MySql" => "10.0.9",
        _ => throw new ArgumentOutOfRangeException(nameof(option), option, "Not an ef-provider option.")
    };

    /// <summary>
    /// One package's active descriptor out of the state file this host records, or <c>null</c> when it
    /// records none for that id. Read as JSON rather than through Nuplane's reader for the same reason the
    /// rest of this suite runs the tool out of process: what an operator can inspect is the file.
    /// </summary>
    public JsonElement? Recorded(string packageId)
    {
        using var state = JsonDocument.Parse(File.ReadAllBytes(StateFile));
        return state.RootElement.GetProperty("activePackageDescriptorsById").EnumerateObject()
            .Where(package => string.Equals(package.Name, packageId, StringComparison.OrdinalIgnoreCase))
            .Select(package => (JsonElement?)package.Value.Clone())
            .FirstOrDefault();
    }

    /// <summary>
    /// Drops a file the feed indexes as <c>packageId</c>/<c>version</c> by name, but that is not a readable
    /// package at all — the shape a pinned request resolves to a candidate for, but the cycle cannot
    /// actually acquire.
    /// </summary>
    public void FeedCorrupt(string packageId, string version)
    {
        Directory.CreateDirectory(FeedDirectory);
        File.WriteAllBytes(System.IO.Path.Join(FeedDirectory, $"{packageId}.{version}.nupkg"), [0x00, 0x01, 0x02, 0x03]);
    }

    /// <summary>
    /// The declaration every EF module package ships (spec 172 FR-001), in the fixture module's own copy:
    /// schema 2, one capability, one option per engine, each carrying that engine's package id and the exact
    /// single-point version this repository pins.
    /// </summary>
    private static string EngineCapabilityDeclaration =>
        $$"""
          {
            "schemaVersion": 2,
            "capabilities": [
              {
                "name": "ef-provider",
                "description": "The EF Core relational provider engine this module binds at run time.",
                "options": [
                  { "name": "Sqlite",     "packageId": "{{EngineId("Sqlite")}}",     "version": "[{{EngineVersion("Sqlite")}}]" },
                  { "name": "SqlServer",  "packageId": "{{EngineId("SqlServer")}}",  "version": "[{{EngineVersion("SqlServer")}}]" },
                  { "name": "PostgreSql", "packageId": "{{EngineId("PostgreSql")}}", "version": "[{{EngineVersion("PostgreSql")}}]" },
                  { "name": "MySql",      "packageId": "{{EngineId("MySql")}}",      "version": "[{{EngineVersion("MySql")}}]" }
                ]
              }
            ]
          }
          """;

    /// <summary>
    /// The assemblies an engine package hands over, taken from this test project's own output because it
    /// references the same central pin the declaration names. A provider's generated SQL goes through its
    /// real type mappings, so its ADO runtime travels with it.
    /// </summary>
    private static string[] EngineAssemblies(string option) => option switch
    {
        "PostgreSql" => [Beside("Npgsql.EntityFrameworkCore.PostgreSQL.dll"), Beside("Npgsql.dll")],
        _ => throw new ArgumentOutOfRangeException(
            nameof(option), option, "Only the engine this test project resolves can be packed from its own output.")
    };

    private static string Beside(string assembly) => System.IO.Path.Join(AppContext.BaseDirectory, assembly);

    /// <summary>
    /// One <c>.nupkg</c>: a nuspec, the assemblies under <c>lib/net10.0</c>, and — for a package that
    /// declares one — a package-root <c>nuplane.json</c>, which is exactly where a real module package
    /// carries it (<c>&lt;None Update="nuplane.json" Pack="true" PackagePath="/" /&gt;</c>).
    /// </summary>
    private void Pack(
        string packageId,
        string version,
        IReadOnlyList<string> assemblies,
        string? capabilities,
        (string Id, string VersionRange)? dependency = null)
    {
        Directory.CreateDirectory(FeedDirectory);
        using var archive = ZipFile.Open(
            System.IO.Path.Join(FeedDirectory, $"{packageId}.{version}.nupkg"),
            ZipArchiveMode.Create);
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
                     <description>Packed by the test that needs a feed to restore from.</description>
                     {(dependency is { } edge
                         ? $"<dependencies><dependency id=\"{edge.Id}\" version=\"{edge.VersionRange}\" /></dependencies>"
                         : "<dependencies />")}
                   </metadata>
                 </package>
                 """);
        }

        if (capabilities is not null)
        {
            using var declaration = new StreamWriter(archive.CreateEntry("nuplane.json").Open());
            declaration.Write(capabilities);
        }

        foreach (var assembly in assemblies)
            archive.CreateEntryFromFile(assembly, $"lib/net10.0/{System.IO.Path.GetFileName(assembly)}");
    }

    private static string Settings(string feeds, string? capabilities = null, string? hostProvidedPackages = null) =>
        $$"""
          {
            "Nuplane": {
              {{(capabilities is null ? "" : $"\"Capabilities\": {capabilities},")}}
              {{(hostProvidedPackages is null ? "" : $"\"HostProvidedPackages\": {hostProvidedPackages},")}}
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
