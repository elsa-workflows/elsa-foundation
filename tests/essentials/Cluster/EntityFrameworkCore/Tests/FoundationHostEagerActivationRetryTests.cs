using System.Net;
using System.Text.Json.Nodes;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2202 on the real thing: the built <c>Elsa.Foundation.Host</c> as a child process, started while the database its shell's
/// module needs is not reachable. Its first activation fails, and the host used to leave it there: live, not ready, and idle, for
/// a readiness-gated load balancer never sends the request that would activate the shell. It now says why on
/// <c>/health/ready</c>, keeps trying, and is ready once the database is reachable, in the same process.
/// </summary>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostEagerActivationRetryTests(FoundationHostFeed feed) : IAsyncLifetime
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    // The database lives in a directory that does not exist yet, which SQLite cannot open a file in.
    private readonly string _directory = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-retry-{Guid.NewGuid():N}");
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={Path.Join(_directory, "orders.db")};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_host is not null)
            await _host.DisposeAsync();
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }

    [Fact]
    public async Task A_host_started_before_its_database_is_reachable_becomes_ready_without_a_restart_once_it_is()
    {
        var settings = Settings(feed);
        settings["Elsa:Boot:EagerShellActivation:Retry:InitialDelay"] = "00:00:00.2";
        settings["Elsa:Boot:EagerShellActivation:Retry:MaxDelay"] = "00:00:01";
        _host = await FoundationHostProcess.StartAsync(
            Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), feed.Directory, settings, awaitShells: false);

        // The failure is reported, with its type and not its message, and the probe says the shell is retrying.
        JsonNode? reason = null;
        await WaitUntilAsync(async () => (reason = await ReasonAsync())?["code"]?.GetValue<string>() == "activation-failed" && reason["attempts"]!.GetValue<int>() >= 2);
        Assert.NotNull(reason!["failureType"]?.GetValue<string>());
        Assert.NotNull(reason["nextAttemptAt"]);
        Assert.Contains("Eager activation of shell 'default' failed (attempt 1)", _host.Output, StringComparison.Ordinal);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await _host.GetAsync("/health/ready")).Status);

        Directory.CreateDirectory(_directory);
        await SeedAsync(ConnectionString);

        await WaitUntilAsync(async () => (await _host.GetAsync("/health/ready")).Status == HttpStatusCode.OK);
        await WaitUntilAsync(async () => (await OrdersAsync(_host)).Status == HttpStatusCode.OK);
        Assert.True(_host.IsRunning, "The host must become ready in the process it started, not be restarted.");
    }

    /// <summary>The first not-active shell's reason in the readiness probe's answer, or <see langword="null"/> when there is none.</summary>
    private async Task<JsonNode?> ReasonAsync()
    {
        var (_, body) = await _host!.GetAsync("/health/ready");
        return JsonNode.Parse(body)!["shells"]!.AsArray().FirstOrDefault()?["reason"];
    }

    private Task WaitUntilAsync(Func<Task<bool>> condition) =>
        Polling.UntilAsync(condition, Patience, TimeSpan.FromMilliseconds(100), () => $"Host output:{Environment.NewLine}{_host!.Output}");
}
