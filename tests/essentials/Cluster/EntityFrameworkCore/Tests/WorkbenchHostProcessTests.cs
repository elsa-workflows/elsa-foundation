using System.Net;
using System.Text.Json;
using System.Text.Json.Nodes;
using Xunit.Abstractions;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FeedLoadedModuleHost;
using static Elsa.Cluster.EntityFrameworkCore.Tests.FoundationHostComposition;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>Exercises the built Workbench's real Nuplane package and shell lifecycle in an owned child process.</summary>
[Collection(FoundationHostCollection.Name)]
public sealed class WorkbenchHostProcessTests(FoundationHostFeed feed, ITestOutputHelper output) : IAsyncLifetime
{
    private const string ManagementKey = "workbench-host-process-tests";
    private const string Reconcile = "/_elsa/module-management/reconcile";
    private const string ReloadDefault = "/_admin/composition/default-shell/reload";
    private const string ObserveDefault = "/_admin/composition/default-shell/observation";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);

    private readonly string _databaseFile = Path.Join(Path.GetTempPath(), $"elsa-workbench-host-{Guid.NewGuid():N}.db");
    private string? _readabilityRoot;
    private WorkbenchHostProcess? _host;

    private string ConnectionString => $"Data Source={_databaseFile};Pooling=False";

    public Task InitializeAsync() => SeedAsync(ConnectionString);

    public async Task DisposeAsync()
    {
        Exception? cleanupFailure = null;
        try
        {
            if (_host is not null)
                await _host.DisposeAsync();
        }
        catch (Exception exception)
        {
            cleanupFailure = exception;
        }

        try
        {
            DeleteDatabaseFiles(_databaseFile);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null ? exception : new AggregateException(cleanupFailure, exception);
        }

        try
        {
            if (_readabilityRoot is { } directory && Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }
        catch (Exception exception)
        {
            cleanupFailure = cleanupFailure is null ? exception : new AggregateException(cleanupFailure, exception);
        }

        if (cleanupFailure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(cleanupFailure).Throw();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Reconciliation_adds_updates_and_removes_a_real_feed_package_under_the_selected_reload_profile(bool autoReload)
    {
        await StartAsync(autoReload, warmDefaultShell: true);

        var initial = await ObserveDefaultAsync();
        Assert.NotNull(initial.Generation);
        Assert.Equal(HttpStatusCode.NotFound, (await OrdersAsync()).Status);

        CopyPackage(feed.PreviousPackage);
        await ReconcileAsync(FoundationHostFeed.FixturePackageId, "1.0.0");
        if (autoReload)
        {
            await WaitForGenerationAndOrdersAsync(initial.Generation.Value, "1");
        }
        else
        {
            Assert.Equal(initial.Generation, (await ObserveDefaultAsync()).Generation);
            Assert.Equal(HttpStatusCode.NotFound, (await OrdersAsync()).Status);
            await ReloadDefaultAsync(initial.Generation.Value, "1");
        }

        var versionOne = await ObserveDefaultAsync();
        Assert.NotNull(versionOne.Generation);
        Assert.Equal(initial.ProcessInstanceId, versionOne.ProcessInstanceId);
        Assert.Contains(await FixtureAssemblyPathsAsync(), path => path.Contains("1.0.0", StringComparison.OrdinalIgnoreCase));

        _host!.UpgradeInPlace(FoundationHostFeed.FixturePackageId, feed.FixturePackage);
        await ReconcileAsync(FoundationHostFeed.FixturePackageId, "2.0.0");
        if (autoReload)
        {
            await WaitForGenerationAndOrdersAsync(versionOne.Generation.Value, "2");
        }
        else
        {
            Assert.Equal(versionOne.Generation, (await ObserveDefaultAsync()).Generation);
            Assert.Contains("is at 1.", (await OrdersAsync()).Text, StringComparison.Ordinal);
            await ReloadDefaultAsync(versionOne.Generation.Value, "2");
        }

        var versionTwo = await ObserveDefaultAsync();
        Assert.NotNull(versionTwo.Generation);
        Assert.Equal(initial.ProcessInstanceId, versionTwo.ProcessInstanceId);
        Assert.Contains(await FixtureAssemblyPathsAsync(), path => path.Contains("2.0.0", StringComparison.OrdinalIgnoreCase));

        foreach (var package in FoundationHostProcess.Releases(_host.PackagesDirectory, FoundationHostFeed.FixturePackageId).ToArray())
            File.Delete(package);
        await ReconcileAsync(expectedActiveVersion: null);
        if (autoReload)
        {
            await Polling.UntilAsync(async () =>
            {
                var observation = await ObserveDefaultAsync();
                return observation.Generation > versionTwo.Generation && (await OrdersAsync()).Status == HttpStatusCode.NotFound;
            }, Patience, TimeSpan.FromMilliseconds(200), () => $"Workbench did not reload after package removal. Host output:{Environment.NewLine}{_host!.Output}");
        }
        else
        {
            Assert.Equal(versionTwo.Generation, (await ObserveDefaultAsync()).Generation);
            Assert.Contains("is at 2.", (await OrdersAsync()).Text, StringComparison.Ordinal);
            await ReloadDefaultAsync(versionTwo.Generation.Value, expectedVersion: null);
        }

        Assert.Empty(await _host.ActivePackagesAsync());
        var final = await ObserveDefaultAsync();
        Assert.Equal(initial.ProcessInstanceId, final.ProcessInstanceId);
        Assert.True(_host.IsRunning);
        Assert.Equal(HttpStatusCode.NotFound, (await OrdersAsync()).Status);
    }

    [Fact]
    public async Task Startup_reconciliation_is_visible_to_the_first_cold_shell_activation()
    {
        await StartAsync(autoReload: false, warmDefaultShell: false, packageFiles: [feed.FixturePackage], automaticReconciliation: true);

        // The state is persisted before completion observers run. Wait until Nuplane has delivered them all
        // before making the first shell request, so this exercises freshness after a completed cold reconciliation.
        await Polling.UntilAsync(() => Task.FromResult(_host!.Output.Contains("Reconciliation cycle completed", StringComparison.Ordinal)),
            Patience, TimeSpan.FromMilliseconds(200), () => $"Startup reconciliation never completed. Host output:{Environment.NewLine}{_host!.Output}");
        var completion = _host!.Output.Split('\n').Last(line => line.Contains("Reconciliation cycle completed", StringComparison.Ordinal));
        Assert.Contains("FailedCount=0", completion, StringComparison.Ordinal);
        Assert.Contains("IsDegraded=False", completion, StringComparison.OrdinalIgnoreCase);

        await Polling.UntilAsync(async () =>
        {
            var active = await _host!.ActivePackagesAsync();
            return active.TryGetValue(FoundationHostFeed.FixturePackageId, out var version) && version == "2.0.0";
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"Startup reconciliation did not install the fixture package. Host output:{Environment.NewLine}{_host!.Output}");

        var before = await ObserveDefaultAsync();
        Assert.Null(before.Generation);

        await Polling.UntilAsync(async () =>
        {
            var result = await OrdersAsync();
            return result.Status == HttpStatusCode.OK && result.Text.Contains("is at 2.", StringComparison.Ordinal);
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"The first Workbench shell did not use the reconciled package catalog. Host output:{Environment.NewLine}{_host!.Output}");

        var after = await ObserveDefaultAsync();
        Assert.NotNull(after.Generation);
        Assert.Equal(before.ProcessInstanceId, after.ProcessInstanceId);
        Assert.Equal("2.0.0", (await _host!.ActivePackagesAsync())[FoundationHostFeed.FixturePackageId]);
        Assert.Contains(await FixtureAssemblyPathsAsync(), path => path.Contains("2.0.0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Actual_workbench_reports_conservative_readability_while_a_replaced_package_request_drains()
    {
        _readabilityRoot = HostReadabilityScenario.CreateOwnedRoot();
        var membershipDatabase = Path.Join(_readabilityRoot, "membership.db");
        var controlDirectory = Path.Join(_readabilityRoot, "control");
        var locksDirectory = Directory.CreateDirectory(Path.Join(_readabilityRoot, "locks")).FullName;
        var hostId = $"workbench-readability-{Guid.NewGuid():N}";
        await StartAsync(
            autoReload: false,
            warmDefaultShell: true,
            packageFiles: [feed.PreviousPackage],
            configureSettings: settings => HostReadabilityScenario.Configure(settings, hostId, membershipDatabase, controlDirectory, automaticReconciliation: false),
            locksDirectory: locksDirectory,
            includeStartupControl: true);
        var host = _host!;
        var initialShell = await ObserveDefaultAsync();
        Assert.NotNull(initialShell.Generation);
        await ReconcileAsync(FoundationHostFeed.FixturePackageId, "1.0.0");
        await ReloadDefaultAsync(initialShell.Generation.Value, "1.0.0");

        async Task ReloadAsync()
        {
            var (status, _) = await host.PostAsync(ReloadDefault, ManagementKey, Patience);
            Assert.Equal(HttpStatusCode.OK, status);
        }

        var driver = new HostReadabilityDriver(
            "Elsa.Workbench",
            hostId,
            membershipDatabase,
            controlDirectory,
            host.PackagesDirectory,
            host.PackageInstallRoot,
            host.ProcessId,
            output,
            (path, key, timeout) => host.PostAsync(path, key, timeout ?? Patience),
            expectedVersion => ReconcileAsync(FoundationHostFeed.FixturePackageId, expectedVersion),
            ReloadAsync,
            OrdersAsync,
            () => host.GetAsync(StartupControlStatusPath),
            () => host.ActivePackagesAsync(),
            () => host.MappedAssembliesAsync(),
            (packageId, package) => host.UpgradeInPlace(packageId, package),
            () => host.Output,
            () => host.ProcessId,
            () => host.IsRunning);

        await HostReadabilityScenario.RunAsync(driver, feed.FixturePackage);
    }

    private async Task StartAsync(
        bool autoReload,
        bool warmDefaultShell,
        IEnumerable<string>? packageFiles = null,
        bool automaticReconciliation = false,
        bool includeStartupControl = false,
        string? locksDirectory = null,
        Action<IDictionary<string, string>>? configureSettings = null)
    {
        var settings = Settings(feed);
        settings["Logging:LogLevel:Nuplane.Observability.ReconciliationLogger"] = "Information";
        settings["Elsa:ModuleManagement:ApiKey"] = ManagementKey;
        settings["Elsa:Readiness:WarmDefaultShell"] = warmDefaultShell.ToString();
        settings["Elsa:Shells:ReloadOnPackageChange"] = autoReload.ToString();
        settings["Elsa:Diagnostics:ConsoleLogStreaming:Enabled"] = "false";
        settings["Nuplane:Setup:AutomaticReconciliation"] = automaticReconciliation.ToString();
        settings["Nuplane:Setup:PollInterval"] = "1.00:00:00";
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
        configureSettings?.Invoke(settings);

        _host = await WorkbenchHostProcess.StartAsync(WithFixtureFeatures(includeStartupControl, locksDirectory), packageFiles ?? Array.Empty<string>(), settings, awaitShells: warmDefaultShell);
    }

    private string WithFixtureFeatures(bool includeStartupControl = false, string? locksDirectory = null)
    {
        var document = JsonNode.Parse(WorkbenchHostProcess.DefaultShellsJson)!.AsObject();
        var features = document["CShells"]!["Shells"]!["default"]!["Features"]!.AsObject();
        features[EntityFrameworkCoreFeature] = new JsonObject
        {
            ["ConnectionString"] = ConnectionString,
            ["Provider"] = "Sqlite"
        };
        features[OrdersFeature] = new JsonObject();
        if (locksDirectory is not null)
            features["FileSystemDistributedLocking"]!["LocksFolderPath"] = locksDirectory;
        if (includeStartupControl)
            features[StartupControlFeature] = new JsonObject();
        return document.ToJsonString();
    }

    private void CopyPackage(string package) =>
        File.Copy(package, Path.Join(_host!.PackagesDirectory, Path.GetFileName(package)));

    private async Task ReconcileAsync(string? packageId = null, string? expectedActiveVersion = null)
    {
        var (status, body) = await _host!.PostAsync(Reconcile, ManagementKey, Patience);
        Assert.Equal(HttpStatusCode.OK, status);
        var outcome = JsonNode.Parse(body)!;
        Assert.Equal("Completed", outcome["reconcile"]!["outcome"]!.GetValue<string>());

        await Polling.UntilAsync(async () =>
        {
            var active = await _host.ActivePackagesAsync();
            return expectedActiveVersion is null
                ? packageId is null ? active.Count == 0 : !active.ContainsKey(packageId)
                : active.TryGetValue(packageId!, out var version) && version == expectedActiveVersion;
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"Workbench active package state did not reach version {expectedActiveVersion ?? "<empty>"}. Host output:{Environment.NewLine}{_host.Output}");
    }

    private async Task ReloadDefaultAsync(int previousGeneration, string? expectedVersion)
    {
        var (status, _) = await _host!.PostAsync(ReloadDefault, ManagementKey, Patience);
        Assert.Equal(HttpStatusCode.OK, status);
        await Polling.UntilAsync(async () =>
        {
            var observation = await ObserveDefaultAsync();
            var route = await OrdersAsync();
            return observation.Generation > previousGeneration &&
                   (expectedVersion is null
                       ? route.Status == HttpStatusCode.NotFound
                       : route.Status == HttpStatusCode.OK && route.Text.Contains($"is at {expectedVersion[0]}.", StringComparison.Ordinal));
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"Workbench shell reload did not serve the expected package state. Host output:{Environment.NewLine}{_host.Output}");
    }

    private async Task WaitForGenerationAndOrdersAsync(int previousGeneration, string expectedVersion) =>
        await Polling.UntilAsync(async () =>
        {
            var observation = await ObserveDefaultAsync();
            var route = await OrdersAsync();
            return observation.Generation > previousGeneration && route.Status == HttpStatusCode.OK &&
                   route.Text.Contains($"is at {expectedVersion}.", StringComparison.Ordinal);
        }, Patience, TimeSpan.FromMilliseconds(200), () => $"Workbench did not auto-reload to fixture version {expectedVersion}. Host output:{Environment.NewLine}{_host!.Output}");

    private async Task<(int? Generation, string ProcessInstanceId)> ObserveDefaultAsync()
    {
        var (status, body) = await _host!.GetManagementAsync(ObserveDefault, ManagementKey);
        Assert.Equal(HttpStatusCode.OK, status);
        var observation = JsonNode.Parse(body)!;
        return (observation["activeGeneration"]?.GetValue<int>(), observation["processInstanceId"]!.GetValue<string>());
    }

    private async Task<(HttpStatusCode Status, string Text)> OrdersAsync()
    {
        var (status, body) = await _host!.GetAsync(OrdersPath);
        try
        {
            return (status, JsonSerializer.Deserialize<string>(body) ?? body);
        }
        catch (JsonException)
        {
            return (status, body);
        }
    }

    private async Task<IReadOnlyList<string>> FixtureAssemblyPathsAsync()
    {
        var mapped = await _host!.MappedAssembliesAsync();
        HostProcessAssemblyEvidence.Write(output, "Elsa.Workbench", _host.ProcessId, mapped);
        return mapped
            .Where(path => StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), FoundationHostFeed.FixturePackageId + ".dll"))
            .Where(path => HostProcessAssemblyEvidence.IsWithinInstallRoot(path, _host.PackageInstallRoot))
            .ToArray();
    }
}
