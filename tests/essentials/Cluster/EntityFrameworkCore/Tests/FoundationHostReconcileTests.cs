using System.Net;
using System.Text.Json.Nodes;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2159: <c>POST /_module-management/reconcile</c> on the built <c>Elsa.Foundation.Host</c>, a child process as an operator runs
/// it, answers with the outcome of the reconcile it ran, whether the feed is empty or a package has just been added to it.
/// </summary>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostReconcileTests(FoundationHostFeed feed) : IAsyncLifetime
{
    private const string ModuleManagementKeyHeader = "X-Elsa-Module-Management-Key";
    private const string ModuleManagementKey = "foundation-host-reconcile-tests";

    /// <summary>Nuplane's <c>ManualReconcileOutcomeCode.Completed</c>, which the response carries as its number.</summary>
    private const int CompletedOutcome = 0;

    /// <summary>A reconcile of a feed of one package takes seconds; a request that outlasts this has hung.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-reconcile-{Guid.NewGuid():N}.db");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

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
        }
    }

    [Fact]
    public async Task Reconciling_an_empty_feed_answers_with_an_outcome_that_changed_nothing()
    {
        await StartAsync();

        var (status, body) = await ReconcileAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal((CompletedOutcome, false, false), (outcome["outcomeCode"]!.GetValue<int>(), outcome["runResult"]!["skipped"]!.GetValue<bool>(), outcome["runResult"]!["isDegraded"]!.GetValue<bool>()));
        Assert.Empty(outcome["runResult"]!["changeSet"]!["added"]!.AsArray());
    }

    [Fact]
    public async Task Reconciling_a_feed_that_gained_a_package_answers_with_its_outcome_and_the_package_serves()
    {
        await SeedAsync(ConnectionString);
        await StartAsync();
        File.Copy(feed.FixturePackage, Path.Join(_host!.PackagesDirectory, Path.GetFileName(feed.FixturePackage)));

        var (status, body) = await ReconcileAsync();

        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal(CompletedOutcome, outcome["outcomeCode"]!.GetValue<int>());
        var added = outcome["runResult"]!["changeSet"]!["added"]!.AsArray().Select(package => package!["id"]!.GetValue<string>());
        Assert.Contains(FoundationHostFeed.FixturePackageId, added);
        Assert.Equal(HttpStatusCode.OK, (await OrdersAsync(_host)).Status);
    }

    /// <summary>The request, bounded: a host that never answers fails the test with what it logged instead of hanging it.</summary>
    private async Task<(HttpStatusCode Status, string Body)> ReconcileAsync()
    {
        try
        {
            return await _host!.PostAsync("/_module-management/reconcile", new Dictionary<string, string> { [ModuleManagementKeyHeader] = ModuleManagementKey })
                .WaitAsync(Patience);
        }
        catch (TimeoutException exception)
        {
            throw new InvalidOperationException($"The reconcile request was not answered within {Patience}. Host output:{Environment.NewLine}{_host!.Output}", exception);
        }
    }

    private async Task StartAsync()
    {
        var settings = Settings(feed);
        settings["Elsa:ModuleManagement:Enabled"] = "true";
        settings["Elsa:ModuleManagement:ApiKey"] = ModuleManagementKey;
        // The folder's watcher would reconcile a package dropped in on its own, racing the request under test.
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
        _host = await FoundationHostProcess.StartAsync(Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), [], settings);
    }
}
