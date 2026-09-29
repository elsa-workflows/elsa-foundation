using System.Net;
using System.Text.Json.Nodes;
using Elsa.Persistence.EntityFramework;
using Microsoft.Data.Sqlite;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// A running <c>Elsa.Foundation.Host</c> under <c>Migrate:Policy=Validate</c> is asked to reload a shell over a database whose
/// migrations are not all applied. CShells keeps the previous generation active when the reload fails, and used to say nothing
/// of it: the endpoint answered 200. It answers 409 now, naming the shell, the EF module, the pending migrations and the
/// command that applies them; the real <c>dotnet elsa persistence apply</c>, run against the host's own directory, clears the
/// refusal, and the same process then reloads.
/// </summary>
/// <remarks>
/// The module arrives from a directory feed, so its <c>Elsa.Persistence.EntityFramework</c> is a private copy in a load
/// context of its own and its refusal a type the host cannot name: the host recognises it by the interface it shares.
/// </remarks>
public sealed class FoundationHostReloadRefusalTests(FoundationHostFeed feed, MigratingModuleFeed migrating)
    : IClassFixture<FoundationHostFeed>, IClassFixture<MigratingModuleFeed>, IAsyncLifetime
{
    private const string ApiKey = "reload-refusal-test-key";
    private const string ConnectionVariable = "ELSA_EF_CONNECTION";
    private const string ShellName = "default";

    // The host's own header; named here rather than referenced, as nothing of the host is loaded into this process.
    private const string ModuleManagementKeyHeader = "X-Elsa-Module-Management-Key";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);
    private static readonly IReadOnlyDictionary<string, string> Credential = new Dictionary<string, string> { [ModuleManagementKeyHeader] = ApiKey };

    /// <summary>
    /// What the fixture module declares, restated: it is built and packed but never referenced, so nothing of it is loaded into
    /// this process. <c>MigratingModule.cs</c> in the fixture is the source of each value.
    /// </summary>
    private static class Fixture
    {
        public const string Name = "MigratingModuleFixture";
        public const string Feature = "MigratingModuleFixture";
        public const string VersionPath = "/migrating-module-fixture/version";
        public const string Table = "MigratingModuleWidgets";
        public const string AddedColumn = "Color";
        public const string AddColor = "20260930000002_AddColor";
    }

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-reload-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    private string Snapshot => _file + ".snapshot";

    public Task InitializeAsync() => Task.CompletedTask;

    /// <summary>Stops the host before its database is deleted, and deletes it even if stopping the host failed.</summary>
    public async Task DisposeAsync()
    {
        try
        {
            if (_host is not null)
                await _host.DisposeAsync();
        }
        finally
        {
            DeleteDatabaseFiles(_file);
            File.Delete(Snapshot);
        }
    }

    /// <summary>
    /// The database is put back to what a backup of the previous release took, under a host that runs the new release: its next
    /// reload finds a migration pending. The reload is refused whatever loaded the module, which is what this proves.
    /// </summary>
    [Fact]
    public async Task A_reload_over_a_database_that_is_behind_is_refused_with_a_409_until_the_persistence_tool_applies_the_migrations()
    {
        await MigrateAsync(generation: 1);
        File.Copy(_file, Snapshot);
        await MigrateAsync(generation: 2);
        _host = await StartAsync(EfMigratePolicy.Validate, generation: 2, deployed: true);
        Assert.Equal((HttpStatusCode.OK, "2.0.0 generation 1"), await Version());

        File.Copy(Snapshot, _file, overwrite: true);
        Assert.DoesNotContain(Fixture.AddedColumn, await ColumnsAsync(), StringComparer.Ordinal);

        // A change to the feed makes the host reconcile and reload its shell on its own, which the pending migration refuses, and
        // the host says so on its log, for whoever reads that instead of a response. The older package is one the host does not
        // switch to, so the reload is all that reaches the shell.
        File.Copy(migrating.Package(1), Path.Join(_host.PackagesDirectory, Path.GetFileName(migrating.Package(1))));
        await WaitUntilAsync(() => _host.Output.Contains($"Reloading shell '{ShellName}' after a Nuplane reconcile was refused", StringComparison.Ordinal));
        Assert.Contains(Fixture.AddColor, _host.Output, StringComparison.Ordinal);

        await AssertRefusedThenAppliedThenReloadedAsync(runningVersion: "2.0.0 generation 1", reloadedVersion: "2.0.0");
    }

    /// <summary>
    /// What an operator does: a newer package version lands in the feed of a host that runs the older one. This needs the
    /// module's own migrations to be found when the older assembly is still loaded, which is not so yet: EF resolves the
    /// migrations assembly it is told by name, and reaches the older one, whose migrations are declared for the older context, so
    /// it finds none pending and the reload is accepted over a schema the new version does not fit. Passes with the module's
    /// binding naming no migrations assembly of its own; enable it with <c>claude/readability-superseded-contexts</c>.
    /// </summary>
    [Fact(Skip = "needs claude/readability-superseded-contexts")]
    public async Task A_newer_version_installed_beside_a_running_one_is_refused_with_a_409_until_the_persistence_tool_applies_its_migrations()
    {
        await MigrateAsync(generation: 1);
        _host = await StartAsync(EfMigratePolicy.Validate, generation: 1, deployed: true);
        Assert.Equal((HttpStatusCode.OK, "1.0.0 generation 1"), await Version());

        // The package lands in the feed, and the host's folder listener reconciles it and reloads the shell onto it, which the
        // pending migration refuses. The host says so on its log, which is what this waits for.
        File.Copy(migrating.Package(2), Path.Join(_host.PackagesDirectory, Path.GetFileName(migrating.Package(2))));
        await WaitUntilAsync(() => _host.Output.Contains($"Reloading shell '{ShellName}' after a Nuplane reconcile was refused", StringComparison.Ordinal));

        await AssertRefusedThenAppliedThenReloadedAsync(runningVersion: "1.0.0 generation 1", reloadedVersion: "2.0.0");
    }

    /// <summary>
    /// A host started over a database that is behind does not activate its shell, and says which module and migrations hold it
    /// back; once the persistence tool has applied them, the same process serves the new version.
    /// </summary>
    [Fact]
    public async Task A_cold_start_over_a_database_that_is_behind_leaves_the_shell_inactive_until_the_persistence_tool_applies_the_migrations()
    {
        await MigrateAsync(generation: 1);
        _host = await StartAsync(EfMigratePolicy.Validate, generation: 2, deployed: true, awaitShells: false);

        await WaitUntilAsync(() => _host.Output.Contains($"EF module '{Fixture.Name}' has pending migrations: {Fixture.AddColor}", StringComparison.Ordinal));
        Assert.NotEqual(HttpStatusCode.OK, (await Version()).Status);
        Assert.NotEqual(HttpStatusCode.OK, (await _host.GetAsync("/health/ready")).Status);

        var (exitCode, output) = await ElsaCliProcess.RunAsync(
            ["persistence", "apply", "--host", _host.ContentRoot, "--provider", "Sqlite", "--modules", Fixture.Name, "--connection-env", ConnectionVariable],
            new Dictionary<string, string> { [ConnectionVariable] = ConnectionString });
        Assert.True(exitCode == 0, $"dotnet elsa persistence apply exited {exitCode}:{Environment.NewLine}{output}");

        var (reloaded, ok) = await _host.PostAsync("/_module-management/reload", Credential);
        var (status, version) = await Version();
        Assert.True(reloaded == HttpStatusCode.OK, $"Reload answered {reloaded}: {ok}{Environment.NewLine}Host output:{Environment.NewLine}{_host.Output}");
        // Each activation that was refused took a generation number, so which one serves is not the first.
        Assert.True(status == HttpStatusCode.OK && version.StartsWith("2.0.0 generation ", StringComparison.Ordinal), $"The shell answered {status}: {version}");
    }

    /// <summary>
    /// The refusal, the tool and the reload of both: a 409 that names the shell, the module, the migration and the command while
    /// the running generation keeps serving; the real tool applying the migration; and the same process reloading onto a
    /// generation that has advanced.
    /// </summary>
    private async Task AssertRefusedThenAppliedThenReloadedAsync(string runningVersion, string reloadedVersion)
    {
        var (refused, problem) = await _host!.PostAsync("/_module-management/reload", Credential);

        Assert.Equal(HttpStatusCode.Conflict, refused);
        var body = JsonNode.Parse(problem)!;
        var detail = body["detail"]!.GetValue<string>();
        var failure = Assert.Single(body["shells"]!.AsArray())!;
        Assert.Equal(
            (ShellName, Fixture.Name, "pending-migrations", "dotnet elsa persistence apply --host <path> --modules MigratingModuleFixture --provider Sqlite --connection-env ELSA_EF_CONNECTION"),
            (failure["shell"]!.GetValue<string>(), failure["module"]!.GetValue<string>(), failure["code"]!.GetValue<string>(), failure["command"]!.GetValue<string>()));
        Assert.Equal([Fixture.AddColor], failure["pendingMigrations"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Contains($"Shell '{ShellName}'", detail, StringComparison.Ordinal);
        Assert.Contains($"EF module '{Fixture.Name}'", detail, StringComparison.Ordinal);
        Assert.Contains(Fixture.AddColor, detail, StringComparison.Ordinal);
        // CShells kept the running generation serving.
        Assert.Equal((HttpStatusCode.OK, runningVersion), await Version());

        // The real tool, against the host's own directory: the package set the running host last reconciled, its
        // Elsa.Persistence.EntityFramework loaded from that package graph, since the host itself carries no EF.
        var (exitCode, output) = await ElsaCliProcess.RunAsync(
            ["persistence", "apply", "--host", _host.ContentRoot, "--provider", "Sqlite", "--modules", Fixture.Name, "--connection-env", ConnectionVariable],
            new Dictionary<string, string> { [ConnectionVariable] = ConnectionString });
        Assert.True(exitCode == 0, $"dotnet elsa persistence apply exited {exitCode}:{Environment.NewLine}{output}");
        Assert.Contains(Fixture.AddedColumn, await ColumnsAsync(), StringComparer.Ordinal);

        var (reloaded, ok) = await _host.PostAsync("/_module-management/reload", Credential);

        Assert.True(reloaded == HttpStatusCode.OK, $"Reload answered {reloaded}: {ok}{Environment.NewLine}Host output:{Environment.NewLine}{_host.Output}");
        var (status, version) = await Version();
        Assert.Equal(HttpStatusCode.OK, status);
        var parts = version.Split(" generation ");
        Assert.Equal(reloadedVersion, parts[0]);
        Assert.True(int.Parse(parts[1]) > 1, $"The shell's generation did not advance: {version}");
    }

    private async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Not met within {Patience}. Host output:{Environment.NewLine}{_host!.Output}");
            await Task.Delay(TimeSpan.FromMilliseconds(100));
        }
    }

    private Task<(HttpStatusCode Status, string Body)> Version() => _host!.GetAsync(Fixture.VersionPath);

    /// <summary>
    /// Brings the database to what a release of <paramref name="generation"/> leaves behind, the way it does: a host of that
    /// generation that migrates on activation, stopped once it has, so the host under test starts on a current schema under
    /// Validate.
    /// </summary>
    private async Task MigrateAsync(int generation)
    {
        await using var host = await StartAsync(EfMigratePolicy.AutoMigrate, generation, deployed: false);
        Assert.NotEmpty(await ColumnsAsync());
    }

    private async Task<FoundationHostProcess> StartAsync(EfMigratePolicy policy, int generation, bool deployed, bool awaitShells = true)
    {
        var packages = Directory.EnumerateFiles(feed.Directory, "Elsa.Persistence.EntityFramework.*.nupkg").Append(migrating.Package(generation));
        return await FoundationHostProcess.StartAsync(
            Shells(ConnectionString),
            packages,
            new Dictionary<string, string>
            {
                // A second feed beside the host's own `packages` one, which resolves what the packages there depend on and holds
                // nothing the host loads on its own account.
                ["Nuplane:Setup:Feeds:1:Name"] = "closure",
                ["Nuplane:Setup:Feeds:1:DirectoryPath"] = feed.ClosureDirectory,
                ["Nuplane:Capabilities:ef-provider"] = "Sqlite",
                [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = policy.ToString(),
                ["Elsa:ModuleManagement:Enabled"] = "true",
                ["Elsa:ModuleManagement:ApiKey"] = ApiKey
            },
            deployed,
            awaitShells);
    }

    private static string Shells(string connectionString) => new JsonObject
    {
        ["CShells"] = new JsonObject
        {
            ["Shells"] = new JsonObject
            {
                [ShellName] = new JsonObject
                {
                    ["Name"] = ShellName,
                    ["Features"] = new JsonObject
                    {
                        [Fixture.Feature] = new JsonObject { ["ConnectionString"] = connectionString }
                    },
                    ["Configuration"] = new JsonObject { ["WebRouting"] = new JsonObject { ["Path"] = "" } }
                }
            }
        }
    }.ToJsonString();

    /// <summary>The columns of the module's table, empty while it does not exist.</summary>
    private async Task<IReadOnlyList<string>> ColumnsAsync()
    {
        await using var connection = new SqliteConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT name FROM pragma_table_info('{Fixture.Table}')";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        return columns;
    }
}
