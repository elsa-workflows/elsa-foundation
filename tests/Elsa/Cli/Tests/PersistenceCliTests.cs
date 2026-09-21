using Acme.Widgets;
using Elsa.Cli.Worker;
using Elsa.Persistence.EntityFramework.Tooling;
using Microsoft.Data.Sqlite;
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

    /// <summary>
    /// The comma-separated and repeated-flag forms this tool's <c>--modules</c> help text and the README
    /// both promise still resolve to the same selection now that <c>AllowMultipleArgumentsPerToken</c> is
    /// off (it used to swallow an unrecognized flag typed after <c>--modules</c>, which is why it was
    /// removed) — pinned here so a future change to option wiring cannot silently break either form.
    /// </summary>
    [Fact]
    public void Modules_comma_separated_and_repeated_forms_resolve_to_the_same_selection()
    {
        var commaSeparated = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("Host"), "--modules", "Secrets,Workflows.Design");
        var repeated = DotnetElsa.Run("persistence", "list", "--host", DotnetElsa.Host("Host"), "--modules", "Secrets", "--modules", "Workflows.Design");

        Assert.Equal(ToolExitCode.Success, commaSeparated.ExitCode);
        Assert.Equal(ToolExitCode.Success, repeated.ExitCode);
        Assert.Equal(ListedModules(commaSeparated), ListedModules(repeated));
        Assert.Equal(["Secrets", "Workflows.Design"], ListedModules(commaSeparated));
    }

    // README.md documents `--packages <dir>` as "Repeatable" only — it makes no comma-separated promise
    // for --packages (unlike --modules, which splits each value on comma), and the repeated form is
    // already exercised by PackageRootProbeTests, so no --packages test is added here.

    private static string[] ListedModules(CliRun run) =>
        [.. run.Output.Split('\n').Skip(1).Select(line => line.Split("  ")[0].Trim()).Where(name => name.Length > 0 && !name.EndsWith("module(s).", StringComparison.Ordinal))];

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

    /// <summary>
    /// The command surface is closed (FR-024): a command no build implements is a usage error rather than
    /// one that quietly does nothing.
    /// </summary>
    [Fact]
    public void An_unrecognized_command_is_a_usage_error_rather_than_a_silent_no_op()
    {
        var run = DotnetElsa.Run("persistence", "un-migrate", "--host", DotnetElsa.Host("MinimalHost"));

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("usage", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// Spec 171 User Story 5 through the real tool: on a database with no legacy rows, <c>post-migrate</c>
    /// runs nothing and says so, and <c>apply</c> is not made to report a required action by it. The legacy
    /// round trip itself is driven against the frozen tooling contract in the Secrets suite, where a legacy
    /// row can be written; this is the command-surface half — that it exists, reaches the host, and reports.
    /// </summary>
    [Fact]
    public void Post_migrate_reaches_the_host_and_reports_nothing_required_on_a_clean_database()
    {
        var db = Path.Join(output.Path, "elsa-post-migrate.db");
        var env = new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = $"Data Source={db}" };

        Assert.Equal(
            ToolExitCode.Success,
            DotnetElsa.Run(env, "persistence", "apply", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets").ExitCode);

        var run = DotnetElsa.Run(env, "persistence", "post-migrate", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.Success, run.ExitCode);
        Assert.Contains("SecretsProjectionReindex", run.Output, StringComparison.Ordinal);
        Assert.Contains("Nothing required", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>post-migrate</c> takes its connection the same way <c>apply</c> and <c>validate</c> do (FR-030,
    /// D7), so a database it cannot reach is a database failure and never a clean exit.
    /// </summary>
    [Fact]
    public void Post_migrate_needs_a_connection_and_never_exits_zero_without_one()
    {
        var run = DotnetElsa.Run(
            new Dictionary<string, string>(),
            "persistence", "post-migrate",
            "--host", DotnetElsa.Host("Host"),
            "--provider", "Sqlite",
            "--modules", "Secrets",
            "--connection-env", "ELSA_EF_CONNECTION_THAT_IS_NOT_SET");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("connection-missing", run.Error, StringComparison.Ordinal);
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

    /// <summary>
    /// The slice's own acceptance criterion (#1876): the same database, applied and then validated,
    /// exits 0 both times. SQLite needs no container, which is what makes this reachable on a machine
    /// where docker pulls are blocked.
    /// </summary>
    [Fact]
    public void Apply_then_validate_against_the_same_sqlite_database_both_exit_zero()
    {
        var db = Path.Join(output.Path, "elsa-apply-validate.db");
        var env = new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = $"Data Source={db}" };

        var apply = DotnetElsa.Run(env, "persistence", "apply", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.Success, apply.ExitCode);
        Assert.True(File.Exists(db));

        var validate = DotnetElsa.Run(env, "persistence", "validate", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.Success, validate.ExitCode);
        Assert.Contains("No pending migrations.", validate.Output, StringComparison.Ordinal);
    }

    /// <summary>Reading the connection off the named environment variable is the default, unnamed flag.</summary>
    [Fact]
    public void Apply_reads_a_custom_connection_env_name()
    {
        var db = Path.Join(output.Path, "elsa-custom-env.db");
        var env = new Dictionary<string, string> { ["CONTOSO_CONNECTION"] = $"Data Source={db}" };

        var apply = DotnetElsa.Run(
            env,
            "persistence", "apply",
            "--host", DotnetElsa.Host("Host"),
            "--provider", "Sqlite",
            "--modules", "Secrets",
            "--connection-env", "CONTOSO_CONNECTION");

        Assert.Equal(ToolExitCode.Success, apply.ExitCode);
        Assert.True(File.Exists(db));
    }

    /// <summary>Reading the connection off this process's own stdin, the only other way in (D7).</summary>
    [Fact]
    public async Task Apply_reads_the_connection_from_stdin()
    {
        var db = Path.Join(output.Path, "elsa-stdin.db");

        var apply = await DotnetElsa.RunWithStdinAsync(
            $"Data Source={db}",
            "persistence", "apply",
            "--host", DotnetElsa.Host("Host"),
            "--provider", "Sqlite",
            "--modules", "Secrets",
            "--connection-stdin");

        Assert.Equal(ToolExitCode.Success, apply.ExitCode);
        Assert.True(File.Exists(db));
    }

    /// <summary>
    /// Validate fails closed on a pending migration and applies nothing — proved by asserting the database
    /// is unchanged, not merely by the exit code (FR-052).
    /// </summary>
    [Fact]
    public void Validate_fails_and_applies_nothing_when_a_migration_is_pending()
    {
        var db = Path.Join(output.Path, "elsa-pending.db");
        var env = new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = $"Data Source={db}" };

        var validate = DotnetElsa.Run(env, "persistence", "validate", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.NegativeResult, validate.ExitCode);
        Assert.Contains("pending-migrations", validate.Error, StringComparison.Ordinal);
        // Nothing was applied: not just the exit code, but the database itself carries no table at all —
        // SQLite may create the file lazily on connect, but validate's own policy never migrates (FR-052).
        Assert.Equal(0L, TableCount(db));
    }

    private static long TableCount(string sqliteDatabase)
    {
        if (!File.Exists(sqliteDatabase))
            return 0;

        using var connection = new SqliteConnection($"Data Source={sqliteDatabase}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table'";
        return (long)command.ExecuteScalar()!;
    }

    /// <summary>
    /// There is no <c>--connection</c> flag (D7): a script that passes one must be told, not silently
    /// handed a command that ignored it.
    /// </summary>
    [Theory]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    public void A_connection_flag_is_rejected_as_a_usage_error(string command)
    {
        var run = DotnetElsa.Run(
            "persistence", command,
            "--host", DotnetElsa.Host("Host"),
            "--provider", "Sqlite",
            "--modules", "Secrets",
            "--connection", "Data Source=whatever.db");

        Assert.Equal(ToolExitCode.Refusal, run.ExitCode);
        Assert.Contains("usage", run.Error, StringComparison.Ordinal);
    }

    /// <summary>
    /// D7's promise extends past the process boundary: the connection string must not appear anywhere an
    /// operator can read, including a failure's own message. This forces a real failure — a SQLite path
    /// that cannot be opened — with a sentinel embedded in the connection string, and checks both output
    /// streams for it.
    /// </summary>
    [Fact]
    public void A_connection_string_never_appears_in_any_output_even_on_failure()
    {
        const string sentinel = "SENTINEL-CREDENTIAL-4f2b91";
        var connection = $"Data Source=/nonexistent-dir-{sentinel}/db.sqlite";
        var env = new Dictionary<string, string> { ["ELSA_EF_CONNECTION"] = connection };

        var apply = DotnetElsa.Run(env, "persistence", "apply", "--host", DotnetElsa.Host("Host"), "--provider", "Sqlite", "--modules", "Secrets");

        Assert.Equal(ToolExitCode.DatabaseFailure, apply.ExitCode);
        Assert.DoesNotContain(sentinel, apply.Text, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, apply.Text, StringComparison.Ordinal);
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
