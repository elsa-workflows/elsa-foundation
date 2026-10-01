using System.Net;
using System.Text.Json.Nodes;
using Elsa.Cluster.Fixtures.MigratingModule;
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
/// The host carries and shares <c>Elsa.Persistence.EntityFramework</c> (#2151), so the module binds the host's copy and the
/// feed offers none: only the module is packed. The host recognises the refusal by the interface it shares, naming no EF type.
/// </remarks>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostReloadRefusalTests(FoundationHostFeed feed, MigratingModuleFeed migrating)
    : IClassFixture<MigratingModuleFeed>, IAsyncLifetime
{
    private const string ApiKey = "reload-refusal-test-key";
    private const string ConnectionVariable = "ELSA_EF_CONNECTION";
    private const string ShellName = "default";

    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-reload-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    private string Snapshot => _file + ".snapshot";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        try
        {
            await StopHostAndDeleteDatabaseAsync(_host, _file);
        }
        finally
        {
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
        Assert.DoesNotContain(MigratingModule.AddedColumn, await ColumnsAsync(), StringComparer.Ordinal);

        // A change to the feed makes the host reconcile and reload its shell on its own, which the pending migration refuses, and
        // the host says so on its log, for whoever reads that instead of a response. The older package is one the host does not
        // switch to, so the reload is all that reaches the shell.
        File.Copy(migrating.Package(1), Path.Join(_host.PackagesDirectory, Path.GetFileName(migrating.Package(1))));
        await HostLogsAsync($"Reloading shell '{ShellName}' after a Nuplane reconcile was refused");
        Assert.Contains(MigratingModule.AddColor, _host.Output, StringComparison.Ordinal);

        await AssertRefusedThenAppliedThenReloadedAsync(runningVersion: "2.0.0 generation 1");
    }

    /// <summary>
    /// The newer release installed into a running host's feed: the host reconciles, loads it beside the release its shell
    /// runs, and reloads onto it, which the migration it adds refuses with a 409 while the release it replaces keeps
    /// serving; the real tool applies the migration, and the same process then reloads onto the new release. Its
    /// migrations are the new release's own: the module hands EF Core its assembly (spec 183, FR-021, amended 2026-09-29).
    /// A name would reach the same release on this host, whose EF Core resolves it through Nuplane's resolution of the
    /// active package set, so the binding is pinned by <c>MigrationsAssemblyAssert</c> rather than here.
    /// </summary>
    [Fact]
    public async Task A_release_installed_on_a_running_host_whose_migration_is_not_applied_is_refused_with_a_409_until_the_persistence_tool_applies_it()
    {
        await MigrateAsync(generation: 1);
        _host = await StartAsync(EfMigratePolicy.Validate, generation: 1, deployed: true);
        Assert.Equal((HttpStatusCode.OK, "1.0.0 generation 1"), await Version());

        File.Copy(migrating.Package(2), Path.Join(_host.PackagesDirectory, Path.GetFileName(migrating.Package(2))));
        await HostLogsAsync($"Reloading shell '{ShellName}' after a Nuplane reconcile was refused");

        await AssertRefusedThenAppliedThenReloadedAsync(runningVersion: "1.0.0 generation 1");
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

        await HostLogsAsync($"EF module '{MigratingModule.Name}' has pending migrations: {MigratingModule.AddColor}");
        Assert.NotEqual(HttpStatusCode.OK, (await Version()).Status);
        var (readiness, probe) = await _host.GetAsync("/health/ready");
        Assert.NotEqual(HttpStatusCode.OK, readiness);
        // The probe says why: the module refused, which an operator resolves, and the host keeps checking back (#2202).
        var reason = JsonNode.Parse(probe)!["shells"]![0]!["reason"]!;
        Assert.Equal(("activation-refused", MigratingModule.Name, "pending-migrations"), (reason["code"]!.GetValue<string>(), reason["refusal"]!["module"]!.GetValue<string>(), reason["refusal"]!["code"]!.GetValue<string>()));
        Assert.Equal([MigratingModule.AddColor], reason["refusal"]!["pendingMigrations"]!.AsArray().Select(id => id!.GetValue<string>()));

        await ApplyAsync();

        var (reloaded, ok) = await ReloadAsync();
        var (status, version) = await Version();
        Assert.True(reloaded == HttpStatusCode.OK, $"Reload answered {reloaded}: {ok}{Environment.NewLine}Host output:{Environment.NewLine}{_host.Output}");
        // Each activation that was refused took a generation number, so which one serves is not the first.
        Assert.True(status == HttpStatusCode.OK && version.StartsWith("2.0.0 generation ", StringComparison.Ordinal), $"The shell answered {status}: {version}");
    }

    /// <summary>
    /// The refusal, the tool and the reload of both: a 409 that names the shell, the module, the migration and the command while
    /// the running generation, <paramref name="runningVersion"/>, keeps serving; the real tool applying the migration; and the
    /// same process reloading onto the newer release, at a generation that has advanced.
    /// </summary>
    private async Task AssertRefusedThenAppliedThenReloadedAsync(string runningVersion)
    {
        var (refused, problem) = await ReloadAsync();

        Assert.Equal(HttpStatusCode.Conflict, refused);
        var body = JsonNode.Parse(problem)!;
        var detail = body["detail"]!.GetValue<string>();
        var failure = Assert.Single(body["shells"]!.AsArray())!;
        Assert.Equal(
            (ShellName, MigratingModule.Name, "pending-migrations"),
            (failure["shell"]!.GetValue<string>(), failure["module"]!.GetValue<string>(), failure["code"]!.GetValue<string>()));
        // Runnable as it stands: --host names the directory of the running host, which holds its build output and the
        // .nuplane state the tool reads. The host may name it through a symlink the test's path does not (macOS's /var).
        var command = failure["command"]!.GetValue<string>();
        var hostDirectory = command.Split('"')[1];
        Assert.Equal($"dotnet elsa persistence apply --host \"{hostDirectory}\" --modules MigratingModuleFixture --provider Sqlite --connection-env ELSA_EF_CONNECTION", command);
        Assert.True(File.Exists(Path.Join(hostDirectory, "Elsa.Foundation.Host.dll")) && Directory.Exists(Path.Join(hostDirectory, ".nuplane")), $"--host {hostDirectory} is not the running host's directory.");
        Assert.Contains(command, detail, StringComparison.Ordinal);
        Assert.Equal([MigratingModule.AddColor], failure["pendingMigrations"]!.AsArray().Select(id => id!.GetValue<string>()));
        Assert.Contains($"Shell '{ShellName}'", detail, StringComparison.Ordinal);
        Assert.Contains($"EF module '{MigratingModule.Name}'", detail, StringComparison.Ordinal);
        Assert.Contains(MigratingModule.AddColor, detail, StringComparison.Ordinal);
        // CShells kept the running generation serving.
        Assert.Equal((HttpStatusCode.OK, runningVersion), await Version());

        // The real tool, against the host's own directory: it finds the module through the host's own copy of
        // Elsa.Persistence.EntityFramework, in that directory's output, and the package set the host last reconciled.
        await ApplyAsync();
        Assert.Contains(MigratingModule.AddedColumn, await ColumnsAsync(), StringComparer.Ordinal);

        var (reloaded, ok) = await ReloadAsync();

        Assert.True(reloaded == HttpStatusCode.OK, $"Reload answered {reloaded}: {ok}{Environment.NewLine}Host output:{Environment.NewLine}{_host!.Output}");
        var (status, version) = await Version();
        Assert.Equal(HttpStatusCode.OK, status);
        var parts = version.Split(" generation ");
        Assert.Equal("2.0.0", parts[0]);
        Assert.True(int.Parse(parts[1]) > 1, $"The shell's generation did not advance: {version}");
    }

    private Task<(HttpStatusCode Status, string Body)> ReloadAsync() => _host!.PostModuleManagementAsync("/_module-management/reload", ApiKey);

    /// <summary>The real tool, against the host's own directory, for the module the host's shell runs.</summary>
    private async Task ApplyAsync()
    {
        var (exitCode, output) = await ElsaCliProcess.RunAsync(
            ["persistence", "apply", "--host", _host!.ContentRoot, "--provider", "Sqlite", "--modules", MigratingModule.Name, "--connection-env", ConnectionVariable],
            new Dictionary<string, string> { [ConnectionVariable] = ConnectionString });
        Assert.True(exitCode == 0, $"dotnet elsa persistence apply exited {exitCode}:{Environment.NewLine}{output}");
    }

    private Task HostLogsAsync(string text) =>
        Polling.UntilAsync(
            () => Task.FromResult(_host!.Output.Contains(text, StringComparison.Ordinal)), Patience, TimeSpan.FromMilliseconds(100), () => $"Host output:{Environment.NewLine}{_host!.Output}");

    private Task<(HttpStatusCode Status, string Body)> Version() => _host!.GetAsync(MigratingModule.VersionPath);

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
        var settings = new Dictionary<string, string>
        {
            // A second feed beside the host's own `packages` one, which resolves what the package there depends on and holds
            // nothing the host loads on its own account: the host carries EF Core and Elsa.Persistence.EntityFramework itself.
            ["Nuplane:Setup:Feeds:1:Name"] = "closure",
            ["Nuplane:Setup:Feeds:1:DirectoryPath"] = feed.ClosureDirectory,
            ["Nuplane:Capabilities:ef-provider"] = "Sqlite",
            [$"{EfMigrateOptions.SectionName}:{nameof(EfMigrateOptions.Policy)}"] = policy.ToString()
        };
        FoundationHostComposition.EnableModuleManagement(settings, ApiKey);
        return await FoundationHostProcess.StartAsync(Shells(ConnectionString), [migrating.Package(generation)], settings, deployed, awaitShells);
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
                        [MigratingModule.Feature] = new JsonObject { ["ConnectionString"] = connectionString }
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
        command.CommandText = $"SELECT name FROM pragma_table_info('{MigratingModule.Table}')";
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            columns.Add(reader.GetString(0));

        return columns;
    }
}
