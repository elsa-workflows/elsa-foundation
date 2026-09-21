using Elsa.Persistence.EntityFramework;
using Elsa.Persistence.EntityFramework.Tooling;
using Elsa.Secrets.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Runtime.Loader;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Xunit;

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

    public void Dispose() => Directory.Delete(root, recursive: true);

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
        Assert.Empty(module.GetProperty("postMigration").EnumerateArray());

        // The manifest's own hash is what a DBA and script-check compare, so it names the file it hashed.
        var sha = module.GetProperty("sha256").GetString();
        Assert.Equal(Convert.ToHexStringLower(SHA256.HashData(Artifact(directory)["01-secrets.sql"])), sha);
    }

    [Fact]
    public async Task Script_refuses_sqlite_with_the_documented_message_and_writes_nothing()
    {
        var output = Path.Join(root, "sqlite");
        var run = await RunAsync(ScriptRequest("Sqlite", ["Secrets"], output));

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.Refusal, run.ExitCode);
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

        Assert.Equal(exitCode, run.ExitCode);
        Assert.Equal("error", run.Response.GetProperty("status").GetString());
        Assert.Equal(code, run.Response.GetProperty("error").GetProperty("code").GetString());
    }

    /// <summary>
    /// A later slice adds <c>--connection</c>; a worker that sends it to this build must be told, not
    /// quietly handed an offline answer to a question that asked for a live one. The message must also not
    /// echo the value it refused.
    /// </summary>
    [Fact]
    public async Task An_unknown_request_property_is_refused_without_echoing_its_value()
    {
        var run = await RunAsync("""{"version":1,"command":"list","connection":"Host=db;Password=hunter2"}""");

        var message = run.Response.GetProperty("error").GetProperty("message").GetString()!;
        Assert.Contains("connection", message, StringComparison.Ordinal);
        Assert.DoesNotContain("hunter2", message, StringComparison.Ordinal);
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
        Assert.Equal("SELECT 1;\n", File.ReadAllText(leftover));
    }

    [Fact]
    public async Task Script_run_twice_into_the_same_directory_succeeds()
    {
        var output = await ScriptAsync("PostgreSql", Selection, "rerun");
        var before = Artifact(output);

        var run = await RunAsync(ScriptRequest("PostgreSql", Selection, output));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("dependency-missing", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["'Acme.Gamma' depends on 'Acme.Missing', which is not in the selection."], Details(run.Response));
    }

    /// <summary>
    /// A module that declares a post-migration action would be recorded as <c>postMigration: []</c> by a
    /// build that cannot describe one, telling a DBA there is nothing left to run. Refused instead.
    /// </summary>
    [Fact]
    public async Task Script_refuses_a_module_that_declares_a_post_migration_action_this_build_cannot_describe()
    {
        var fixture = SyntheticEfModules.Build(
            "Acme.PostMigration.Modules",
            new SyntheticModule("Acme.Delta", "AcmeDelta", PostgreSql: typeof(object), PostMigration: [typeof(Uri)]));
        var output = Path.Join(root, "post-migration");
        var request = new ScriptRequestBody
        {
            Provider = "PostgreSql",
            Selection = new() { Kind = "modules", Modules = ["Acme.Delta"] },
            Output = output,
            Host = new() { Name = "Elsa.Tooling.Tests", ProviderAgreement = "not-checked", Environment = "Production" },
            Engine = new() { Package = EfRelationalProviderBinding.ProviderPackageId("PostgreSql"), Version = "9.9.9-test", Source = "host-deps-file" },
            Packages = [Package("Acme.PostMigration.Modules")]
        };

        var run = await RunAsync(request, [fixture]);

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
        Assert.Equal("post-migration-unsupported", run.Response.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal(["'Acme.Delta' declares post-migration action 'Uri'."], Details(run.Response));
        Assert.False(Directory.Exists(output));
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
        var fixture = AssemblyLoadContext.Default.LoadFromStream(new MemoryStream(image));
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.ResolutionFailure, run.ExitCode);
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

        Assert.Equal(EfToolingExitCode.Success, exitCode);
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
        Assert.Equal(EfToolingExitCode.Success, exitCode);
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

            Assert.Equal(EfToolingExitCode.Success, exitCode);
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
        var output = Path.Combine(root, provider, name);
        var run = await RunAsync(ScriptRequest(provider, modules, output));

        Assert.Equal(EfToolingExitCode.Success, run.ExitCode);
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
}
