using System.Net;
using System.Runtime.ExceptionServices;
using System.Text.Json.Nodes;
using Elsa.Cluster.Core.Options;
using Microsoft.Data.Sqlite;
using Xunit.Abstractions;

namespace Elsa.Cluster.EntityFrameworkCore.Tests;

/// <summary>Runs one real child-host package transition while observing its durable readability report.</summary>
internal static class HostReadabilityScenario
{
    private const string Family = FeedModuleDatabase.Family;
    private const string PackageId = FoundationHostFeed.FixturePackageId;
    private const string HoldPath = "/feed-module-fixture/startup-control/hold/";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(100);

    public static string CreateOwnedRoot() => Directory.CreateTempSubdirectory("elsa-host-readability-").FullName;

    public static void Configure(IDictionary<string, string> settings, string hostId, string membershipDatabase, string controlDirectory, bool automaticReconciliation)
    {
        var membership = ClusterMembershipOptions.SectionName;
        var ef = $"{membership}:{EfClusterMembershipOptions.SectionKey}";
        settings[$"{membership}:{nameof(ClusterMembershipOptions.HostId)}"] = hostId;
        settings[$"{membership}:{nameof(ClusterMembershipOptions.HeartbeatInterval)}"] = "00:00:02";
        settings[$"{membership}:{nameof(ClusterMembershipOptions.ExpiryPeriod)}"] = "00:00:06";
        settings[$"{membership}:{nameof(ClusterMembershipOptions.SkewAllowance)}"] = "00:00:01";
        settings[$"{ef}:Enabled"] = "true";
        settings[$"{ef}:Provider"] = "Sqlite";
        settings[$"{ef}:ConnectionString"] = $"Data Source={membershipDatabase};Pooling=False;Default Timeout=5";
        settings[$"{ef}:CleanupPeriod"] = "00:01:00";
        settings["Elsa:Cluster:ReadabilityControl:Directory"] = controlDirectory;
        settings["Nuplane:Setup:AutomaticReconciliation"] = automaticReconciliation.ToString();
        settings["Nuplane:Setup:PollInterval"] = "1.00:00:00";
        settings["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false";
    }

    public static async Task RunAsync(HostReadabilityDriver host, string v2Package)
    {
        var operationId = Guid.NewGuid().ToString("N");
        var enteredPath = Path.Join(host.ControlDirectory, $"{operationId}.entered");
        var releasePath = Path.Join(host.ControlDirectory, $"{operationId}.release");
        var disposedMarker = $"FEED_MODULE_GENERATION_HOLD|{operationId}|disposed|";
        var failedMarker = $"FEED_MODULE_GENERATION_HOLD|{operationId}|failed|";
        Task<(HttpStatusCode Status, string Body)>? heldRequest = null;
        var released = false;
        var terminalMarkerObserved = false;
        Exception? primaryFailure = null;
        var cleanupFailures = new List<Exception>();

        try
        {
            var initialPackages = await host.ActivePackagesAsync().ConfigureAwait(false);
            Assert.Equal("1.0.0", initialPackages[PackageId]);
            var initialGeneration = await WaitForPackageGenerationVersionAsync(host, "1.0.0", "1").ConfigureAwait(false);
            await WaitForOrdersVersionAsync(host, "1.").ConfigureAwait(false);
            var initialReport = await WaitForReportAsync(host, snapshot => snapshot.ReadableVersions.SequenceEqual(["1"])).ConfigureAwait(false);
            Assert.Equal("Active", initialReport.Status);
            WritePhase(host, "v1-active", initialReport, initialPackages);
            host.TestOutput.WriteLine($"HOST_READABILITY host={host.Name} phase=v1-shell pid={host.ProcessId} generation={initialGeneration} fixtureVersion=1");

            heldRequest = host.PostAsync(HoldPath + operationId, null, Patience);
            await Polling.UntilAsync(
                () => Task.FromResult(File.Exists(enteredPath)),
                TimeSpan.FromSeconds(30),
                PollInterval,
                () => $"The v1 request did not enter its generation hold. Host output:{Environment.NewLine}{host.Output()}").ConfigureAwait(false);
            var capturedGeneration = int.Parse(await File.ReadAllTextAsync(enteredPath).ConfigureAwait(false), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(initialGeneration, capturedGeneration);
            host.TestOutput.WriteLine($"HOST_READABILITY host={host.Name} phase=held-request pid={host.ProcessId} operation={operationId} generation={capturedGeneration}");

            host.UpgradeInPlace(PackageId, v2Package);
            await host.ReconcileAsync("2.0.0").ConfigureAwait(false);
            await host.ReloadAsync().ConfigureAwait(false);
            var candidateGeneration = await WaitForPackageGenerationVersionAsync(host, "2.0.0", "2").ConfigureAwait(false);
            Assert.True(candidateGeneration > capturedGeneration, "The v2 status route must run in a newer provider than the captured v1 request.");
            Assert.Equal(host.ProcessId, host.CurrentProcessId());
            var dormantOrders = await host.OrdersAsync().ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Conflict, dormantOrders.Status);

            var mapped = await host.MappedAssembliesAsync().ConfigureAwait(false);
            HostProcessAssemblyEvidence.Write(host.TestOutput, host.Name, host.ProcessId, mapped);
            Assert.Contains(mapped, path =>
                StringComparer.OrdinalIgnoreCase.Equals(Path.GetFileName(path), PackageId + ".dll")
                && HostProcessAssemblyEvidence.IsWithinInstallRoot(path, host.PackageInstallRoot)
                && path.Contains("2.0.0", StringComparison.OrdinalIgnoreCase));

            // The newer shell is constructed and serves its unconstrained status endpoint, but its Orders feature must remain dormant:
            // the active v1 request keeps schema 2 from finalizing in this same process.
            var v2ServedAtUtcTicks = DateTimeOffset.UtcNow.UtcTicks;
            var whileHeld = await WaitForReportAsync(host,
                snapshot => snapshot.HeartbeatAtUtcTicks > v2ServedAtUtcTicks
                            && snapshot.ReadableVersions.SequenceEqual(["1"])).ConfigureAwait(false);
            Assert.Equal("Active", whileHeld.Status);
            Assert.True(whileHeld.RowRevision >= initialReport.RowRevision);
            Assert.True(whileHeld.ReportRevision >= initialReport.ReportRevision);
            Assert.False(heldRequest.IsCompleted, "The old request must still hold the captured v1 shell while the v2 report is freshly recomposed.");
            WritePhase(host, "v2-serving-held-v1", whileHeld, await host.ActivePackagesAsync().ConfigureAwait(false));
            host.TestOutput.WriteLine($"HOST_READABILITY host={host.Name} phase=v2-shell-candidate pid={host.ProcessId} generation={candidateGeneration} fixtureVersion=2 ordersStatus={(int)dormantOrders.Status}");

            await File.WriteAllTextAsync(releasePath, "release").ConfigureAwait(false);
            released = true;
            var (holdStatus, holdBody) = await heldRequest.WaitAsync(Patience).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.OK, holdStatus);
            var holdResponseGeneration = JsonNode.Parse(holdBody)!["generation"]!.GetValue<int>();
            Assert.Equal(capturedGeneration, holdResponseGeneration);

            await Task.WhenAll(
                WaitForOutputAsync(host, disposedMarker + capturedGeneration + "|Disposed"),
                WaitForReportAsync(host, snapshot => snapshot.ReadableVersions.SequenceEqual(["1", "2"]))
            ).ConfigureAwait(false);
            terminalMarkerObserved = true;
            await WaitForOrdersVersionAsync(host, "2.").ConfigureAwait(false);
            var drainedReport = await ReadReportAsync(host.MembershipDatabase, host.HostId).ConfigureAwait(false);
            Assert.NotNull(drainedReport);
            WritePhase(host, "old-provider-drained", drainedReport, await host.ActivePackagesAsync().ConfigureAwait(false));
            host.TestOutput.WriteLine($"HOST_READABILITY host={host.Name} phase=old-provider-drained-operation pid={host.ProcessId} operation={operationId} generation={capturedGeneration} marker=Disposed");

            foreach (var package in FoundationHostProcess.Releases(host.PackagesDirectory, PackageId).ToArray())
                File.Delete(package);
            await host.ReconcileAsync(null).ConfigureAwait(false);
            await host.ReloadAsync().ConfigureAwait(false);
            await WaitForEmptyActiveGraphAsync(host).ConfigureAwait(false);
            var zeroPackages = await WaitForReportAsync(host, snapshot => snapshot.ReadableVersions.SequenceEqual(["1"])).ConfigureAwait(false);
            Assert.Equal("Active", zeroPackages.Status);
            WritePhase(host, "zero-active-packages", zeroPackages, await host.ActivePackagesAsync().ConfigureAwait(false));

            File.Copy(v2Package, Path.Join(host.PackagesDirectory, Path.GetFileName(v2Package)));
            await host.ReconcileAsync("2.0.0").ConfigureAwait(false);
            await host.ReloadAsync().ConfigureAwait(false);
            await WaitForPackageGenerationVersionAsync(host, "2.0.0", "2").ConfigureAwait(false);
            await WaitForOrdersVersionAsync(host, "2.").ConfigureAwait(false);
            var reintroducedReport = await WaitForReportAsync(host, snapshot => snapshot.ReadableVersions.SequenceEqual(["1", "2"])).ConfigureAwait(false);
            WritePhase(host, "v2-reintroduced", reintroducedReport, await host.ActivePackagesAsync().ConfigureAwait(false));
            Assert.Equal(host.ProcessId, host.CurrentProcessId());
            Assert.True(host.IsRunning());
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }
        finally
        {
            if (heldRequest is not null)
            {
                if (!released)
                {
                    try
                    {
                        Directory.CreateDirectory(host.ControlDirectory);
                        await File.WriteAllTextAsync(releasePath, "release").ConfigureAwait(false);
                        released = true;
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                    }
                }

                try
                {
                    await heldRequest.WaitAsync(Patience).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    cleanupFailures.Add(exception);
                }

                if (!terminalMarkerObserved && File.Exists(enteredPath) && released)
                {
                    try
                    {
                        await WaitForTerminalOutputAsync(host, disposedMarker, failedMarker).ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        cleanupFailures.Add(exception);
                    }
                }
            }
        }

        if (primaryFailure is not null)
        {
            if (cleanupFailures.Count > 0)
                throw new AggregateException("The host readability scenario failed and owned request cleanup also failed.", [primaryFailure, .. cleanupFailures]);
            ExceptionDispatchInfo.Capture(primaryFailure).Throw();
        }

        if (cleanupFailures.Count > 0)
            throw new AggregateException("The host readability scenario could not clean up its held request.", cleanupFailures);
    }

    private static void WritePhase(HostReadabilityDriver host, string phase, HostReadabilitySnapshot snapshot, IReadOnlyDictionary<string, string> packages) =>
        host.TestOutput.WriteLine($"HOST_READABILITY host={host.Name} phase={phase} pid={host.ProcessId} packages={string.Join(',', packages.OrderBy(package => package.Key, StringComparer.OrdinalIgnoreCase).Select(package => $"{package.Key}={package.Value}"))} row={Format(snapshot)}");

    private static string Format(HostReadabilitySnapshot? snapshot) =>
        snapshot is null
            ? "<no-active-row>"
            : $"status={snapshot.Status},heartbeatUtcTicks={snapshot.HeartbeatAtUtcTicks},rowRevision={snapshot.RowRevision},reportRevision={snapshot.ReportRevision},readableVersions=[{string.Join(',', snapshot.ReadableVersions)}]";

    private static async Task<int> WaitForPackageGenerationVersionAsync(HostReadabilityDriver host, string packageVersion, string fixtureVersion)
    {
        int generation = 0;
        await Polling.UntilAsync(async () =>
        {
            var packages = await host.ActivePackagesAsync().ConfigureAwait(false);
            var (status, body) = await host.GenerationStatusAsync().ConfigureAwait(false);
            if (packages.TryGetValue(PackageId, out var activeVersion)
                && activeVersion == packageVersion
                && status == HttpStatusCode.OK)
            {
                var statusNode = JsonNode.Parse(body)!;
                if (statusNode["fixtureVersion"]?.GetValue<string>() == fixtureVersion)
                {
                    generation = statusNode["generation"]!.GetValue<int>();
                    return true;
                }
            }

            return false;
        }, Patience, PollInterval, () => $"Package {packageVersion} did not produce the expected shell generation. Host output:{Environment.NewLine}{host.Output()}").ConfigureAwait(false);
        return generation;
    }

    private static async Task WaitForOrdersVersionAsync(HostReadabilityDriver host, string routeVersion)
    {
        await Polling.UntilAsync(async () =>
        {
            var (status, body) = await host.OrdersAsync().ConfigureAwait(false);
            return status == HttpStatusCode.OK && body.Contains($"is at {routeVersion}", StringComparison.Ordinal);
        }, Patience, PollInterval, () => $"Orders did not serve fixture version {routeVersion}. Host output:{Environment.NewLine}{host.Output()}").ConfigureAwait(false);
    }

    private static async Task WaitForEmptyActiveGraphAsync(HostReadabilityDriver host)
    {
        await Polling.UntilAsync(async () =>
        {
            var packages = await host.ActivePackagesAsync().ConfigureAwait(false);
            var (status, _) = await host.OrdersAsync().ConfigureAwait(false);
            return packages.Count == 0 && status == HttpStatusCode.NotFound;
        }, Patience, PollInterval, () => $"The empty package graph did not produce a shell without Orders. Host output:{Environment.NewLine}{host.Output()}").ConfigureAwait(false);
    }

    private static async Task<HostReadabilitySnapshot> WaitForReportAsync(HostReadabilityDriver host, Func<HostReadabilitySnapshot, bool> condition)
    {
        HostReadabilitySnapshot? snapshot = null;
        await Polling.UntilAsync(async () =>
        {
            snapshot = await ReadReportAsync(host.MembershipDatabase, host.HostId).ConfigureAwait(false);
            return snapshot is not null && condition(snapshot);
        }, Patience, PollInterval, () => $"The persisted readability report did not reach the expected state. Last row: {snapshot}. Host output:{Environment.NewLine}{host.Output()}").ConfigureAwait(false);
        return snapshot!;
    }

    private static async Task<HostReadabilitySnapshot?> ReadReportAsync(string databaseFile, string hostId)
    {
        await using var connection = new SqliteConnection($"Data Source={databaseFile};Pooling=False;Default Timeout=5");
        await connection.OpenAsync().ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
                             SELECT Status, HeartbeatAtUtcTicks, Revision, ReportRevision, ReportJson
                             FROM elsa_cluster_members
                             WHERE HostId = $hostId AND CurrentHostId = $hostId AND Status = 'Active'
                             ORDER BY Revision DESC LIMIT 1
                             """;
        command.Parameters.AddWithValue("$hostId", hostId);
        await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
        if (!await reader.ReadAsync().ConfigureAwait(false))
            return null;

        var document = JsonNode.Parse(reader.GetString(4))!;
        var entry = document["readability"]?["entries"]?.AsArray()
            .SingleOrDefault(value => StringComparer.Ordinal.Equals(value?["family"]?.GetValue<string>(), Family));
        var versions = entry?["readableVersions"]?.AsArray().Select(value => value!.GetValue<string>()).ToArray() ?? [];
        return new HostReadabilitySnapshot(
            reader.GetString(0),
            reader.GetInt64(1),
            reader.GetInt64(2),
            reader.GetInt64(3),
            versions);
    }

    private static Task WaitForOutputAsync(HostReadabilityDriver host, string marker) =>
        Polling.UntilAsync(() => Task.FromResult(host.Output().Contains(marker, StringComparison.Ordinal)), Patience, PollInterval,
            () => $"The captured shell drain marker did not appear: {marker}. Host output:{Environment.NewLine}{host.Output()}");

    private static Task WaitForTerminalOutputAsync(HostReadabilityDriver host, string disposedPrefix, string failedPrefix) =>
        Polling.UntilAsync(() => Task.FromResult(host.Output().Contains(disposedPrefix, StringComparison.Ordinal)
                                || host.Output().Contains(failedPrefix, StringComparison.Ordinal)),
            Patience, PollInterval, () => $"The captured shell drain did not produce a terminal marker. Host output:{Environment.NewLine}{host.Output()}");
}

internal sealed record HostReadabilitySnapshot(string Status, long HeartbeatAtUtcTicks, long RowRevision, long ReportRevision, IReadOnlyList<string> ReadableVersions);

internal sealed record HostReadabilityDriver(
    string Name,
    string HostId,
    string MembershipDatabase,
    string ControlDirectory,
    string PackagesDirectory,
    string PackageInstallRoot,
    int ProcessId,
    ITestOutputHelper TestOutput,
    Func<string, string?, TimeSpan?, Task<(HttpStatusCode Status, string Body)>> PostAsync,
    Func<string?, Task> ReconcileAsync,
    Func<Task> ReloadAsync,
    Func<Task<(HttpStatusCode Status, string Text)>> OrdersAsync,
    Func<Task<(HttpStatusCode Status, string Body)>> GenerationStatusAsync,
    Func<Task<IReadOnlyDictionary<string, string>>> ActivePackagesAsync,
    Func<Task<IReadOnlyList<string>>> MappedAssembliesAsync,
    Action<string, string> UpgradeInPlace,
    Func<string> Output,
    Func<int> CurrentProcessId,
    Func<bool> IsRunning);
