using Acme.Widgets;
using Elsa.Cli.Worker;
using Elsa.Persistence.EntityFramework.Tooling;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Xunit;

namespace Elsa.Cli.Tests;

/// <summary>
/// The tool run against a packaged host: build output, a deps file, and no source tree anywhere (spec 171
/// User Stories 2 and 6).
/// </summary>
public sealed class PersistenceCliTests : IDisposable
{
    /// <summary>The 13-name vocabulary, plus the third-party module the fixture host installs beside it.</summary>
    private static readonly string[] EveryModuleTheFixtureHostInstalls =
    [
        "Acme.Widgets",
        "Activities.Design",
        "Diagnostics.OpenTelemetry",
        "Diagnostics.StructuredLogs",
        "Elsa3.Activities.Design.Import",
        "Identity.Iam",
        "Identity.ProviderConfiguration",
        "Secrets",
        "Studio.Preferences",
        "Workflows.Design",
        "Workflows.Publishing",
        "Workflows.Runtime",
        "Workflows.Runtime.Distributed.CommandTransport",
        "Workflows.Runtime.Distributed.Placement"
    ];

    private static readonly JsonSerializerOptions ToolingJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly TempDirectory output = new("elsa-cli-artifact-");

    public void Dispose() => output.Dispose();

    /// <summary>
    /// Spec 171 User Story 2's independent test: a packaged host, no <c>--modules</c>, and every installed
    /// EF module named. An unscoped selection is the one that cannot be checked against a list the caller
    /// supplied, so this is what proves discovery sees the whole closure.
    /// </summary>
    [Fact]
    public void List_names_every_module_a_packaged_host_installs()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("Host"));

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal(
            EveryModuleTheFixtureHostInstalls,
            run.Output.Split('\n').Skip(1).Select(line => line.Split("  ")[0].Trim()).Where(name => name.Length > 0 && !name.EndsWith("module(s).", StringComparison.Ordinal)));
    }

    /// <summary>A third-party module is indistinguishable in shape from a first-party one (User Story 6, scenario 1).</summary>
    [Fact]
    public void List_reports_a_third_party_module_the_same_way_it_reports_a_first_party_one()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("MinimalHost"));

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("Acme.Widgets", run.Output, StringComparison.Ordinal);
        Assert.Contains("WidgetsDbContext", run.Output, StringComparison.Ordinal);
        Assert.Contains("__EFMigrationsHistory_AcmeWidgets", run.Output, StringComparison.Ordinal);
        Assert.Contains("PostgreSql, SqlServer", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The slice's own acceptance criterion: what the worker produces against a packaged host is what the
    /// host's tooling entry point produces in process for the same inputs — byte for byte, SQL and manifest
    /// alike. A process boundary that normalized a line ending or reordered a key would show up here and
    /// nowhere else.
    /// </summary>
    [Fact]
    public async Task Script_against_a_packaged_host_matches_what_the_host_produces_in_process()
    {
        Assert.Equal(ToolExitCode.Success, Script("MinimalHost", output.Path, "PostgreSql", "Acme.Widgets").ExitCode);
        using var inProcess = new TempDirectory("elsa-cli-in-process-");

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(output.File(MigrationPlan.FileName)));
        var plan = manifest.RootElement;
        var module = plan.GetProperty("modules")[0];
        var request = new
        {
            version = 1,
            command = "script",
            provider = plan.GetProperty("provider").GetString(),
            output = inProcess.Path,
            selection = new { kind = "modules", modules = new[] { "Acme.Widgets" } },
            host = new
            {
                name = plan.GetProperty("host").GetProperty("name").GetString(),
                providerAgreement = plan.GetProperty("host").GetProperty("providerAgreement").GetString(),
                environment = plan.GetProperty("host").GetProperty("environment").GetString()
            },
            engine = new
            {
                package = plan.GetProperty("engine").GetProperty("package").GetString(),
                version = plan.GetProperty("engine").GetProperty("version").GetString(),
                source = plan.GetProperty("engine").GetProperty("source").GetString()
            },
            packages = new[]
            {
                new
                {
                    assembly = module.GetProperty("assembly").GetString(),
                    id = module.GetProperty("package").GetProperty("id").GetString(),
                    version = module.GetProperty("package").GetProperty("version").GetString(),
                    source = module.GetProperty("package").GetProperty("source").GetString()
                }
            }
        };

        using var requestStream = new MemoryStream(JsonSerializer.SerializeToUtf8Bytes(request, ToolingJson));
        using var responseStream = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(requestStream, responseStream, [typeof(WidgetsDbContext).Assembly], CancellationToken.None);

        Assert.Equal(0, exitCode);
        AssertSameBytes(inProcess.Path, output.Path);
    }

    [Fact]
    public void Script_produces_the_same_bytes_every_run()
    {
        using var second = new TempDirectory("elsa-cli-artifact-again-");

        Assert.Equal(ToolExitCode.Success, Script("MinimalHost", output.Path, "PostgreSql", "Acme.Widgets").ExitCode);
        Assert.Equal(ToolExitCode.Success, Script("MinimalHost", second.Path, "PostgreSql", "Acme.Widgets").ExitCode);

        AssertSameBytes(output.Path, second.Path);
    }

    [Fact]
    public void Script_writes_lf_utf8_files_with_no_byte_order_mark()
    {
        Script("MinimalHost", output.Path, "PostgreSql", "Acme.Widgets");

        foreach (var file in Directory.EnumerateFiles(output.Path))
        {
            var bytes = File.ReadAllBytes(file);
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.False(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{file} starts with a byte-order mark.");
            Assert.DoesNotContain(output.Path, Encoding.UTF8.GetString(bytes), StringComparison.Ordinal);
        }
    }

    /// <summary>A third-party module produces a numbered file and a manifest entry like any other (User Story 6, scenario 2).</summary>
    [Fact]
    public void Script_all_numbers_every_module_the_host_installs_including_the_third_party_one()
    {
        var run = Script("Host", output.Path, "PostgreSql", modules: null);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("01-acme-widgets.sql", Directory.EnumerateFiles(output.Path, "*.sql").Select(Path.GetFileName).Order(StringComparer.Ordinal).First());
        Assert.Equal(
            EveryModuleTheFixtureHostInstalls.Length,
            Directory.EnumerateFiles(output.Path, "*.sql").Count());

        using var manifest = JsonDocument.Parse(File.ReadAllBytes(output.File(MigrationPlan.FileName)));
        var widgets = manifest.RootElement.GetProperty("modules").EnumerateArray().First();
        Assert.Equal("Acme.Widgets", widgets.GetProperty("module").GetString());
        Assert.Equal("__EFMigrationsHistory_AcmeWidgets", widgets.GetProperty("historyTable").GetString());
        Assert.Equal("Acme.Widgets", widgets.GetProperty("package").GetProperty("id").GetString());
        Assert.Equal("host-deps-file", widgets.GetProperty("package").GetProperty("source").GetString());
    }

    [Fact]
    public void Plan_reports_the_migration_range_without_writing_anything()
    {
        var run = DotnetElsa.Run("persistence", "plan", "--host", DotnetElsa.Host("MinimalHost"), "--provider", "PostgreSql", "--modules", "Acme.Widgets");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("0 -> 20260102000000_AddLabel", run.Output, StringComparison.Ordinal);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output.Path));
    }

    /// <summary>
    /// Discovery covers the whole closure and fails closed, so a name collision withholds every module —
    /// and the refusal has to name both assemblies, or an operator reads it as "my host has no modules"
    /// (User Story 6, scenario 3).
    /// </summary>
    [Fact]
    public void Two_modules_whose_names_collide_are_refused_naming_both_assemblies()
    {
        var run = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("CollisionHost"));

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("Acme.Widgets.Collision", run.Error, StringComparison.Ordinal);
        Assert.Contains("Acme.Widgets", run.Error, StringComparison.Ordinal);
        Assert.Contains("fails closed", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_refuses_a_provider_whose_engine_the_host_does_not_pin_and_writes_nothing()
    {
        var target = Path.Join(output.Path, "never-written");

        var run = Script("MinimalHost", target, "MySql", "Acme.Widgets");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("provider-engine-unavailable", run.Error, StringComparison.Ordinal);
        Assert.Contains("No other provider was tried.", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    /// <summary>A provider the module declares no context for is a named refusal, not a null-reference failure (FR-016).</summary>
    [Fact]
    public void Script_refuses_a_provider_the_module_declares_no_context_for()
    {
        var run = Script("Host", Path.Join(output.Path, "never-written"), "MySql", "Acme.Widgets");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("provider-unsupported-for-module", run.Error, StringComparison.Ordinal);
        Assert.Contains("'Acme.Widgets' declares no MySql context.", run.Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_refuses_sqlite_outright()
    {
        var target = Path.Join(output.Path, "never-written");

        var run = Script("Host", target, "Sqlite", "Acme.Widgets");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("SQLite cannot produce an idempotent script", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void A_module_the_host_does_not_declare_is_refused_before_anything_is_written()
    {
        var target = Path.Join(output.Path, "never-written");

        var run = Script("MinimalHost", target, "PostgreSql", "Contoso.Nope");

        Assert.Equal(ToolExitCode.ResolutionFailure, run.ExitCode);
        Assert.Contains("unknown-module", run.Error, StringComparison.Ordinal);
        Assert.False(Directory.Exists(target));
    }

    [Fact]
    public void Script_with_no_selector_is_refused_rather_than_defaulted()
    {
        Assert.Equal(ToolExitCode.Refusal, WithSelector().ExitCode);
        Assert.Contains("invalid-selection", WithSelector().Error, StringComparison.Ordinal);
    }

    [Fact]
    public void Script_with_two_selectors_is_refused_rather_than_resolved_by_precedence()
    {
        var run = WithSelector("--modules", "Acme.Widgets", "--all");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("invalid-selection", run.Error, StringComparison.Ordinal);
    }

    private CliRun WithSelector(params string[] selector) =>
        DotnetElsa.Run(
            ["persistence", "script", "--host", DotnetElsa.Host("MinimalHost"), "--provider", "PostgreSql", "--output", output.Path, .. selector]);

    [Fact]
    public void An_unrecognized_command_is_a_usage_error_rather_than_a_silent_no_op()
    {
        var run = DotnetElsa.Run("persistence", "apply", "--host", DotnetElsa.Host("MinimalHost"));

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("usage", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// A host that lives in its own schema is configured through <c>ELSA_EF_SCHEMA</c> today, and
    /// <c>tools/ef/module-migrate.sh</c> already reads it; the artifact has to target the same schema the
    /// host will look for its history table in (FR-029).
    /// </summary>
    [Fact]
    public void The_schema_environment_variable_is_honoured_and_an_explicit_schema_overrides_it()
    {
        var run = DotnetElsa.Run(
            new Dictionary<string, string> { ["ELSA_EF_SCHEMA"] = "elsa_from_the_environment" },
            "persistence", "script",
            "--host", DotnetElsa.Host("MinimalHost"),
            "--provider", "PostgreSql",
            "--modules", "Acme.Widgets",
            "--output", output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("elsa_from_the_environment", Schema());

        run = DotnetElsa.Run(
            new Dictionary<string, string> { ["ELSA_EF_SCHEMA"] = "elsa_from_the_environment" },
            "persistence", "script",
            "--host", DotnetElsa.Host("MinimalHost"),
            "--provider", "PostgreSql",
            "--modules", "Acme.Widgets",
            "--schema", "elsa_from_the_flag",
            "--output", output.Path);

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Equal("elsa_from_the_flag", Schema());
    }

    private string? Schema()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllBytes(output.File(MigrationPlan.FileName)));
        return manifest.RootElement.GetProperty("schema").GetString();
    }

    private static CliRun Script(string host, string target, string provider, string? modules) =>
        DotnetElsa.Run(
        [
            "persistence", "script",
            "--host", DotnetElsa.Host(host),
            "--provider", provider,
            "--output", target,
            .. modules is null ? (string[])["--all"] : ["--modules", modules]
        ]);

    private static void AssertSameBytes(string expected, string actual)
    {
        var expectedFiles = Files(expected);
        var actualFiles = Files(actual);

        Assert.Equal(expectedFiles.Keys, actualFiles.Keys);
        foreach (var (name, bytes) in expectedFiles)
            Assert.True(bytes.AsSpan().SequenceEqual(actualFiles[name]), $"{name} differs between the two runs.");
    }

    private static SortedDictionary<string, byte[]> Files(string directory) =>
        new(Directory.EnumerateFiles(directory).ToDictionary(file => Path.GetFileName(file), File.ReadAllBytes), StringComparer.Ordinal);
}
