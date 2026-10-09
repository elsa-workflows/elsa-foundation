using System.Globalization;
using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Xunit.Abstractions;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>
/// #2159: <c>POST /_module-management/reconcile</c> on the built <c>Elsa.Foundation.Host</c>, a child process as an operator runs
/// it, answers with the outcome of the reconcile it ran, whether the feed is empty or a package has just been added to it, and
/// refuses a request that carries no valid key.
/// </summary>
[Collection(FoundationHostCollection.Name)]
public sealed class FoundationHostReconcileTests(FoundationHostFeed feed, ITestOutputHelper output) : IAsyncLifetime
{
    private const string Reconcile = "/_module-management/reconcile";
    private const string ModuleManagementKey = "foundation-host-reconcile-tests";

    /// <summary>A reconcile of a feed of one package takes seconds; a request that outlasts this has hung, as the sibling suites bound theirs.</summary>
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(60);

    private readonly string _file = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-reconcile-{Guid.NewGuid():N}.db");
    private string? _lateDatabaseDirectory;
    private FoundationHostProcess? _host;

    private string ConnectionString => $"Data Source={_file};Pooling=False";

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        Exception? cleanupFailure = null;
        try
        {
            await StopHostAndDeleteDatabaseAsync(_host, _file);
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        try
        {
            if (_lateDatabaseDirectory is { } directory && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null ? exception : new AggregateException(cleanupFailure, exception);
        }

        if (cleanupFailure is not null)
            ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

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

    [Fact]
    public async Task Reconciling_a_feed_that_lost_its_only_package_removes_it_from_the_active_graph_and_shell()
    {
        await SeedAsync(ConnectionString);
        await StartAsync([feed.FixturePackage]);

        await Polling.UntilAsync(async () => (await OrdersAsync(_host!)).Status == HttpStatusCode.OK, Patience,
            TimeSpan.FromMilliseconds(200), () => $"The package never served before removal. Host output:{Environment.NewLine}{_host!.Output}");
        var before = await _host!.ActivePackagesAsync();
        Assert.Equal("2.0.0", before[FoundationHostFeed.FixturePackageId]);

        var mapped = await _host.MappedAssembliesAsync();
        HostProcessAssemblyEvidence.Write(output, "Elsa.Foundation.Host", _host.ProcessId, mapped);
        Assert.Contains(mapped, path =>
            StringComparer.Ordinal.Equals(Path.GetFileName(path), FoundationHostFeed.FixturePackageId + ".dll") &&
            HostProcessAssemblyEvidence.IsWithinInstallRoot(path, _host.PackageInstallRoot));

        var package = FoundationHostProcess.Releases(_host.PackagesDirectory, FoundationHostFeed.FixturePackageId).Single();
        File.Delete(package);
        var (status, body) = await _host.PostModuleManagementAsync(Reconcile, ModuleManagementKey, Patience);

        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal("Completed", outcome["outcomeCode"]!.GetValue<string>());
        var removed = outcome["runResult"]!["changeSet"]!["removed"]!.AsArray()
            .Select(package => package!.GetValue<string>())
            .ToArray();
        Assert.Contains(removed, id => StringComparer.OrdinalIgnoreCase.Equals(id, FoundationHostFeed.FixturePackageId));

        await Polling.UntilAsync(async () =>
        {
            var active = await _host.ActivePackagesAsync();
            var (routeStatus, _) = await OrdersAsync(_host);
            return active.Count == 0 && routeStatus == HttpStatusCode.NotFound;
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"The removed package remained active or served. Host output:{Environment.NewLine}{_host.Output}");

        Assert.Empty(await _host.ActivePackagesAsync());
        Assert.True(_host.IsRunning);
    }

    [Fact]
    public async Task Runner_observes_external_activation_and_does_not_restart_after_later_drain()
    {
        _lateDatabaseDirectory = Path.Join(Path.GetTempPath(), $"elsa-foundation-host-external-settlement-{Guid.NewGuid():N}");
        var connectionString = $"Data Source={Path.Join(_lateDatabaseDirectory, "orders.db")};Pooling=False";
        var maxRetryInterval = TimeSpan.FromSeconds(15);
        var settings = Settings(feed);
        settings["Elsa:Boot:EagerShellActivation:Retry:InitialDelay"] = maxRetryInterval.ToString("c", CultureInfo.InvariantCulture);
        settings["Elsa:Boot:EagerShellActivation:Retry:MaxDelay"] = maxRetryInterval.ToString("c", CultureInfo.InvariantCulture);
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
        settings["Nuplane:Setup:PollInterval"] = "1.00:00:00";

        _host = await FoundationHostProcess.StartAsync(
            Shells(connectionString, EntityFrameworkCoreFeature, OrdersFeature, "FeedModuleFixtureStartupControl"),
            feed.Directory,
            settings,
            awaitShells: false);
        var processId = _host.ProcessId;

        JsonNode? initialReason = null;
        await Polling.UntilAsync(async () =>
        {
            var (_, body) = await _host.GetAsync("/health/ready");
            initialReason = JsonNode.Parse(body)!["shells"]!.AsArray().Single()!["reason"];
            return initialReason?["attempts"]?.GetValue<int>() == 1
                   && initialReason["nextAttemptAt"] is not null;
        }, Patience, TimeSpan.FromMilliseconds(100), () => $"The host did not record its first failed activation. Host output:{Environment.NewLine}{_host.Output}");

        var nextAttemptAt = DateTimeOffset.Parse(
            initialReason!["nextAttemptAt"]!.GetValue<string>(),
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);
        Assert.True(nextAttemptAt - DateTimeOffset.UtcNow > TimeSpan.FromSeconds(8), "The external activation must have time to settle before its captured retry deadline.");

        Directory.CreateDirectory(_lateDatabaseDirectory);
        await SeedAsync(connectionString);

        var (ordersStatus, _) = await OrdersAsync(_host);
        Assert.Equal(HttpStatusCode.OK, ordersStatus);
        await Polling.UntilAsync(async () =>
        {
            var (status, body) = await _host.GetAsync("/health/ready");
            var health = JsonNode.Parse(body)!;
            return status == HttpStatusCode.OK
                   && health["shells"]!.AsArray().Single()!["active"]!.GetValue<bool>()
                   && health["shells"]!.AsArray().Single()!["reason"] is null;
        }, Patience, TimeSpan.FromMilliseconds(100), () => $"The external request did not settle the shell. Host output:{Environment.NewLine}{_host.Output}");

        Assert.True(DateTimeOffset.UtcNow < nextAttemptAt, "External activation must precede the retry deadline.");
        var originalFailureLog = "Eager activation of shell 'default' failed";
        var ownSuccessLog = "Eagerly activated shell 'default' at boot.";
        var failureCountAfterExternalActivation = CountOccurrences(_host.Output, originalFailureLog);
        var successCountAfterExternalActivation = CountOccurrences(_host.Output, ownSuccessLog);
        Assert.Equal(1, failureCountAfterExternalActivation);
        Assert.Equal(0, successCountAfterExternalActivation);

        // Keep the externally activated shell serving through the captured retry deadline. This is the
        // observable process boundary for external settlement; it does not inspect the runner's snapshot.
        var firstObservationDeadline = nextAttemptAt + TimeSpan.FromSeconds(2);
        var firstObservationTimeout = firstObservationDeadline - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        Assert.True(firstObservationTimeout > TimeSpan.Zero, "The first bounded observation must include the captured retry deadline.");
        var retryObservedWhileServing = false;
        await Polling.UntilAsync(async () =>
        {
            var (status, body) = await _host.GetAsync("/health/ready");
            var health = JsonNode.Parse(body)!;
            var shell = health["shells"]!.AsArray().Single()!;
            retryObservedWhileServing = status != HttpStatusCode.OK
                                        || shell["state"]?.GetValue<string>() != "Active"
                                        || !shell["active"]!.GetValue<bool>()
                                        || CountOccurrences(_host.Output, originalFailureLog) != failureCountAfterExternalActivation
                                        || CountOccurrences(_host.Output, ownSuccessLog) != successCountAfterExternalActivation;
            return retryObservedWhileServing || DateTimeOffset.UtcNow >= firstObservationDeadline;
        }, firstObservationTimeout, TimeSpan.FromMilliseconds(100), () => $"The first retry observation window did not reach its captured deadline. Host output:{Environment.NewLine}{_host.Output}");

        Assert.False(retryObservedWhileServing, $"The eager runner changed state or logged another attempt after external activation. Host output:{Environment.NewLine}{_host.Output}");
        Assert.True(DateTimeOffset.UtcNow >= firstObservationDeadline, "The serving shell must remain stable through the captured retry deadline and margin.");

        var operationId = Guid.NewGuid().ToString("N");
        var (drainStatus, _) = await _host.PostModuleManagementAsync(
            $"/feed-module-fixture/startup-control/drain/{operationId}", key: null, timeout: Patience);
        Assert.Equal(HttpStatusCode.Accepted, drainStatus);

        var completedMarker = $"FEED_MODULE_STARTUP_CONTROL|{operationId}|completed|Disposed";
        var failedMarker = $"FEED_MODULE_STARTUP_CONTROL|{operationId}|failed|";
        await Polling.UntilAsync(async () =>
        {
            _ = await _host.GetAsync("/health/ready");
            return _host.Output.Contains(completedMarker, StringComparison.Ordinal)
                   || _host.Output.Contains(failedMarker, StringComparison.Ordinal);
        }, Patience, TimeSpan.FromMilliseconds(100), () => $"The fixture drain did not complete with a live, not-ready shell. Host output:{Environment.NewLine}{_host.Output}");

        Assert.Contains(completedMarker, _host.Output, StringComparison.Ordinal);

        // Drain clears the disposed generation from the registry. Keep the child alive for one full
        // maximum retry interval afterward to prove later deactivation does not restart recovery.
        var secondObservationDeadline = DateTimeOffset.UtcNow + maxRetryInterval + TimeSpan.FromSeconds(2);
        var secondObservationTimeout = secondObservationDeadline - DateTimeOffset.UtcNow + TimeSpan.FromSeconds(10);
        Assert.True(secondObservationTimeout > TimeSpan.Zero, "The second bounded observation must include a full maximum retry interval and margin.");
        var recoveryRestartedAfterDrain = false;
        await Polling.UntilAsync(async () =>
        {
            var (status, body) = await _host.GetAsync("/health/ready");
            var health = JsonNode.Parse(body)!;
            var shell = health["shells"]!.AsArray().Single()!;
            recoveryRestartedAfterDrain = status != HttpStatusCode.ServiceUnavailable
                                          || health["status"]?.GetValue<string>() != "not-ready"
                                          || shell["state"]?.GetValue<string>() != "inactive"
                                          || shell["generation"] is not null
                                          || shell["active"]!.GetValue<bool>()
                                          || shell["reason"]?["code"]?.GetValue<string>() != "not-activated"
                                          || CountOccurrences(_host.Output, originalFailureLog) != failureCountAfterExternalActivation
                                          || CountOccurrences(_host.Output, ownSuccessLog) != successCountAfterExternalActivation;
            return recoveryRestartedAfterDrain || DateTimeOffset.UtcNow >= secondObservationDeadline;
        }, secondObservationTimeout, TimeSpan.FromMilliseconds(100), () => $"The post-drain retry observation window did not reach its deadline. Host output:{Environment.NewLine}{_host.Output}");

        var (finalStatus, finalBody) = await _host.GetAsync("/health/ready");
        var finalHealth = JsonNode.Parse(finalBody)!;
        var finalShell = finalHealth["shells"]!.AsArray().Single()!;
        Assert.False(recoveryRestartedAfterDrain, $"The eager runner restarted recovery after the later drain. Host output:{Environment.NewLine}{_host.Output}");
        Assert.True(DateTimeOffset.UtcNow >= secondObservationDeadline, "The inactive shell must remain stable through a full maximum retry interval and margin.");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, finalStatus);
        Assert.Equal("not-ready", finalHealth["status"]!.GetValue<string>());
        Assert.Equal("inactive", finalShell["state"]!.GetValue<string>());
        Assert.Null(finalShell["generation"]);
        Assert.False(finalShell["active"]!.GetValue<bool>());
        Assert.Equal("not-activated", finalShell["reason"]!["code"]!.GetValue<string>());
        Assert.Equal(failureCountAfterExternalActivation, CountOccurrences(_host.Output, originalFailureLog));
        Assert.Equal(successCountAfterExternalActivation, CountOccurrences(_host.Output, ownSuccessLog));
        Assert.Equal(processId, _host.ProcessId);
        Assert.True(_host.IsRunning);
        Assert.Equal(HttpStatusCode.OK, (await _host.GetAsync("/health/live")).Status);
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

    private Task StartAsync() => StartAsync([]);

    private async Task StartAsync(IEnumerable<string> packageFiles)
    {
        var settings = Settings(feed);
        EnableModuleManagement(settings, ModuleManagementKey);
        // The folder's watcher would reconcile a package dropped in on its own, and the poll would run a cycle of its own, either
        // racing the request under test and answering it with a skipped outcome.
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
        settings["Nuplane:Setup:PollInterval"] = "1.00:00:00";
        _host = await FoundationHostProcess.StartAsync(Shells(ConnectionString, EntityFrameworkCoreFeature, OrdersFeature), packageFiles, settings);
    }

    private static int CountOccurrences(string value, string text) =>
        value.Split(text, StringSplitOptions.None).Length - 1;
}
