using System.Collections.Concurrent;
using System.IO.Compression;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Studio.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Elsa.Studio.Hosting;
using Elsa.Studio.Hosting.Nuplane;
using Microsoft.AspNetCore.Builder;
using Nuplane.Abstractions;
using Nuplane.Admin;
using Nuplane.Feeds.Configuration;
using Nuplane.Reconciliation;
using Nuplane.Reconciliation.Configuration;
using Nuplane.Reconciliation.LockFile;

namespace Elsa.Studio.Tests;

public sealed class StudioPackageLifecycleTests
{
    [Fact]
    public async Task ProductionProfileDiscoversUpdatesRetriesFailuresAndRemovesRealPackages()
    {
        const string packageId = "Acme.StudioLifecycle";
        var root = Path.Combine(Path.GetTempPath(), "studio-lifecycle-" + Guid.NewGuid().ToString("N"));
        var feed = Path.Combine(root, "feed");
        Directory.CreateDirectory(feed);
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = timeout.Token;
        using var logs = new CapturingLoggerProvider();
        try
        {
            var builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions { ContentRootPath = root });
            builder.Logging.ClearProviders();
            builder.Logging.AddProvider(logs);
            File.Copy(Path.Combine(AppContext.BaseDirectory, "WebHost", "nuplane-management.json"), Path.Combine(root, "nuplane-management.json"));
            builder.Configuration.AddJsonFile(Path.Combine(AppContext.BaseDirectory, "WebHost", "appsettings.json"));
            var studio = builder.AddElsaStudio(options => options.ShellsFile = null)
                .AddElsaStudioNuplane(configureNuplane: nuplane => nuplane.WithStateFile(Path.Combine(root, "state.json")));
            builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["CShells:Shells:Default:Features:StudioApi"] = "true",
                ["CShells:Shells:Default:Features:StudioReuseProbe"] = "true",
                ["CShells:Shells:Default:Features:StudioReloadGate:Fail"] = "false",
                ["Nuplane:Setup:AutomaticReconciliation"] = "false",
                ["Nuplane:Setup:Feeds:0:DirectoryPath"] = feed,
                ["Nuplane:Setup:Feeds:0:Directory:Watch"] = "false"
            });
            builder.Services.Configure<FeedResolutionOptions>(options => options.PackageInstallRoot = Path.Combine(root, "installs"));
            builder.Services.Configure<ReconciliationOptions>(options => options.EnableAutomaticReconciliation = false);
            builder.Services.Configure<LockFileOptions>(options => options.Path = Path.Combine(root, "nuplane.lock.json"));
            studio.ConfigureShells(shells => shells
                .WithStudioFeatureAssemblies()
                .WithAssemblies(typeof(StudioReloadGateFeature).Assembly));

            await using var host = builder.Build();
            await host.StartAsync(token);
            var registry = host.Services.GetRequiredService<IShellRegistry>();
            var catalog = host.Services.GetRequiredService<IRuntimeFeatureCatalog>();
            var admin = host.Services.GetRequiredService<INuplaneAdminOperations>();
            Assert.Empty(registry.GetActiveShells());

            Pack("1.0.0", "First");
            await Reconcile();
            Assert.Empty(registry.GetActiveShells());
            Assert.Empty(logs.ReloadResults);
            var first = await registry.GetOrActivateAsync("Default", token);
            await using (var retained = first.BeginScope())
            {
                Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());
                Assert.Contains((await catalog.GetSnapshotAsync(token)).FeatureDescriptors, feature => feature.Id == "StudioReuseProbe");

                File.Delete(Path.Combine(feed, packageId + ".1.0.0.nupkg"));
                Pack("2.0.0", "Second");
                builder.Configuration["CShells:Shells:Default:Features:StudioReloadGate:Fail"] = "true";
                await Reconcile();
                Assert.Same(first, registry.GetActive("Default"));
                var updatedFeature = (await catalog.GetSnapshotAsync(token)).FeatureDescriptors.Single(feature => feature.Id == "StudioReuseProbe");
                Assert.NotNull(updatedFeature.StartupType);
                Assert.Equal("Second", updatedFeature.StartupType.Assembly.GetName().Name);

                // No additional feed change: the shared adapter must retain and retry the failed reload.
                builder.Configuration["CShells:Shells:Default:Features:StudioReloadGate:Fail"] = "false";
                await Reconcile();
                var second = registry.GetActive("Default");
                Assert.NotNull(second);
                Assert.NotSame(first, second);
                Assert.Contains(logs.ReloadResults, entry => entry.Level == LogLevel.Warning && entry.Message.Contains("Default") && entry.Exception?.ToString().Contains("Studio fixture reload failure") == true);
                await using (var current = second.BeginScope())
                    Assert.Equal("second", current.ServiceProvider.GetRequiredService<string>());
                Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());

                // The public observer's requested-token cancellation must propagate, without a detached task.
                var observer = host.Services.GetServices<INuplaneObserver>().Single(value => value is IShellGenerationBuildParticipant);
                var active = Assert.Single((await admin.GetPackagesAsync(token)).Packages);
                var resolved = new ResolvedPackage(packageId, "2.0.0", Assert.IsType<string>(active.FeedName), active.InstallPath, DateTimeOffset.UtcNow, active.SourceName ?? "");
                var change = new PackageChangeSet([], [resolved], [], Guid.NewGuid().ToString("N"), DateTimeOffset.UtcNow);
                using (var cancelled = new CancellationTokenSource())
                {
                    cancelled.Cancel();
                    await Assert.ThrowsAnyAsync<OperationCanceledException>(() => observer.OnPackagesReconciledAsync(change, [resolved], cancelled.Token));
                }
                await Reconcile();

                File.Delete(Path.Combine(feed, packageId + ".2.0.0.nupkg"));
                await Reconcile();
                var empty = registry.GetActive("Default");
                Assert.NotNull(empty);
                Assert.NotSame(second, empty);
                await using (var current = empty.BeginScope())
                    Assert.Null(current.ServiceProvider.GetService<string>());
                Assert.DoesNotContain((await catalog.GetSnapshotAsync(token)).FeatureDescriptors, feature => feature.Id == "StudioReuseProbe");
                Assert.Empty((await admin.GetPackagesAsync(token)).Packages);
                Assert.Equal("first", retained.ServiceProvider.GetRequiredService<string>());

                var generation = catalog.CurrentSnapshot.Generation;
                var reports = logs.ReloadResults.Count;
                await Reconcile();
                Assert.Same(empty, registry.GetActive("Default"));
                Assert.Equal(generation, catalog.CurrentSnapshot.Generation);
                Assert.Equal(reports, logs.ReloadResults.Count);
            }
            await host.StopAsync(token);
            Assert.True(host.Services.GetRequiredService<IHostApplicationLifetime>().ApplicationStopped.IsCancellationRequested);

            async Task Reconcile()
            {
                var outcome = await admin.TriggerReconcileAsync(token);
                Assert.True(outcome.OutcomeCode == ManualReconcileOutcomeCode.Completed && outcome.RunResult is { IsDegraded: false }, System.Text.Json.JsonSerializer.Serialize(outcome));
            }

            void Pack(string version, string fixture)
            {
                using var archive = ZipFile.Open(Path.Combine(feed, packageId + "." + version + ".nupkg"), ZipArchiveMode.Create);
                using (var writer = new StreamWriter(archive.CreateEntry(packageId + ".nuspec").Open()))
                    writer.Write($"<package><metadata><id>{packageId}</id><version>{version}</version><authors>Studio tests</authors><description>Owned lifecycle fixture</description></metadata></package>");
                archive.CreateEntryFromFile(Path.Combine(AppContext.BaseDirectory, fixture + ".dll"), "lib/net10.0/" + fixture + ".dll");
                // A bundled contract copy must still bind to the host identity selected by Studio's sharing policy.
                archive.CreateEntryFromFile(typeof(IShellFeature).Assembly.Location, "lib/net10.0/CShells.Abstractions.dll");
            }
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [ShellFeature("StudioReloadGate")]
    public sealed class StudioReloadGateFeature : IShellFeature
    {
        public bool Fail { get; set; }
        public void ConfigureServices(IServiceCollection services)
        {
            if (Fail)
                throw new InvalidOperationException("Studio fixture reload failure");
        }
    }

    private sealed record LogEntry(LogLevel Level, Exception? Exception, string Message);

    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        private readonly ConcurrentQueue<LogEntry> entries = new();
        public IReadOnlyCollection<LogEntry> ReloadResults => entries.ToArray();
        public ILogger CreateLogger(string categoryName) => new Capture(categoryName == "Elsa.Studio.Web.PackageReload", entries);
        public void Dispose() { }

        private sealed class Capture(bool capture, ConcurrentQueue<LogEntry> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
            public bool IsEnabled(LogLevel logLevel) => true;
            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (capture) entries.Enqueue(new(logLevel, exception, formatter(state, exception)));
            }
        }
    }
}
