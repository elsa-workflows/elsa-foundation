using Elsa.Cli.Worker;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The engine-capability consumer end to end (spec 172, issue #1939): a host whose modules arrive as
/// packages selects its EF provider engine with one configuration key instead of naming the engine package
/// in its closure, and <c>--provider</c> stays authoritative against that selection (ADR 0076 D4).
/// </summary>
/// <remarks>
/// <para>
/// Against <c>NuplaneCapabilityHost</c>, whose deps file names no engine at all. That is what makes these
/// assertions mean something: the engine the run binds can only be the one the selection put in the
/// package set, so an artifact produced here is an artifact produced from a capability-injected root.
/// </para>
/// <para>
/// The feed holds the fixture module packed with the same package-root <c>nuplane.json</c> a real EF module
/// package ships, plus the engine packages those options name, packed from this test project's own resolved
/// copies — the same "zip a nuspec and a <c>lib/net10.0</c> folder" the suite already packs the module with.
/// </para>
/// </remarks>
public sealed class EngineCapabilityCliTests : IDisposable
{
    private const string Module = "Acme.Widgets";
    private const string Version = "1.4.2";
    private const string Option = "PostgreSql";
    private const string Key = "Nuplane:Capabilities:ef-provider";

    private readonly RestoreHost host = new("NuplaneCapabilityHost");
    private readonly TempDirectory output = new("elsa-cli-capability-artifact-");

    public EngineCapabilityCliTests()
    {
        host.FeedWithEngineCapability(Module, Version);
        host.FeedEngine(Option);
    }

    public void Dispose()
    {
        host.Dispose();
        output.Dispose();
    }

    /// <summary>
    /// The case the key exists for, and the proof that the selection is what acquired the engine: the state
    /// file records it as a root of the module's own graph, with the source name that says which decision
    /// produced it — and the module's feed pattern never mentions it.
    /// </summary>
    [Fact]
    public void A_selected_engine_is_restored_as_a_root_naming_the_selection_that_asked_for_it()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");

        var run = Script(output.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        var engine = host.Recorded(RestoreHost.EngineId(Option));
        Assert.True(engine is not null, $"'{RestoreHost.EngineId(Option)}' is not in the restored package set.");
        Assert.Equal($"capability:ef-provider={Option}", engine!.Value.GetProperty("sourceName").GetString());
        Assert.Equal(RestoreHost.EngineVersion(Option), engine.Value.GetProperty("version").GetString());
        Assert.Contains(
            RestoreHost.EngineId(Option),
            engine.Value.GetProperty("rootPackageIds").EnumerateArray().Select(id => id.GetString()),
            StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The selection changes where the engine comes from and nothing else. A second host names the same
    /// engine package by hand — an explicit include pattern, the pre-spec-172 way — and the two artifacts
    /// are compared byte for byte, migration plan included, so a difference in the engine's recorded package
    /// facts would fail here.
    /// </summary>
    [Fact]
    public void The_artifact_equals_the_one_a_host_that_names_the_engine_by_hand_produces()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");
        Assert.Equal(ToolExitCode.Success, Script(output.Path, "--restore").ExitCode);

        using var byHand = new RestoreHost("NuplaneCapabilityHost");
        byHand.FeedWithEngineCapability(Module, Version);
        byHand.FeedEngine(Option);
        byHand.Configure(NamedEngineFeed);
        using var again = new TempDirectory("elsa-cli-capability-by-hand-");

        var run = DotnetElsa.Run(ScriptArguments(byHand, again.Path, "--restore"));

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.DoesNotContain(
            "capability:",
            byHand.Recorded(RestoreHost.EngineId(Option))!.Value.GetProperty("sourceName").GetString(),
            StringComparison.Ordinal);
        ArtifactAssert.SameBytes(output.Path, again.Path);
    }

    /// <summary>
    /// No selection and no explicit engine root is a refusal, never a guess (spec 172 FR-005). Nuplane
    /// refuses the declaring module with <c>capability-unselected</c>; the tool reports that as a refusal of
    /// its own naming the key and every declared option, rather than as "a package could not be installed",
    /// which is true and useless — the package is right there on the feed.
    /// </summary>
    [Fact]
    public void An_unselected_engine_refuses_naming_the_key_and_every_option_and_scripts_nothing()
    {
        host.Configure(ModuleFeed);

        var run = Script(output.Path, "--restore");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("restore-capability-unresolved", run.Error, StringComparison.Ordinal);
        Assert.Contains(Key, run.Error, StringComparison.Ordinal);
        Assert.Contains("capability-unselected", run.Error, StringComparison.Ordinal);
        Assert.Contains(Module, run.Error, StringComparison.Ordinal);
        foreach (var option in new[] { "Sqlite", "SqlServer", "PostgreSql", "MySql" })
            Assert.Contains(option, run.Error, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>
    /// The agreement rule (D4): the selection is compared with <c>--provider</c> and never taken for it.
    /// <c>script --provider Sqlite</c> would ordinarily be refused on the command and the provider alone —
    /// EF cannot script SQLite idempotently — but a host whose closure selects PostgreSql was never a SQLite
    /// host, so the disagreement is what it is told about.
    /// </summary>
    /// <remarks>
    /// This fixture carries no <c>shells.json</c>, which is the second thing the test holds: the selection
    /// is a fact about the host's package closure, so the check must run on a host whose per-feature check
    /// reports <c>not-checked</c>. Asserted rather than left to the fixture's shape, because a fixture that
    /// quietly gained one would make this test pass for the wrong reason.
    /// </remarks>
    [Fact]
    public void A_provider_the_selection_does_not_contain_exits_three_naming_the_key()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");
        Assert.False(File.Exists(Path.Join(host.Path, "shells.json")), "This fixture is supposed to carry no shell configuration.");
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore").ExitCode);

        var run = DotnetElsa.Run(
            "persistence", "script", "--host", host.Path, "--provider", "Sqlite", "--modules", Module, "--output", output.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("provider-disagreement", run.Text, StringComparison.Ordinal);
        Assert.Contains(Key, run.Text, StringComparison.Ordinal);
        Assert.Contains($"'{Option}'", run.Text, StringComparison.Ordinal);
        Assert.DoesNotContain("sqlite-script-refused", run.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>
    /// The other refusal site, reached by a command that needs no engine of its own: <c>validate</c> opens
    /// the database directly, so it passes through the merged check rather than the SQLite short-circuit
    /// above — and is refused before it opens anything.
    /// </summary>
    [Fact]
    public void A_database_command_is_refused_on_the_selection_before_it_opens_anything()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore").ExitCode);
        var database = Path.Join(output.Path, "never-opened.db");

        var run = DotnetElsa.Run(
            new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = $"Data Source={database}" },
            "persistence", "validate", "--host", host.Path, "--provider", "Sqlite", "--modules", Module);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("provider-disagreement", run.Text, StringComparison.Ordinal);
        Assert.Contains(Key, run.Text, StringComparison.Ordinal);
        Assert.False(File.Exists(database), "A run refused on the selection opened a database anyway.");
    }

    /// <summary>
    /// The direction that must not be mistaken for the one above: the same host, the same key, and the
    /// provider the selection does contain proceeds and writes the artifact. Without this the refusal test
    /// would pass just as well against a check that refused everything.
    /// </summary>
    [Fact]
    public void The_provider_the_selection_contains_proceeds()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");

        var run = Script(output.Path, "--restore");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.True(File.Exists(output.File(MigrationPlan.FileName)), "The agreeing run wrote no manifest.");
    }

    /// <summary>
    /// A host that runs two engines on purpose selects both, and the agreement is containment rather than
    /// equality: the requested provider is one of them, so the run proceeds.
    /// </summary>
    /// <remarks>
    /// The package set is restored under the one-engine selection first and the second engine is added to
    /// the key afterwards, because acquiring a second engine would need that engine's own package in the
    /// feed and this test is about the comparison, not about what a second root costs to install.
    /// </remarks>
    [Fact]
    public void A_multi_engine_selection_that_contains_the_requested_provider_agrees()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore").ExitCode);
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"Sqlite,{Option}\" }}");

        var run = Script(output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.True(File.Exists(output.File(MigrationPlan.FileName)), "A selection containing --provider was refused.");
    }

    /// <summary>
    /// The object form of the same selection, which is how a host that also overrides the version or names a
    /// feed writes it. The <c>Option</c> child is read exactly as the bare value is, so the agreement rule
    /// cannot depend on which shape the operator chose.
    /// </summary>
    [Fact]
    public void The_object_form_of_the_selection_is_read_the_same_way_as_the_bare_value()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": {{ \"Option\": \"{Option}\", \"Version\": \"[{RestoreHost.EngineVersion(Option)}]\" }} }}");

        var agreed = Script(output.Path, "--restore");
        Assert.Equal(ToolExitCode.Success, agreed.ExitCode);

        using var refused = new TempDirectory("elsa-cli-capability-object-form-");
        var run = DotnetElsa.Run(
            "persistence", "script", "--host", host.Path, "--provider", "MySql", "--modules", Module, "--output", refused.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains(Key, run.Text, StringComparison.Ordinal);
    }

    /// <summary>
    /// A selection Nuplane's own reader cannot resolve — a JSON array, which is neither the string form nor
    /// the object form — is refused rather than read as "no selection at all". Silently skipping the
    /// agreement check is the one outcome that looks like success while comparing nothing.
    /// </summary>
    [Fact]
    public void A_selection_shape_nuplane_does_not_read_is_refused_rather_than_ignored()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": [ \"{Option}\" ] }}");

        var run = DotnetElsa.Run(
            "persistence", "script", "--host", host.Path, "--provider", Option, "--modules", Module, "--output", output.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("capability-selection-invalid", run.Text, StringComparison.Ordinal);
        Assert.Contains(Key, run.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>
    /// The other way the comparison could quietly not happen: the host's configuration is there and cannot
    /// be read at all, so whether it selects an engine is unknown. Unknown is refused, because the
    /// alternative is an artifact for a provider this host may never have agreed to, produced in silence.
    /// </summary>
    /// <remarks>
    /// The package set is restored from a well-formed file first and only then is the file corrupted, so the
    /// run under test gets all the way past resolution to the one read this test is about — and the
    /// configuration provider the host itself composes is what fails on it, rather than a parser of the
    /// tool's own. Deliberately no <c>--restore</c>: that pass reads the same file, and it would refuse
    /// first, for a reason of its own.
    /// </remarks>
    [Fact]
    public void A_configuration_that_cannot_be_read_at_all_is_refused_rather_than_read_as_no_selection()
    {
        host.Configure(ModuleFeed, $"{{ \"ef-provider\": \"{Option}\" }}");
        Assert.Equal(ToolExitCode.Success, DotnetElsa.Run("persistence", "list", "--host", host.Path, "--restore").ExitCode);
        File.WriteAllText(Path.Join(host.Path, "appsettings.json"), "{ \"Nuplane\": { this is not JSON ");

        var run = Script(output.Path);

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("capability-selection-unreadable", run.Text, StringComparison.Ordinal);
        Assert.Contains(host.Path, run.Text, StringComparison.Ordinal);
        Assert.Contains(Key, run.Text, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>The module's own feed pattern, which deliberately never matches an engine package.</summary>
    private static string ModuleFeed =>
        """
          "local-packages": {
            "DirectoryPath": "packages",
            "IncludePatterns": [ "Acme.*" ]
          }
        """;

    /// <summary>The pre-spec-172 shape: the engine named by hand, as an explicit pinned root beside the modules.</summary>
    private static string NamedEngineFeed =>
        $$"""
            "local-packages": {
              "DirectoryPath": "packages",
              "IncludePatterns": [ "Acme.*", "{{RestoreHost.EngineId(Option)}}" ]
            }
          """;

    private CliRun Script(string target, params string[] extra) => DotnetElsa.Run(ScriptArguments(host, target, extra));

    private static string[] ScriptArguments(RestoreHost target, string output, params string[] extra) =>
    [
        "persistence", "script",
        "--host", target.Path,
        "--provider", Option,
        "--modules", Module,
        "--output", output,
        .. extra
    ];
}
