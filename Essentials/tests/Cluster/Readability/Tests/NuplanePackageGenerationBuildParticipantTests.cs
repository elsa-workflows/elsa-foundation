using System.Reflection;
using System.Threading.Channels;
using CShells;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Persistence.Schema;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;
using Nuplane.Loading;
using static Elsa.Cluster.Readability.Tests.UpgradedPackage;

namespace Elsa.Cluster.Readability.Tests;

public sealed class NuplanePackageGenerationBuildParticipantTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public async Task AddEfSchemaReadability_composes_one_canonical_participant_and_source_when_repeated()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        await using var host = BuildHost(package, new RuntimeCatalog(FeatureOf(package.Current)));

        var participant = host.GetRequiredService<NuplanePackageGenerationBuildParticipant>();
        Assert.Same(participant, host.GetRequiredService<IShellGenerationBuildParticipant>());
        Assert.Single(host.GetServices<IShellGenerationBuildParticipant>());
        Assert.IsType<NuplanePackageGenerations>(host.GetRequiredService<ISupersededAssemblySource>());

        var lease = await participant.BeginAsync(Context("identity", 1));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Existing_superseded_source_remains_the_override_and_does_not_install_a_second_tracker()
    {
        var custom = new EmptySupersededAssemblySource();
        var services = new ServiceCollection()
            .AddSingleton<ISupersededAssemblySource>(custom)
            .AddEfSchemaReadability();
        await using var host = services.BuildServiceProvider();

        Assert.Same(custom, host.GetRequiredService<ISupersededAssemblySource>());
        Assert.Empty(host.GetServices<IShellGenerationBuildParticipant>());
    }

    [Fact]
    public async Task Begin_pins_conservatively_then_exact_selected_snapshot_owns_the_pin_until_duplicate_release()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new StockRuntimeCatalog(FeatureOf(package.Current));
        await using var host = BuildHost(package, catalog);
        var participant = host.GetRequiredService<IShellGenerationBuildParticipant>();
        var source = host.GetRequiredService<ISupersededAssemblySource>();

        var lease = await participant.BeginAsync(Context("selected-snapshot", 1));
        Assert.Equal(PreviousOnly, await package.ReadableAsync(host));
        Assert.DoesNotContain(package.Previous, await source.GetRetiredAsync());

        // The catalog's current snapshot names the replacement, but this in-flight build selected the older snapshot.
        await lease.OnSnapshotSelectedAsync(RuntimeCatalog.Snapshot(2, FeatureOf(package.Previous)));
        Assert.DoesNotContain(package.Previous, await source.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await package.ReadableAsync(host));

        await lease.DisposeAsync();
        await lease.DisposeAsync();

        Assert.Contains(package.Previous, await source.GetRetiredAsync());
        Assert.Equal(Both, await package.ReadableAsync(host));
    }

    [Fact]
    public async Task Proven_replacement_history_does_not_retire_a_removed_package_held_by_no_current_generation()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new RuntimeCatalog(FeatureOf(package.Current));
        await using var host = BuildHost(package, catalog);
        var source = host.GetRequiredService<ISupersededAssemblySource>();

        var lease = await host.GetRequiredService<IShellGenerationBuildParticipant>()
            .BeginAsync(Context("history-with-build-lease", 1));
        try
        {
            Assert.Contains(package.Previous, await source.GetReplacedAsync());

            // The selected build and current feature catalog use only the new assembly. The old identity remains known
            // for activation after package removal, but removal is not fresh evidence that it is retired/readable.
            await lease.OnSnapshotSelectedAsync(await catalog.GetSnapshotAsync());
            package.Catalog.Active = [];

            Assert.Contains(package.Previous, await source.GetReplacedAsync());
            Assert.Empty(await source.GetRetiredAsync());
            Assert.Equal(PreviousOnly, await package.ReadableAsync(host));
        }
        finally
        {
            await lease.DisposeAsync();
        }
    }

    [Fact]
    public async Task Selected_snapshot_does_not_pin_a_scanned_assembly_without_a_feature_descriptor()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new StockRuntimeCatalog(FeatureOf(package.Current));
        await using var host = BuildHost(package, catalog);
        var participant = host.GetRequiredService<IShellGenerationBuildParticipant>();
        var lease = await participant.BeginAsync(Context("scanned-only", 1));

        // The selected snapshot was produced by a wider scan, but its only feature comes from the replacement context.
        await lease.OnSnapshotSelectedAsync(RuntimeCatalog.SnapshotWithScanned(
            2,
            [FeatureOf(package.Current)],
            [package.Previous]));

        Assert.Contains(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
        Assert.Equal(Both, await package.ReadableAsync(host));
        await lease.DisposeAsync();
    }

    [Fact]
    public async Task Cancellation_before_Begin_does_not_subscribe_or_acquire_a_lease()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new StockRuntimeCatalog(FeatureOf(package.Current));
        await using var host = BuildHost(package, catalog);
        var participant = host.GetRequiredService<IShellGenerationBuildParticipant>();
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
            await participant.BeginAsync(Context("cancelled", 1), cancellation.Token));

        Assert.Equal(1, catalog.SubscriberCount);
        Assert.Contains(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
    }

    [Fact]
    public async Task Stock_catalog_subscription_reconciles_late_state_and_publishes_reintroduction_but_not_an_unchanged_commit()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        // This snapshot predates the participant subscription; the first build must reconcile it after subscribing.
        var catalog = new StockRuntimeCatalog(FeatureOf(package.Current));
        await using var host = BuildHost(package, catalog);
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        var membership = Membership(host);
        await membership.WaitForReadabilityAsync(package.Family, Both, 1);
        Assert.Equal(1, membership.PublishCount);

        Assert.Contains(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());

        // Rollback/reintroduction restores the readability constraint although no shell needs to be disposed.
        var beforeReintroduction = membership.PublishCount;
        catalog.Commit(FeatureOf(package.Previous));
        await membership.WaitForReadabilityAsync(package.Family, PreviousOnly, beforeReintroduction + 1);
        Assert.Equal(PreviousOnly, await package.ReadableAsync(host));

        var afterReintroduction = membership.PublishCount;
        catalog.Commit(FeatureOf(package.Previous));
        await catalog.WaitForReadAsync(catalog.CurrentSnapshot.Generation);

        // The later changed report is an ordered positive fence for the unchanged evaluation. A redundant unchanged
        // publication would raise the final count; no timing window is used to infer worker quiescence.
        catalog.Commit(FeatureOf(package.Current));
        await membership.WaitForReadabilityAsync(package.Family, Both, afterReintroduction + 1);
        Assert.Equal(afterReintroduction + 1, membership.PublishCount);
        Assert.Contains(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
        Assert.Equal(Both, await package.ReadableAsync(host));
    }

    [Fact]
    public async Task Root_disposal_detaches_catalog_notifications_and_rejects_later_builds()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new StockRuntimeCatalog(FeatureOf(package.Current));
        var host = BuildHost(package, catalog);
        var participant = host.GetRequiredService<NuplanePackageGenerationBuildParticipant>();
        var lease = await participant.BeginAsync(Context("shutdown", 1));
        await lease.OnSnapshotSelectedAsync(await catalog.GetSnapshotAsync());
        Assert.Equal(1, catalog.SubscriberCount);

        await lease.DisposeAsync();
        await host.DisposeAsync();

        Assert.Equal(0, catalog.SubscriberCount);
        await Assert.ThrowsAsync<ObjectDisposedException>(async () =>
            await participant.BeginAsync(Context("after-shutdown", 2)));
    }

    [Fact]
    public async Task A_lease_acquired_during_an_awaited_package_catalog_read_is_included_in_the_retirement_snapshot()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var packageCatalog = new GatedPackageCatalog(package.Catalog);
        await using var host = BuildHost(package, new RuntimeCatalog(FeatureOf(package.Current)), packageCatalog);
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        var source = host.GetRequiredService<ISupersededAssemblySource>();
        var evaluation = source.GetRetiredAsync().AsTask();
        IShellGenerationBuildLease? lease = null;
        try
        {
            await packageCatalog.Entered.Task.WaitAsync(Patience);
            // Neither acquiring nor selecting a lease may wait for the catalog's outstanding asynchronous read.
            lease = await host.GetRequiredService<IShellGenerationBuildParticipant>()
                .BeginAsync(Context("during-read", 1)).AsTask().WaitAsync(Patience);
            await lease.OnSnapshotSelectedAsync(RuntimeCatalog.Snapshot(2, FeatureOf(package.Previous)))
                .AsTask().WaitAsync(Patience);
            packageCatalog.Continue.TrySetResult();

            Assert.DoesNotContain(package.Previous, await evaluation.WaitAsync(Patience));
        }
        finally
        {
            packageCatalog.Continue.TrySetResult();
            try
            {
                await evaluation.WaitAsync(Patience);
            }
            finally
            {
                if (lease is not null)
                    await lease.DisposeAsync();
            }
        }
    }

    [Fact]
    public async Task Custom_catalog_without_commit_notifications_is_reconciled_by_the_conservative_watch()
    {
        using var package = new UpgradedPackage();
        package.Catalog.Active = [package.Current];
        var catalog = new RuntimeCatalog(FeatureOf(package.Previous));
        await using var host = BuildHost(package, catalog);

        // Binding mirrors CShells resolving the root lifecycle subscriber; this does not refresh the catalog.
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        Assert.DoesNotContain(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
        var membership = Membership(host);
        var before = membership.PublishCount;

        catalog.SetWithoutNotification(FeatureOf(package.Current));
        await membership.WaitForReadabilityAsync(package.Family, Both, before + 1);
        Assert.Contains(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());

        var beforeRollback = membership.PublishCount;
        catalog.SetWithoutNotification(FeatureOf(package.Previous));
        await membership.WaitForReadabilityAsync(package.Family, PreviousOnly, beforeRollback + 1);
        Assert.DoesNotContain(package.Previous, await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
    }

    private static ServiceProvider BuildHost(UpgradedPackage package, IRuntimeFeatureCatalog catalog, IPackageAssemblyCatalog? packageCatalog = null)
    {
        var services = new ServiceCollection()
            .Configure<ClusterMembershipOptions>(options => options.HostId = $"generation-{Guid.NewGuid():N}")
            .AddSingleton<IPackageAssemblyCatalog>(packageCatalog ?? package.Catalog)
            .AddSingleton<IRuntimeFeatureCatalog>(catalog)
            .AddSingleton<IClusterMembership>(provider => new RecordingMembership(
                ActivatorUtilities.CreateInstance<InProcessClusterMembership>(provider)))
            .AddEfSchemaReadability()
            .AddEfSchemaReadability();

        return services.BuildServiceProvider();
    }

    private static RecordingMembership Membership(IServiceProvider services) =>
        Assert.IsType<RecordingMembership>(services.GetRequiredService<IClusterMembership>());

    private static ShellGenerationBuildContext Context(string name, int generation) =>
        new(ShellDescriptor.Create(name, generation), new ShellId(name));

    private sealed class EmptySupersededAssemblySource : ISupersededAssemblySource
    {
        public ValueTask<IReadOnlySet<Assembly>> GetReplacedAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<Assembly>>(new HashSet<Assembly>());

        public ValueTask<IReadOnlySet<Assembly>> GetRetiredAsync(CancellationToken cancellationToken = default) =>
            ValueTask.FromResult<IReadOnlySet<Assembly>>(new HashSet<Assembly>());
    }

    private sealed class GatedPackageCatalog(IPackageAssemblyCatalog inner) : IPackageAssemblyCatalog
    {
        private int _reads;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Continue { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<PackageAssemblies>> GetPackagedAssembliesAsync(CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _reads) == 1)
            {
                Entered.TrySetResult();
                await Continue.Task.WaitAsync(cancellationToken);
            }
            return await inner.GetPackagedAssembliesAsync(cancellationToken);
        }

        public Task<PackageAssemblies?> GetPackagedAssembliesAsync(string packageId, CancellationToken cancellationToken) =>
            inner.GetPackagedAssembliesAsync(packageId, cancellationToken);
    }

    private class RuntimeCatalog : IRuntimeFeatureCatalog
    {
        private RuntimeFeatureCatalogSnapshot _snapshot;
        private readonly Channel<long> _reads = Channel.CreateUnbounded<long>();

        public RuntimeCatalog(Type feature)
        {
            _snapshot = Snapshot(1, feature);
        }

        public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => new View(_snapshot.Generation, _snapshot.RefreshedAt);

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = Volatile.Read(ref _snapshot);
            _reads.Writer.TryWrite(snapshot.Generation);
            return Task.FromResult(snapshot);
        }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CurrentSnapshot);

        public void SetWithoutNotification(params Type[] features) =>
            _snapshot = Snapshot(_snapshot.Generation + 1, features);

        protected void Set(params Type[] features) =>
            _snapshot = Snapshot(_snapshot.Generation + 1, features);

        protected RuntimeFeatureCatalogSnapshot CurrentDetailedSnapshot => _snapshot;

        public async Task WaitForReadAsync(long generation)
        {
            using var timeout = new CancellationTokenSource(Patience);
            while (await _reads.Reader.ReadAsync(timeout.Token) != generation)
            {
                // Discard earlier observations until the expected catalog generation is read.
            }
        }

        private sealed record View(long Generation, DateTimeOffset RefreshedAt) : IRuntimeFeatureCatalogSnapshot
        {
            public IReadOnlyList<RuntimeFeatureDescriptor> FeatureDescriptors => [];
        }

        public static RuntimeFeatureCatalogSnapshot Snapshot(long generation, params Type[] features)
            => SnapshotWithScanned(generation, features, []);

        public static RuntimeFeatureCatalogSnapshot SnapshotWithScanned(
            long generation,
            IReadOnlyList<Type> features,
            IReadOnlyList<Assembly> scannedAssemblies)
        {
            var descriptors = features.Select(feature => new ShellFeatureDescriptor(feature.Name) { StartupType = feature }).ToArray();
            return new RuntimeFeatureCatalogSnapshot(
                generation,
                [.. features.Select(feature => feature.Assembly).Concat(scannedAssemblies)],
                descriptors,
                descriptors.ToDictionary(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow);
        }
    }

    private sealed class StockRuntimeCatalog(Type feature) : RuntimeCatalog(feature), IRuntimeFeatureCatalogCommitSource
    {
        public event Action<RuntimeFeatureCatalogSnapshot>? SnapshotCommitted;

        public int SubscriberCount => SnapshotCommitted?.GetInvocationList().Length ?? 0;

        public void Commit(params Type[] features)
        {
            Set(features);
            SnapshotCommitted?.Invoke(CurrentDetailedSnapshot);
        }
    }

    private sealed class RecordingMembership(IClusterMembership inner) : IClusterMembership
    {
        private readonly Channel<(int Count, PublishedMemberReport Report)> _published = Channel.CreateUnbounded<(int, PublishedMemberReport)>();
        private int _publishCount;

        public int PublishCount => Volatile.Read(ref _publishCount);

        public ClusterProviderKind ProviderKind => inner.ProviderKind;

        public LocalMemberStanding GetLocalStanding() => inner.GetLocalStanding();

        public ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default) =>
            inner.ReadFleetAsync(mode, cancellationToken);

        public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
        {
            var result = await inner.PublishReportAsync(cancellationToken);
            var count = Interlocked.Increment(ref _publishCount);
            _published.Writer.TryWrite((count, result));
            return result;
        }

        public ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, mode, cancellationToken);

        public IChangeToken GetChangeToken() => inner.GetChangeToken();

        public async Task WaitForReadabilityAsync(string family, IReadOnlyList<string> versions, int minimumCount)
        {
            using var timeout = new CancellationTokenSource(Patience);
            while (true)
            {
                var published = await _published.Reader.ReadAsync(timeout.Token);
                if (published.Count >= minimumCount && published.Report.Report.Readability?.Entries
                        .SingleOrDefault(entry => entry.Family == family)?.ReadableVersions.SequenceEqual(versions) == true)
                    return;
            }
        }
    }
}
