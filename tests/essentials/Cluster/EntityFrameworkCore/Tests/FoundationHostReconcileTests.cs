using System.Net;
using System.Text.Json.Nodes;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2159: <c>POST /_module-management/reconcile</c> on the built <c>Elsa.Foundation.Host</c>, a child process as an operator runs
/// it, answers with the outcome of the reconcile it ran, whether the feed is empty or a package has just been added to it, and
/// refuses a request that carries no valid key.
/// </summary>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostReconcileTests(FoundationHostFeed feed) : IAsyncLifetime
{
    private const string Reconcile = "/_module-management/reconcile";
    private const string ModuleManagementKey = "foundation-host-reconcile-tests";

    /// <summary>A reconcile of a feed of one package takes seconds; a request that outlasts this has hung, as the sibling suites bound theirs.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-reconcile-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    public Task DisposeAsync() => StopHostAndDeleteDatabaseAsync(_host, _file);

    [Fact]
    public async Task Reconciling_an_empty_feed_answers_with_an_outcome_that_changed_nothing()
    {
        await StartAsync();

        var (status, body) = await _host!.PostModuleManagementAsync(Reconcile, ModuleManagementKey, Patience);

        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal(("Completed", false, false), (outcome["outcomeCode"]!.GetValue<string>(), outcome["runResult"]!["skipped"]!.GetValue<bool>(), outcome["runResult"]!["isDegraded"]!.GetValue<bool>()));
        Assert.Empty(outcome["runResult"]!["changeSet"]!["added"]!.AsArray());
    }

    [Fact]
    public async Task Reconciling_a_feed_that_gained_a_package_answers_with_its_outcome_and_the_package_serves()
    {
        await SeedAsync(ConnectionString);
        await StartAsync();
        File.Copy(feed.FixturePackage, Path.Join(_host!.PackagesDirectory, Path.GetFileName(feed.FixturePackage)));

        var (status, body) = await _host.PostModuleManagementAsync(Reconcile, ModuleManagementKey, Patience);

        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal("Completed", outcome["outcomeCode"]!.GetValue<string>());
        var added = outcome["runResult"]!["changeSet"]!["added"]!.AsArray().Select(package => package!["id"]!.GetValue<string>());
        Assert.Contains(FoundationHostFeed.FixturePackageId, added);
        // The shells reload from the cycle's own completion, which is the host's step and not the request's, so wait for the package.
        await Polling.UntilAsync(async () => (await OrdersAsync(_host)).Status == HttpStatusCode.OK, Patience, TimeSpan.FromMilliseconds(200), () => $"The package never served. Host output:{Environment.NewLine}{_host.Output}");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("not-the-key")]
    public async Task A_request_without_the_module_management_key_is_refused_and_reconciles_nothing(string? key)
    {
        await StartAsync();

        var (status, _) = await _host!.PostModuleManagementAsync(Reconcile, key, Patience);

        Assert.Equal(HttpStatusCode.Unauthorized, status);
    }

    private async Task StartAsync()
    {
        var settings = Settings(feed);
        EnableModuleManagement(settings, ModuleManagementKey);
        // The folder's watcher would reconcile a package dropped in on its own, and the poll would run a cycle of its own, either
        // racing the request under test and answering it with a skipped outcome.
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
        settings["Nuplane:Setup:PollInterval"] = "1.00:00:00";
        _host = await FoundationHostProcess.StartAsync(Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), [], settings);
    }
}
