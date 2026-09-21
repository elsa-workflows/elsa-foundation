using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;
using Xunit.Sdk;

namespace Elsa.Persistence.EntityFrameworkCore.Migrations.Tests;

/// <summary>
/// The frozen <see cref="EfToolingHost"/> contract, exercised the way the CLI worker will: versioned JSON
/// in, versioned JSON out, and an artifact on disk. Determinism is the deliverable (spec 171 FR-043), so
/// the artifact assertions compare bytes rather than parsed structures.
/// </summary>
public sealed class EfToolingHostTests : IDisposable
{
    // Three modules whose ordinal order is neither the order they are passed in nor the reverse of it.
    private static readonly string[] Selection = ["Studio.Preferences", "Secrets", "Activities.Design"];

    private static readonly string[] ServerProviders = ["SqlServer", "PostgreSql", "MySql"];

    private static readonly IReadOnlyList<EfModuleDescriptor> Descriptors = EfModuleCatalog.Discover(ModuleContextCatalog.Modules);

    private static readonly JsonSerializerOptions RequestJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private static readonly Regex IsoTimestamp = new(@"\d{4}-\d{2}-\d{2}[T ]\d{2}:\d{2}", RegexOptions.Compiled);

    private readonly string root = Directory.CreateTempSubdirectory("elsa-ef-tooling-").FullName;

    /// <summary>
    /// Clears the connection pool before removing the directory, for the reason recorded on
    /// <c>TemporarySqliteDatabase</c> in <c>tests/Elsa/Persistence/EntityFramework/Tests</c> (#1884):
    /// Microsoft.Data.Sqlite pools connections, so a bare delete can still find the file handle held —
    /// harmless on macOS/Linux, an <see cref="IOException"/> on Windows. This fixture owns a whole temp
    /// directory rather than one database path, so it clears the pool and removes the tree itself instead
    /// of delegating to that type's per-file teardown, which is also why that file is not compiled in here.
    /// A residual lock is swallowed for the same reason it is there: teardown must never fail an
    /// otherwise-green test.
    /// </summary>
    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch (Exception failure) when (failure is IOException or UnauthorizedAccessException)
        {
            // A residual lock is tolerated; a missing directory is not. DirectoryNotFoundException derives
            // from IOException, so the filter above would otherwise swallow it. Nothing today can raise it:
            // the tooling host contains no Directory.Delete or File.Delete at all, and every path this class
            // builds is a subdirectory of root. That is precisely why it rethrows rather than being tolerated
            // - it is a canary for the host gaining a delete it should not have, not a guard on a live
            // failure, and a silently missing temp directory would be the first sign of one.
            if (failure is DirectoryNotFoundException)
                throw;

            // Tolerated, but not silent. A swallowed teardown failure left no trace at all, so a leaked
            // handle looked identical to a clean run; stderr keeps the run green while leaving the type and
            // message in the log for whoever investigates the next flake.
            Console.Error.WriteLine(
                $"{nameof(EfToolingHostTests)} teardown could not remove '{root}': {failure.GetType().Name}: {failure.Message}");
        }
    }

    public static TheoryData<string> Providers() => [.. ServerProviders];

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Script_is_byte_stable_and_independent_of_the_order_modules_were_given(string provider)
    {
        var forward = await ScriptAsync(provider, Selection, "forward");
        var again = await ScriptAsync(provider, Selection, "again");
        var reversed = await ScriptAsync(provider, [.. Selection.Reverse()], "reversed");

        AssertSameBytes(forward, again);
        AssertSameBytes(forward, reversed);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Script_numbers_files_topologically_then_by_ordinal_name(string provider)
    {
        var directory = await ScriptAsync(provider, Selection, "numbered");

        Assert.Equal(
            ["01-activities-design.sql", "02-secrets.sql", "03-studio-preferences.sql", "migration-plan.json"],
            Artifact(directory).Keys);
    }

    [Theory]
    [MemberData(nameof(Providers))]
    public async Task Script_output_carries_no_cr_no_bom_no_absolute_path_and_no_timestamp(string provider)
    {
        var directory = await ScriptAsync(provider, Selection, "clean");

        foreach (var (name, bytes) in Artifact(directory))
        {
            Assert.DoesNotContain((byte)'\r', bytes);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, $"{name} starts with a byte-order mark.");
            var text = Encoding.UTF8.GetString(bytes);
            foreach (var path in new[] { root, directory, Path.GetTempPath(), AppContext.BaseDirectory, Path.GetDirectoryName(typeof(EfToolingHost).Assembly.Location)! })
                Assert.DoesNotContain(path, text, StringComparison.Ordinal);
            Assert.DoesNotMatch(IsoTimestamp, text);
            Assert.DoesNotContain(DateTime.UtcNow.ToString("yyyy-MM-dd"), text, StringComparison.Ordinal);
            // The placeholder connection the contexts were configured with never reaches the artifact.
            Assert.DoesNotContain("elsa_design_time", text, StringComparison.Ordinal);
            // Nor does the tool's own version (FR-043); the versions in the manifest are EF's and the caller's.
            if (typeof(EfToolingHost).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion is { } toolVersion)
                Assert.DoesNotContain(toolVersion, text, StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The normalizer that makes Windows output byte-identical to Linux output (spec 171 FR-043):
    /// <c>EfToolingLineEndings.Utf8Lf</c> must fold both CRLF and a lone CR down to LF, add exactly one
    /// trailing newline, and leave the line structure otherwise untouched. This is the direct, non-vacuous
    /// counterpart to <see cref="Script_output_carries_no_cr_no_bom_no_absolute_path_and_no_timestamp"/>,
    /// which is vacuous on Linux and macOS because EF's own <c>Environment.NewLine</c> is already LF there.
    /// </summary>
    [Fact]
    public void Utf8Lf_folds_crlf_and_lone_cr_to_lf_and_preserves_line_structure()
    {
        var bytes = EfToolingLineEndings.Utf8Lf("line one\r\nline two\rline three\n");

        Assert.DoesNotContain((byte)'\r', bytes);
        Assert.Equal("line one\nline two\nline three\n", Encoding.UTF8.GetString(bytes));
    }

    /// <summary>
    /// The script is only worth committing if it is the idempotent one: every migration guarded against
    /// the module's own history table, so a DBA can re-run the file without a second thought.
    /// </summary>
    [Fact]
    public async Task Script_guards_every_migration_against_the_modules_own_history_table()
    {
        var directory = await ScriptAsync("PostgreSql", ["Secrets"], "idempotent");
        var sql = Encoding.UTF8.GetString(Artifact(directory)["01-secrets.sql"]);

        Assert.Contains(""""CREATE TABLE IF NOT EXISTS "__EFMigrationsHistory_ElsaSecrets"""", sql, StringComparison.Ordinal);
        using var manifest = JsonDocument.Parse(Artifact(directory)["migration-plan.json"]);
        var ids = manifest.RootElement.GetProperty("modules")[0].GetProperty("migrations").GetProperty("ids");
        Assert.NotEmpty(ids.EnumerateArray());
        foreach (var id in ids.EnumerateArray())
        {
            Assert.Contains(
                $"""IF NOT EXISTS(SELECT 1 FROM "__EFMigrationsHistory_ElsaSecrets" WHERE "MigrationId" = '{id.GetString()}')""",
                sql,
                StringComparison.Ordinal);
        }
    }

    /// <summary>
    /// The MySQL counterpart of the test above, and the assertion whose absence let #1914 ship: MySql
    /// appeared only in the determinism theories, so an artifact no MySQL server can execute stayed
    /// byte-stable, reproducible and unrunnable through four merged slices. Every guard has to sit inside one
    /// per-module stored procedure, because MySQL allows <c>IF … THEN</c> only inside a routine; the module's
    /// own history table still guards each one, and is still created outside the procedure, where
    /// <c>CREATE TABLE IF NOT EXISTS</c> is idempotent on its own.
    /// </summary>
    [Fact]
    public async Task Script_hoists_every_mysql_guard_into_the_modules_own_stored_procedure()
    {
        var directory = await ScriptAsync("MySql", ["Secrets"], "mysql-idempotent");
        var sql = Encoding.UTF8.GetString(Artifact(directory)["01-secrets.sql"]);

        Assert.Contains("CREATE TABLE IF NOT EXISTS `__EFMigrationsHistory_ElsaSecrets`", sql, StringComparison.Ordinal);
        Assert.Contains(
            "DROP PROCEDURE IF EXISTS `elsa_migrate_secrets`;\nDELIMITER //\nCREATE PROCEDURE `elsa_migrate_secrets`()\nBEGIN\n",
            sql,
            StringComparison.Ordinal);
        Assert.EndsWith(
            "END //\nDELIMITER ;\nCALL `elsa_migrate_secrets`();\nDROP PROCEDURE `elsa_migrate_secrets`;\n",
            sql,
            StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(Artifact(directory)["migration-plan.json"]);
        var ids = manifest.RootElement.GetProperty("modules")[0].GetProperty("migrations").GetProperty("ids");
        Assert.NotEmpty(ids.EnumerateArray());
        foreach (var id in ids.EnumerateArray())
        {
            Assert.Contains(
                $"    IF NOT EXISTS(SELECT * FROM `__EFMigrationsHistory_ElsaSecrets` WHERE `MigrationId` = '{id.GetString()}') THEN\n",
                sql,
                StringComparison.Ordinal);
        }

        // Nothing MySQL's grammar refuses is left at the top level: no bare BEGIN beyond the procedure's own
        // body opener, no END;, and none of EF's transaction control, which MySQL's implicit DDL commit makes
        // a promise the file cannot keep.
        Assert.Equal(1, sql.Split('\n').Count(line => line == "BEGIN"));
        Assert.DoesNotContain("\nEND;\n", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\nSTART TRANSACTION;\n", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("\nCOMMIT;\n", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// #1914's rewrite belongs to MySQL alone. SQL Server's idempotent script is where MySQL's generator
    /// copied its top-level <c>IF NOT EXISTS</c> / <c>BEGIN</c> / <c>END;</c> from, and PostgreSQL's carries
    /// a <c>BEGIN</c> of its own inside every <c>DO $EF$</c> block, so a rewrite whose provider gate slipped
    /// would turn a perfectly good artifact into a stored procedure that still looked plausible. Each
    /// provider's own transaction statement is asserted present, because that is the line the rewrite drops.
    /// </summary>
    [Theory]
    [InlineData("SqlServer", "\nBEGIN TRANSACTION;\n")]
    [InlineData("PostgreSql", "\nSTART TRANSACTION;\n")]
    public async Task Script_leaves_a_provider_other_than_mysql_exactly_as_ef_generated_it(string provider, string transaction)
    {
        var sql = Encoding.UTF8.GetString(Artifact(await ScriptAsync(provider, ["Secrets"], "not-mysql"))["01-secrets.sql"]);

        Assert.Contains(transaction, sql, StringComparison.Ordinal);
        Assert.DoesNotContain("DELIMITER", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("CREATE PROCEDURE", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("elsa_migrate_", sql, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>Idempotent</c> without <c>Script</c> drops the batch separators <c>sqlcmd</c> needs, which no
    /// assertion about determinism would catch: the file would be byte-stable and unrunnable.
    /// </summary>
    [Fact]
    public async Task Script_keeps_the_batch_separator_a_sql_server_client_needs()
    {
        var directory = await ScriptAsync("SqlServer", ["Secrets"], "batches");

        Assert.Contains("\nGO\n", Encoding.UTF8.GetString(Artifact(directory)["01-secrets.sql"]), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Manifest_declares_schema_version_1_in_the_fixed_key_order()
    {
        var directory = await ScriptAsync("PostgreSql", ["Secrets"], "manifest");
        var bytes = Artifact(directory)["migration-plan.json"];
        var text = Encoding.UTF8.GetString(bytes);

        Assert.StartsWith("{\n  \"schemaVersion\": 1,\n  \"provider\": \"PostgreSql\",\n", text, StringComparison.Ordinal);
        Assert.EndsWith("\n", text, StringComparison.Ordinal);

        using var manifest = JsonDocument.Parse(bytes);
        var plan = manifest.RootElement;
        Assert.Equal(
            ["schemaVersion", "provider", "engine", "efCoreVersion", "schema", "idempotent", "ordering", "host", "modules"],
            Keys(plan));
        Assert.Equal(["package", "version", "source"], Keys(plan.GetProperty("engine")));
        Assert.Equal(["name", "providerAgreement", "shell", "environment"], Keys(plan.GetProperty("host")));
        Assert.Equal("dependsOn-then-name", plan.GetProperty("ordering").GetString());
        Assert.True(plan.GetProperty("idempotent").GetBoolean());
        Assert.Equal(JsonValueKind.Null, plan.GetProperty("schema").ValueKind);

        var module = Assert.Single(plan.GetProperty("modules").EnumerateArray());
        Assert.Equal(
            ["order", "module", "file", "sha256", "package", "assembly", "context", "historyTable", "migrations", "dependsOn", "postMigration"],
            Keys(module));
        Assert.Equal(["id", "version", "source"], Keys(module.GetProperty("package")));
        Assert.Equal(["from", "to", "count", "ids"], Keys(module.GetProperty("migrations")));
        Assert.Equal("Secrets", module.GetProperty("module").GetString());
        Assert.Equal("01-secrets.sql", module.GetProperty("file").GetString());
        Assert.Equal("SecretsPostgreSqlDbContext", module.GetProperty("context").GetString());
        Assert.Equal("__EFMigrationsHistory_ElsaSecrets", module.GetProperty("historyTable").GetString());
        Assert.Equal("0", module.GetProperty("migrations").GetProperty("from").GetString());
        Assert.Empty(module.GetProperty("dependsOn").EnumerateArray());
        // FR-048: the declared action, not the empty array a build that could not describe one had to refuse.
        var action = Assert.Single(module.GetProperty("postMigration").EnumerateArray());
        Assert.Equal(["id", "kind", "requiredWhen", "audit", "run"], Keys(action));
        Assert.Equal("SecretsProjectionReindex", action.GetProperty("id").GetString());
        Assert.Equal("projection-reindex", action.GetProperty("kind").GetString());
        Assert.Equal("legacy-projection-detected", action.GetProperty("requiredWhen").GetString());
        Assert.Equal("SecretsProjectionContract.HasLegacyProjectionsAsync", action.GetProperty("audit").GetString());
        Assert.Equal(
            "dotnet elsa persistence post-migrate --modules Secrets --provider PostgreSql",
            action.GetProperty("run").GetString());

        // The manifest's own hash is what a DBA and script-check compare, so it names the file it hashed.
        var sha = module.GetProperty("sha256").GetString();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Artifact(directory)["01-secrets.sql"])), sha);
    }

    [Fact]
    public async Task Script_refuses_sqlite_with_the_documented_message_and_writes_nothing()
    {
        var output = Path.Join(root, "sqlite");
        var run = await RunAsync(ScriptRequest("Sqlite", ["Secrets"], output));

        AssertExit(EfToolingExitCode.Refusal, run);
        Assert.Equal("error", run.Response.GetProperty("status").GetString());
        Assert.Equal("sqlite-script-refused", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            """
            script: SQLite cannot produce an idempotent script (EF throws NotSupportedException).
            Script a server provider, and bring a SQLite database up to date with:
              dotnet elsa persistence apply --provider Sqlite
            """.ReplaceLineEndings("\n"),
            run.Response.GetProperty("error").GetProperty("message").GetString());
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Plan_accepts_sqlite_and_reports_zero_to_head_offline()
    {
        var run = await RunAsync(new
        {
            Version = 1,
            Command = "plan",
            Provider = "Sqlite",
            Selection = new { Kind = "modules", Modules = new[] { "Secrets" } }
        });

        AssertExit(EfToolingExitCode.Success, run);
        var module = Assert.Single(run.Response.GetProperty("plan").GetProperty("modules").EnumerateArray());
        using var context = ModuleContextCatalog.Create(typeof(SecretsSqliteDbContext), ModuleContextCatalog.PlaceholderConnection("Sqlite"));
        var expected = context.Database.GetMigrations().ToArray();

        Assert.Equal(1, module.GetProperty("order").GetInt32());
        Assert.Equal("0", module.GetProperty("from").GetString());
        Assert.Equal(expected[^1], module.GetProperty("to").GetString());
        Assert.Equal(expected.Length, module.GetProperty("count").GetInt32());
        Assert.Equal(expected, module.GetProperty("ids").EnumerateArray().Select(id => id.GetString()).ToArray());
    }

    [Fact]
    public async Task List_reports_every_discovered_module_in_ordinal_name_order()
    {
        var run = await RunAsync(new { Version = 1, Command = "list" });

        AssertExit(EfToolingExitCode.Success, run);
        var modules = run.Response.GetProperty("list").GetProperty("modules").EnumerateArray().ToArray();
        Assert.Equal(
            Descriptors.Select(descriptor => descriptor.Name).Order(StringComparer.Ordinal),
            modules.Select(module => module.GetProperty("module").GetString()));

        var secrets = modules.Single(module => module.GetProperty("module").GetString() == "Secrets");
        Assert.Equal("Elsa.Secrets.Persistence.EntityFrameworkCore", secrets.GetProperty("assembly").GetString());
        Assert.Equal("SecretsDbContext", secrets.GetProperty("context").GetString());
        Assert.Equal("__EFMigrationsHistory_ElsaSecrets", secrets.GetProperty("historyTable").GetString());
        Assert.Equal(
            ["MySql", "PostgreSql", "SqlServer", "Sqlite"],
            secrets.GetProperty("providers").EnumerateArray().Select(provider => provider.GetString()));
    }

    [Fact]
    public async Task List_refuses_the_fields_it_has_no_use_for()
    {
        var run = await RunAsync(new { Version = 1, Command = "list", Provider = "PostgreSql" });

        AssertExit(EfToolingExitCode.Refusal, run);
        Assert.Equal("invalid-request", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["'provider' is not accepted by 'list'."], Details(run.Response));
    }

    [Theory]
    [InlineData("""{"version":1,"command":"migrate"}""", EfToolingExitCode.Refusal, "unknown-command")]
    [InlineData("""{"version":2,"command":"list"}""", EfToolingExitCode.Refusal, "unsupported-request-version")]
    [InlineData("""{"version":1,"command":"list","connection":"Host=db;Password=hunter2"}""", EfToolingExitCode.Refusal, "invalid-request")]
    [InlineData("""{"version":1,"command":"plan","provider":"Nope","selection":{"kind":"all"}}""", EfToolingExitCode.Refusal, "unknown-provider")]
    [InlineData("""{"version":1,"command":"plan","provider":"PostgreSql","selection":{"kind":"every"}}""", EfToolingExitCode.Refusal, "invalid-request")]
    [InlineData("""{"version":1,"command":"plan","provider":"PostgreSql","selection":{"kind":"modules","modules":["Nope"]}}""", EfToolingExitCode.ResolutionFailure, "unknown-module")]
    [InlineData("""not json""", EfToolingExitCode.Refusal, "invalid-request")]
    public async Task A_request_this_build_does_not_understand_is_refused_rather_than_answered(string request, int exitCode, string code)
    {
        var run = await RunAsync(request);

        AssertExit(exitCode, run);
        Assert.Equal("error", run.Response.GetProperty("status").GetString());
        Assert.Equal(code, run.Response.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// A field a future slice invents; a worker that sends it to this build must be told, not quietly
    /// handed an answer to a different question. The message must also not echo the value it refused.
    /// </summary>
    [Fact]
    public async Task An_unknown_request_property_is_refused_without_echoing_its_value()
    {
        var run = await RunAsync("""{"version":1,"command":"list","totallyUnknownField":"Host=db;Password=hunter2"}""");

        var message = run.Response.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("totallyUnknownField", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
    }

    /// <summary>
    /// <c>connection</c> (#1876) is a real field now, but only <c>apply</c>, <c>validate</c> and
    /// <c>post-migrate</c> open a database (D7); <c>list</c>, <c>plan</c> and <c>script</c> refuse it rather
    /// than silently ignoring it, and the refusal names the field, never the value.
    /// </summary>
    [Fact]
    public async Task Connection_is_refused_on_a_command_that_opens_no_database()
    {
        var run = await RunAsync("""{"version":1,"command":"list","connection":"Host=db;Password=hunter2"}""");

        AssertExit(EfToolingExitCode.Refusal, run);
        var message = run.Response.GetProperty("error").GetProperty("message").GetString()!;
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
        Assert.Contains(Details(run.Response), detail => detail.Contains("'connection' is not accepted", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    public async Task Connection_is_required_by_every_command_that_opens_a_database(string command)
    {
        var run = await RunAsync(new ApplyRequestBody { Command = command, Provider = "Sqlite", Selection = new() { Kind = "modules", Modules = ["Secrets"] } });

        AssertExit(EfToolingExitCode.Refusal, run);
        Assert.Contains(Details(run.Response), detail => detail.Contains("'connection' is required", StringComparison.Ordinal));
    }

    /// <summary>
    /// The slice's own acceptance criterion (#1876): <c>apply</c> followed by <c>validate</c> against the
    /// same database exits 0 both times, and <c>apply</c> reports what it actually applied.
    /// </summary>
    [Fact]
    public async Task Apply_then_validate_against_sqlite_round_trips_and_exits_zero()
    {
        var connection = $"Data Source={Path.Join(root, "apply-validate.db")}";

        var apply = await RunAsync(ApplyRequest("apply", "Sqlite", ["Secrets"], connection));
        AssertExit(EfToolingExitCode.Success, apply);
        var applied = apply.Response.GetProperty("apply").GetProperty("modules")[0];
        Assert.Equal("Secrets", applied.GetProperty("module").GetString());
        Assert.True(applied.GetProperty("applied").GetArrayLength() > 0);

        var validate = await RunAsync(ApplyRequest("validate", "Sqlite", ["Secrets"], connection));
        AssertExit(EfToolingExitCode.Success, validate);
        Assert.Equal("Secrets", validate.Response.GetProperty("validate").GetProperty("modules")[0].GetProperty("module").GetString());
    }

    /// <summary>
    /// <c>validate</c> fails closed (exit 1) on a pending migration and applies nothing (FR-052) — proved
    /// here by reading the database back, not merely by the exit code: no table exists at all, because
    /// <see cref="EfMigratePolicy.Validate"/> never calls <c>MigrateAsync</c>.
    /// </summary>
    [Fact]
    public async Task Validate_fails_closed_on_a_pending_migration_and_applies_nothing()
    {
        var db = Path.Join(root, "pending.db");
        var connection = $"Data Source={db}";

        var validate = await RunAsync(ApplyRequest("validate", "Sqlite", ["Secrets"], connection));

        AssertExit(EfToolingExitCode.NegativeResult, validate);
        Assert.Equal("pending-migrations", validate.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(0L, TableCount(db));
    }

    /// <summary>
    /// An unreadable database is a database failure (exit 4), never the negative result <c>validate</c>
    /// reports for a pending migration (exit 1): the two are classified by where the failure originates,
    /// not by exception type alone, so an operator is never told to run <c>apply</c> against a database
    /// that cannot be read at all. A file that exists but is not a database — rather than a missing
    /// directory, which Sqlite's own history check treats as "nothing applied yet" and so genuinely does
    /// report as pending — makes <c>GetPendingMigrationsAsync</c> itself throw, the same shape of failure
    /// the classification in <c>EfToolingHost.Validate</c> exists to tell apart from a real pending
    /// migration.
    /// </summary>
    [Fact]
    public async Task Validate_reports_an_unreadable_database_as_a_database_failure_not_pending_migrations()
    {
        var db = Path.Join(root, "corrupt.db");
        File.WriteAllText(db, "not a sqlite database");
        var connection = $"Data Source={db}";

        var validate = await RunAsync(ApplyRequest("validate", "Sqlite", ["Secrets"], connection));

        AssertExit(EfToolingExitCode.DatabaseFailure, validate);
        Assert.Equal("module-validate-failed", validate.Response.GetProperty("error").GetProperty("code").GetString());
        var message = validate.Response.GetProperty("error").GetProperty("message").GetString()!;
        Assert.DoesNotContain("apply", message, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// D7's promise extends past the process boundary: the connection string must not appear in a
    /// refusal's message, even when the underlying failure is a real database one. A sentinel embedded in
    /// a connection string an unreachable path forces proves it, rather than trusting that no driver ever
    /// echoes one back.
    /// </summary>
    [Fact]
    public async Task A_connection_string_never_appears_in_a_database_failure()
    {
        const string sentinel = "SENTINEL-4f2b91";
        var connection = $"Data Source=/nonexistent-dir-{sentinel}/db.sqlite";

        var apply = await RunAsync(ApplyRequest("apply", "Sqlite", ["Secrets"], connection));

        AssertExit(EfToolingExitCode.DatabaseFailure, apply);
        var message = apply.Response.GetProperty("error").GetProperty("message").GetString()!;
        Assert.DoesNotContain(sentinel, message, StringComparison.Ordinal);
        Assert.DoesNotContain(connection, message, StringComparison.Ordinal);
    }

    /// <summary>
    /// The exact-match replacement in <see cref="EfToolingRedaction.Redact"/> only catches a verbatim echo
    /// of the connection string it was given. A driver that re-serialises, re-cases or re-quotes the string
    /// before embedding it in a message — Npgsql, SqlClient and MySql's driver are not Sqlite, and none of
    /// them are proven not to — would slip straight through it. This drives
    /// <see cref="EfToolingRedaction.Redact"/> directly, through the public seam it is exposed for exactly
    /// this reason, with a message shaped exactly like that: the credential echoed back re-cased and
    /// re-quoted, not as the exact connection string this call was given. It proves both halves of what
    /// that pattern claims: the secret is gone, and the surrounding non-credential text does not get
    /// mangled along with it.
    /// </summary>
    [Fact]
    public void Redact_scrubs_a_recased_and_requoted_echo_of_the_credential_a_driver_might_produce()
    {
        const string secret = "Secret-Value-4f2b91";
        const string connection = $"Host=db;Username=u;Password={secret};Database=d";
        var reformatted = $"Npgsql.NpgsqlException: Login failed [ConnectionString: Host=db;USERNAME=u;PWD=\"{secret}\";Database=d]";

        var redacted = EfToolingRedaction.Redact(reformatted, connection);

        Assert.DoesNotContain(secret, redacted, StringComparison.Ordinal);
        Assert.Contains("Host=db", redacted, StringComparison.Ordinal);
        Assert.Contains("Database=d", redacted, StringComparison.Ordinal);
    }

    private static ApplyRequestBody ApplyRequest(string command, string provider, string[] modules, string connection) => new()
    {
        Command = command,
        Provider = provider,
        Selection = new() { Kind = "modules", Modules = modules },
        Connection = connection
    };

    private static long TableCount(string sqliteDatabase)
    {
        if (!File.Exists(sqliteDatabase))
            return 0;

        using var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={sqliteDatabase}");
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT count(*) FROM sqlite_master WHERE type = 'table'";
        return (long)command.ExecuteScalar()!;
    }

    [Fact]
    public async Task Script_refuses_a_module_whose_package_facts_are_missing_and_writes_nothing()
    {
        var output = Path.Join(root, "no-packages");
        var request = ScriptRequest("PostgreSql", ["Secrets", "Activities.Design"], output) with
        {
            Packages = [Package("Elsa.Secrets.Persistence.EntityFrameworkCore")]
        };

        var run = await RunAsync(request);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("package-metadata-missing", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(
            ["'Activities.Design' is in assembly 'Elsa.Activities.Design.Persistence.EntityFrameworkCore', which no 'packages' entry describes."],
            Details(run.Response));
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task Script_refuses_an_engine_package_that_is_not_the_one_the_provider_binds()
    {
        var output = Path.Join(root, "wrong-engine");
        var request = ScriptRequest("PostgreSql", ["Secrets"], output) with
        {
            Engine = new() { Package = "Pomelo.EntityFrameworkCore.MySql", Version = "9.9.9-test", Source = "host-deps-file" }
        };

        var run = await RunAsync(request);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("engine-package-mismatch", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(output));
    }

    /// <summary>
    /// Nothing already in the output directory is ever deleted, even a leftover this selection does not
    /// produce: whether that leftover is stale is <c>script-check</c>'s call to make (FR-045), not
    /// <c>script</c>'s.
    /// </summary>
    [Fact]
    public async Task Script_leaves_a_sql_file_it_does_not_produce_untouched()
    {
        var output = await ScriptAsync("PostgreSql", Selection, "leftover");
        var leftover = Path.Join(output, "04-gone.sql");
        File.WriteAllText(leftover, "SELECT 1;\n");

        var run = await RunAsync(ScriptRequest("PostgreSql", Selection, output));

        AssertExit(EfToolingExitCode.Success, run);
        Assert.Equal("SELECT 1;\n", File.ReadAllText(leftover));
    }

    [Fact]
    public async Task Script_run_twice_into_the_same_directory_succeeds()
    {
        var output = await ScriptAsync("PostgreSql", Selection, "rerun");
        var before = Artifact(output);

        var run = await RunAsync(ScriptRequest("PostgreSql", Selection, output));

        AssertExit(EfToolingExitCode.Success, run);
        AssertSameBytes(before, Artifact(output));
    }

    [Fact]
    public async Task A_dependency_cycle_is_refused_by_name_rather_than_ordered_arbitrarily()
    {
        var fixture = SyntheticEfModules.Build(
            "Acme.Cycle.Modules",
            new SyntheticModule("Acme.Beta", "AcmeBeta", DependsOn: ["Acme.Alpha"]),
            new SyntheticModule("Acme.Alpha", "AcmeAlpha", DependsOn: ["Acme.Beta"]));

        var run = await RunAsync(new { Version = 1, Command = "list" }, [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("dependency-cycle", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["Acme.Alpha -> Acme.Beta -> Acme.Alpha"], Details(run.Response));
    }

    [Fact]
    public async Task A_dependency_outside_the_selection_is_refused_by_name()
    {
        var fixture = SyntheticEfModules.Build(
            "Acme.Dangling.Modules",
            new SyntheticModule("Acme.Gamma", "AcmeGamma", DependsOn: ["Acme.Missing"]));

        var run = await RunAsync(new { Version = 1, Command = "list" }, [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("dependency-missing", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["'Acme.Gamma' depends on 'Acme.Missing', which is not in the selection."], Details(run.Response));
    }

    /// <summary>
    /// A declaration this build cannot turn into an <see cref="IEfPostMigrationAction"/> is refused for the
    /// whole selection, naming every offender. Skipping it instead would record <c>postMigration: []</c> for a
    /// module that has a real obligation, telling a DBA there is nothing left to run — the one failure this
    /// seam exists to prevent.
    /// </summary>
    [Theory]
    [InlineData(typeof(Uri), "does not implement IEfPostMigrationAction")]
    [InlineData(typeof(NoParameterlessConstructor), "has no public parameterless constructor")]
    [InlineData(typeof(BlankIdentifiers), "leaves Id, Kind, RequiredWhen, Audit blank")]
    public async Task Script_refuses_a_module_whose_declared_post_migration_action_this_build_cannot_use(Type declared, string reason)
    {
        // A module name of its own per case: these assemblies stay loaded, and the two-arg RunAsync overload
        // discovers every one of them at once, where two modules sharing a name is a refusal by design.
        var module = $"Acme.Delta.{declared.Name}";
        var fixture = SyntheticEfModules.Build(
            $"Acme.PostMigration.{declared.Name}.Modules",
            new SyntheticModule(module, $"AcmeDelta{declared.Name}", PostgreSql: typeof(object), PostMigration: [declared]));
        var output = Path.Join(root, $"post-migration-{declared.Name}");
        var request = new ScriptRequestBody
        {
            Provider = "PostgreSql",
            Selection = new() { Kind = "modules", Modules = [module] },
            Output = output,
            Host = new() { Name = "Elsa.Tooling.Tests", ProviderAgreement = "not-checked", Environment = "Production" },
            Engine = new() { Package = EfRelationalProviderBinding.ProviderPackageId("PostgreSql"), Version = "9.9.9-test", Source = "host-deps-file" },
            Packages = [Package($"Acme.PostMigration.{declared.Name}.Modules")]
        };

        var run = await RunAsync(request, [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("post-migration-invalid", run.Response.GetProperty("error").GetProperty("code").GetString());
        var detail = Assert.Single(Details(run.Response));
        Assert.Contains($"'{declared.Name}'", detail, StringComparison.Ordinal);
        Assert.Contains(reason, detail, StringComparison.Ordinal);
        Assert.False(Directory.Exists(output));
    }

    /// <summary>
    /// The same refusal reaches the commands that open a database, not just <c>script</c>: missing one would
    /// let <c>apply --modules X</c> start refusing while <c>script</c> worked, or the reverse.
    /// </summary>
    [Theory]
    [InlineData("plan")]
    [InlineData("apply")]
    [InlineData("validate")]
    [InlineData("post-migrate")]
    public async Task Every_command_refuses_a_declared_post_migration_action_this_build_cannot_use(string command)
    {
        var module = $"Acme.Epsilon.{command}";
        var fixture = SyntheticEfModules.Build(
            $"Acme.PostMigration.{command}.Modules",
            new SyntheticModule(module, $"AcmeEpsilon{command.Replace("-", "")}", PostgreSql: typeof(object), PostMigration: [typeof(Uri)]));

        var run = await RunAsync(
            new ApplyRequestBody
            {
                Command = command,
                Provider = "PostgreSql",
                Selection = new() { Kind = "modules", Modules = [module] },
                Connection = command is "plan" ? null : "Host=localhost;Database=unused"
            },
            [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("post-migration-invalid", run.Response.GetProperty("error").GetProperty("code").GetString());
    }

    private sealed class NoParameterlessConstructor(string unused) : IEfPostMigrationAction
    {
        public string Id => unused;
        public string Kind => "test";
        public string RequiredWhen => "never";
        public string Audit => "none";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class BlankIdentifiers : IEfPostMigrationAction
    {
        public string Id => "";
        public string Kind => "";
        public string RequiredWhen => " ";
        public string Audit => "";
        public Task<bool> AuditAsync(DbContext context, CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task RunAsync(DbContext context, CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    /// <summary>
    /// A third-party <c>[EfModule]</c> name is never slugged (spec 171 User Story 6): a name containing a
    /// directory separator would otherwise reach <c>File.WriteAllBytes</c> as a rooted-looking path segment
    /// and fail deep inside the write, surfacing as an opaque <c>internal-error</c> instead of a named
    /// refusal.
    /// </summary>
    [Fact]
    public async Task Script_refuses_a_module_name_that_produces_a_non_bare_file_name_and_writes_nothing()
    {
        // Loaded via AssemblyLoadContext.Default.LoadFromStream rather than SyntheticEfModules.Build's
        // Assembly.Load(byte[]): the latter is not resolvable by simple name afterwards, and
        // EfRelationalProviderBinding.Use configures the migrations assembly by name, which
        // SecretsPostgreSqlDbContext's IMigrationsAssembly service resolves eagerly on first use.
        var image = SyntheticEfModules.BuildImage(
            "Acme.Traversal.Modules",
            new SyntheticModule("Acme/Evil", "AcmeEvil", PostgreSql: typeof(SecretsPostgreSqlDbContext)));
        Assembly fixture;
        using (var imageStream = new MemoryStream(image))
        {
            fixture = AssemblyLoadContext.Default.LoadFromStream(imageStream);
        }
        var output = Path.Join(root, "traversal");
        var request = new ScriptRequestBody
        {
            Provider = "PostgreSql",
            Selection = new() { Kind = "modules", Modules = ["Acme/Evil"] },
            Output = output,
            Host = new() { Name = "Elsa.Tooling.Tests", ProviderAgreement = "not-checked", Environment = "Production" },
            Engine = new() { Package = EfRelationalProviderBinding.ProviderPackageId("PostgreSql"), Version = "9.9.9-test", Source = "host-deps-file" },
            Packages = [Package("Acme.Traversal.Modules")]
        };

        var run = await RunAsync(request, [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("module-file-name-invalid", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.False(Directory.Exists(output));
    }

    [Fact]
    public async Task A_third_party_module_is_listed_exactly_like_a_first_party_one()
    {
        var fixture = SyntheticEfModules.Build("Acme.Widgets.Modules", new SyntheticModule("Acme.Widgets", "AcmeWidgets", PostgreSql: typeof(object)));

        var run = await RunAsync(new { Version = 1, Command = "list" }, [fixture]);

        var module = Assert.Single(run.Response.GetProperty("list").GetProperty("modules").EnumerateArray());
        Assert.Equal("Acme.Widgets", module.GetProperty("module").GetString());
        Assert.Equal("__EFMigrationsHistory_AcmeWidgets", module.GetProperty("historyTable").GetString());
        Assert.Equal(["PostgreSql"], module.GetProperty("providers").EnumerateArray().Select(provider => provider.GetString()));
    }

    [Fact]
    public async Task A_module_that_does_not_support_the_requested_provider_is_refused_by_name()
    {
        var fixture = SyntheticEfModules.Build("Acme.SqlServerless.Modules", new SyntheticModule("Acme.Epsilon", "AcmeEpsilon", PostgreSql: typeof(object)));

        var run = await RunAsync(new
        {
            Version = 1,
            Command = "plan",
            Provider = "SqlServer",
            Selection = new { Kind = "modules", Modules = new[] { "Acme.Epsilon" } }
        }, [fixture]);

        AssertExit(EfToolingExitCode.ResolutionFailure, run);
        Assert.Equal("provider-unsupported-for-module", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["'Acme.Epsilon' declares no SqlServer context."], Details(run.Response));
    }

    /// <summary>
    /// A cancelled run is not a refused one. Reporting it as an exit-3 response would tell an operator the
    /// tool rejected their request when it simply never finished it.
    /// </summary>
    [Fact]
    public async Task Cancellation_propagates_instead_of_being_reported_as_a_refusal()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();
        using var request = new MemoryStream(Encoding.UTF8.GetBytes(Serialize(new { Version = 1, Command = "list" })));
        using var response = new MemoryStream();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => EfToolingHost.RunAsync(request, response, ModuleContextCatalog.Modules, cancellation.Token));
        Assert.Empty(response.ToArray());
    }

    /// <summary>
    /// The two-argument overload is the exact signature FR-003 freezes and the one #1874's worker invokes
    /// reflectively; every other test in this file goes through the four-argument overload with an explicit
    /// assembly list instead. This exercises the real module-discovery path — <c>AssemblyLoadContext.All</c>
    /// over every assembly already loaded into this process — end to end over real streams. Scoped with a
    /// <c>selection</c> to just the modules under test, rather than a bare <c>list</c>: other tests in this
    /// process load synthetic assemblies with a deliberate dependency cycle or a dangling dependency into
    /// the default load context, which never unloads, and an unscoped <c>list</c> validates every discovered
    /// module's graph regardless of selection.
    /// </summary>
    [Fact]
    public async Task RunAsync_two_arg_discovers_first_party_modules_and_returns_the_response_exit_code()
    {
        using var request = new MemoryStream(Encoding.UTF8.GetBytes(Serialize(new
        {
            Version = 1,
            Command = "list",
            Selection = new { Kind = "modules", Modules = Selection }
        })));
        using var response = new MemoryStream();

        var exitCode = await EfToolingHost.RunAsync(request, response);

        AssertExit(EfToolingExitCode.Success, exitCode, response.ToArray());
        using var document = JsonDocument.Parse(response.ToArray());
        Assert.Equal(EfToolingExitCode.Success, document.RootElement.GetProperty("exitCode").GetInt32());
        var modules = document.RootElement.GetProperty("list").GetProperty("modules").EnumerateArray()
            .Select(module => module.GetProperty("module").GetString())
            .ToArray();
        Assert.Equal(Selection.Order(StringComparer.Ordinal), modules);
    }

    /// <summary>
    /// The exact reflective call FR-003 promises the worker: locate the method by name and its two
    /// <see cref="Stream"/> parameters, invoke it, and await the returned value as a plain <see cref="Task"/>.
    /// </summary>
    [Fact]
    public async Task RunAsync_two_arg_works_through_the_reflective_call_the_worker_makes()
    {
        using var request = new MemoryStream(Encoding.UTF8.GetBytes(Serialize(new
        {
            Version = 1,
            Command = "list",
            Selection = new { Kind = "modules", Modules = Selection }
        })));
        using var response = new MemoryStream();

        var method = typeof(EfToolingHost).GetMethod(nameof(EfToolingHost.RunAsync), [typeof(Stream), typeof(Stream)]);
        Assert.NotNull(method);

        var invoked = method!.Invoke(null, [request, response]);
        var task = Assert.IsAssignableFrom<Task>(invoked);
        await task;

        var exitCode = (int)task.GetType().GetProperty("Result")!.GetValue(task)!;
        AssertExit(EfToolingExitCode.Success, exitCode, response.ToArray());
        Assert.NotEmpty(response.ToArray());
    }

    /// <summary>
    /// A Nuplane <c>HostIntegrated</c> package graph loads into an <see cref="AssemblyLoadContext"/> of its
    /// own, not the default one, which is exactly why <c>LoadedAssemblies()</c> walks
    /// <see cref="AssemblyLoadContext.All"/> instead of the default context alone. Proven here with a
    /// synthetic module assembly loaded into a separate collectible context, scoped by selection for the
    /// same reason as above.
    /// </summary>
    [Fact]
    public async Task RunAsync_two_arg_discovers_a_module_loaded_into_a_non_default_assembly_load_context()
    {
        var context = new AssemblyLoadContext(nameof(RunAsync_two_arg_discovers_a_module_loaded_into_a_non_default_assembly_load_context), isCollectible: true);
        try
        {
            var image = SyntheticEfModules.BuildImage("Acme.Isolated.Modules", new SyntheticModule("Acme.Zeta", "AcmeZeta", PostgreSql: typeof(object)));
            using (var imageStream = new MemoryStream(image))
                context.LoadFromStream(imageStream);

            using var request = new MemoryStream(Encoding.UTF8.GetBytes(Serialize(new
            {
                Version = 1,
                Command = "list",
                Selection = new { Kind = "modules", Modules = new[] { "Acme.Zeta" } }
            })));
            using var response = new MemoryStream();

            var exitCode = await EfToolingHost.RunAsync(request, response);

            AssertExit(EfToolingExitCode.Success, exitCode, response.ToArray());
            using var document = JsonDocument.Parse(response.ToArray());
            var modules = document.RootElement.GetProperty("list").GetProperty("modules").EnumerateArray()
                .Select(module => module.GetProperty("module").GetString())
                .ToArray();
            Assert.Equal(new string?[] { "Acme.Zeta" }, modules);
        }
        finally
        {
            context.Unload();
        }
    }

    private async Task<string> ScriptAsync(string provider, string[] modules, string name)
    {
        var output = Path.Join(root, provider, name);
        var run = await RunAsync(ScriptRequest(provider, modules, output));

        AssertExit(EfToolingExitCode.Success, run);
        Assert.Equal("migration-plan.json", run.Response.GetProperty("script").GetProperty("manifest").GetString());
        return output;
    }

    private static ScriptRequestBody ScriptRequest(string provider, string[] modules, string output) => new()
    {
        Provider = provider,
        Selection = new() { Kind = "modules", Modules = modules },
        Output = output,
        Host = new() { Name = "Elsa.Tooling.Tests", ProviderAgreement = "not-checked", Environment = "Production" },
        Engine = new() { Package = EfRelationalProviderBinding.ProviderPackageId(provider), Version = "9.9.9-test", Source = "host-deps-file" },
        Packages = [.. modules.Select(module => Package(EfModuleCatalog.Find(Descriptors, module)!.Assembly.GetName().Name!))]
    };

    private static PackageBody Package(string assembly) => new() { Assembly = assembly, Id = assembly, Version = "9.9.9-test", Source = "host-deps-file" };

    private static string Serialize(object request) => JsonSerializer.Serialize(request, RequestJson);

    private Task<Run> RunAsync(object request, IEnumerable<Assembly>? assemblies = null) => RunAsync(Serialize(request), assemblies);

    private static async Task<Run> RunAsync(string request, IEnumerable<Assembly>? assemblies = null)
    {
        using var input = new MemoryStream(Encoding.UTF8.GetBytes(request));
        using var output = new MemoryStream();
        var exitCode = await EfToolingHost.RunAsync(input, output, assemblies ?? ModuleContextCatalog.Modules);
        using var response = JsonDocument.Parse(output.ToArray());
        return new(exitCode, response.RootElement.Clone());
    }

    private static SortedDictionary<string, byte[]> Artifact(string directory) => new(
        Directory.EnumerateFiles(directory).ToDictionary(path => Path.GetFileName(path), File.ReadAllBytes),
        StringComparer.Ordinal);

    private static void AssertSameBytes(string expected, string actual) => AssertSameBytes(Artifact(expected), Artifact(actual));

    private static void AssertSameBytes(SortedDictionary<string, byte[]> expected, SortedDictionary<string, byte[]> actual)
    {
        Assert.Equal(expected.Keys, actual.Keys);
        foreach (var (name, bytes) in expected)
            Assert.True(bytes.AsSpan().SequenceEqual(actual[name]), $"{name} is not byte-identical.");
    }

    private static string[] Keys(JsonElement element) => [.. element.EnumerateObject().Select(property => property.Name)];

    /// <summary>
    /// The helper only produces output when an assertion fails, so a green suite says nothing about it. These
    /// two cases pin both branches of <see cref="Describe"/>: the refusal branch, which is what #1910 needed and
    /// did not have, and the no-error branch, which is what runs when a test expects a refusal and gets success.
    /// The assertions are deliberately on the presence of the code, message and details rather than on exact
    /// formatting, so a reworded diagnostic does not become a failing test.
    /// </summary>
    [Fact]
    public void AssertExit_reports_the_refusal_and_survives_a_response_without_one()
    {
        using var refusal = JsonDocument.Parse(
            """{"exitCode":4,"error":{"code":"module-apply-failed","message":"'Secrets' could not be applied.","details":["first","second"]}}""");
        var reported = Assert.ThrowsAny<XunitException>(() => AssertExit(EfToolingExitCode.Success, 4, refusal.RootElement)).Message;
        // Asserted with the label attached, not as bare substrings: printing the message under error.code and
        // the code under error.message would satisfy two loose Contains checks and ship a swapped diagnostic.
        Assert.Contains("error.code='module-apply-failed'", reported, StringComparison.Ordinal);
        Assert.Contains("error.message=''Secrets' could not be applied.'", reported, StringComparison.Ordinal);
        Assert.Contains("first", reported, StringComparison.Ordinal);
        Assert.Contains("second", reported, StringComparison.Ordinal);

        // The shape #1910 actually produced: a database failure with no details at all. No DatabaseFailure path
        // in EfToolingHost populates details, so this - not the case above - is what the next occurrence looks
        // like, and it was the one shape this test did not drive.
        using var detailless = JsonDocument.Parse(
            """{"exitCode":4,"error":{"code":"module-apply-failed","message":"'Secrets' could not be applied."}}""");
        var bare = Assert.ThrowsAny<XunitException>(() => AssertExit(EfToolingExitCode.Success, 4, detailless.RootElement)).Message;
        Assert.Contains("error.code='module-apply-failed'", bare, StringComparison.Ordinal);
        Assert.DoesNotContain("details=", bare, StringComparison.Ordinal);

        using var clean = JsonDocument.Parse("""{"exitCode":0,"list":{"modules":[]}}""");
        var missing = Assert.ThrowsAny<XunitException>(() => AssertExit(EfToolingExitCode.Refusal, 0, clean.RootElement)).Message;
        Assert.Contains("no error payload", missing, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Asserts an exit code and, when it differs, reports the refusal the host wrote beside it.
    /// </summary>
    /// <remarks>
    /// The bare code does not diagnose its own failure. <see cref="EfToolingHost"/> flattens every non-refusal
    /// exception raised while a module is applied or validated into <see cref="EfToolingExitCode.DatabaseFailure"/>,
    /// so a failure reported as "expected 0, got 4" says only that something went wrong — which is exactly what
    /// #1910 recorded, and why its cause could not be named afterwards (#1913). The refusal carries the module,
    /// provider, context type and underlying message. Printing it is safe for the paths these tests drive:
    /// the apply, validate and post-migration refusals run their message through
    /// <c>EfToolingRedaction.Redact</c>, which <see cref="A_connection_string_never_appears_in_a_database_failure"/>
    /// pins with a sentinel. Two limits on that guarantee, so nobody reads it as unconditional: the test pins
    /// <c>error.message</c> only, not the <c>details</c> this helper also prints (no database-failure path
    /// populates details, and the one refusal that does redacts first); and <c>EfToolingHost</c>'s
    /// internal-error handler builds its message straight from the exception without redacting. A future test
    /// that drives an unexpected exception with a credentialed connection string would print it.
    /// </remarks>
    private static void AssertExit(int expected, Run run) => AssertExit(expected, run.ExitCode, run.Response);

    private static void AssertExit(int expected, int actual, JsonElement response)
    {
        if (actual != expected)
            Assert.Fail($"Expected exit {expected}, got {actual}. {Describe(response)}");
    }

    /// <summary>The raw-stream overload; the response is parsed only when the assertion has already failed.</summary>
    private static void AssertExit(int expected, int actual, byte[] response)
    {
        if (actual == expected)
            return;
        using var document = JsonDocument.Parse(response);
        AssertExit(expected, actual, document.RootElement);
    }

    private static string Describe(JsonElement response)
    {
        if (!response.TryGetProperty("error", out var error))
            return "The response carried no error payload.";
        var code = error.TryGetProperty("code", out var value) ? value.GetString() : null;
        var message = error.TryGetProperty("message", out var text) ? text.GetString() : null;
        var details = error.TryGetProperty("details", out var list)
            ? list.EnumerateArray().Select(detail => detail.GetString()).Where(detail => !string.IsNullOrWhiteSpace(detail)).ToArray()
            : [];
        var described = $"error.code='{code}' error.message='{message}'";
        return details.Length == 0 ? described : $"{described} details=[{string.Join("; ", details)}]";
    }

    private static string[] Details(JsonElement response) =>
        [.. response.GetProperty("error").GetProperty("details").EnumerateArray().Select(detail => detail.GetString()!)];

    private sealed record Run(int ExitCode, JsonElement Response);

    private sealed record ScriptRequestBody
    {
        public int Version { get; init; } = 1;
        public string Command { get; init; } = "script";
        public string? Provider { get; init; }
        public SelectionBody? Selection { get; init; }
        public string? Output { get; init; }
        public HostBody? Host { get; init; }
        public EngineBody? Engine { get; init; }
        public IReadOnlyList<PackageBody>? Packages { get; init; }
    }

    private sealed record SelectionBody
    {
        public string? Kind { get; init; }
        public IReadOnlyList<string>? Modules { get; init; }
    }

    private sealed record HostBody
    {
        public string? Name { get; init; }
        public string? ProviderAgreement { get; init; }
        public string? Shell { get; init; }
        public string? Environment { get; init; }
    }

    private sealed record EngineBody
    {
        public string? Package { get; init; }
        public string? Version { get; init; }
        public string? Source { get; init; }
    }

    private sealed record PackageBody
    {
        public string? Assembly { get; init; }
        public string? Id { get; init; }
        public string? Version { get; init; }
        public string? Source { get; init; }
    }

    private sealed record ApplyRequestBody
    {
        public int Version { get; init; } = 1;
        public string Command { get; init; } = "apply";
        public string? Provider { get; init; }
        public SelectionBody? Selection { get; init; }
        public string? Schema { get; init; }
        public string? Connection { get; init; }
    }
}
