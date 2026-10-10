using System.Reflection;
using System.Runtime.Loader;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Models;
using Elsa.Cluster.Core.Options;
using Elsa.Cluster.InProcess;
using Elsa.Persistence.Schema;
using Elsa.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Primitives;
using Nuplane.Loading;
using static Elsa.Cluster.Readability.Tests.UpgradedPackage;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 183's FR-021, amended 2026-09-29: a package upgraded in place leaves its previous generation loaded in a Nuplane
/// load context for the life of the process, and that generation stops counting for the readability report only once a
/// newer generation of the same assembly is the active one and nothing that could still run it is left: no shell
/// generation whose container has not finished disposing composes from its load context, and the feature catalog the next
/// shell generation is built from does not name it.
/// </summary>
/// <remarks>
/// <para>
/// Both directions are pinned. Stopping too late looks like a healthy host that never finalizes; stopping too early
/// credits a version a generation still running cannot read, which looks like success until it reads a row it refuses.
/// </para>
/// <para>
/// A shell generation here is a container built as CShells builds one (<c>ShellProviderBuilder</c> in the pinned CShells):
/// copies of the host's registrations but the lifecycle subscribers, which CShells never copies, plus the shell and the
/// feature descriptors of the catalog snapshot it was built from; and every initializer is constructed before any runs.
/// <see cref="SupersededPackageGenerationShellTests"/> drives the same through CShells itself.
/// </para>
/// </remarks>
public sealed class SupersededPackageGenerationTests : IAsyncDisposable
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(10);

    private readonly UpgradedPackage _package = new();
    private readonly FeatureCatalog _features = new();
    private readonly RecordingLogger _log = new();
    private readonly GatedPublishes _publishes = new(open: true);
    private readonly List<ServiceProvider> _containers = [];
    private readonly IServiceCollection _services;
    private readonly ServiceProvider _host;

    public SupersededPackageGenerationTests()
    {
        // The upgrade is the active package, and the feature catalog was refreshed onto it, as a reload after a reconcile
        // leaves them; each test moves them from there.
        _package.Catalog.Active = [_package.Current];
        _features.Names(FeatureOf(_package.Current));
        _services = HostServices(_package.Catalog, _features, _log, _publishes);
        _host = Bound(_services.BuildServiceProvider());
    }

    private ISupersededAssemblySource Generations => _host.GetRequiredService<ISupersededAssemblySource>();

    private IShellLifecycleSubscriber Lifecycle => (IShellLifecycleSubscriber)Generations;

    [Fact]
    public async Task Once_the_upgrade_is_the_active_package_the_previous_generation_stops_counting() =>
        Assert.Equal(Both, await ReadableAsync());

    /// <summary>
    /// Before the catalog lists the upgrade - its package is still loading, or it failed to load - nothing says the previous
    /// generation was replaced, so both count.
    /// </summary>
    [Fact]
    public async Task Until_the_catalog_lists_the_upgrade_both_generations_count()
    {
        _package.Catalog.Active = [_package.Previous];
        Assert.Equal(PreviousOnly, await ReadableAsync());

        _package.Catalog.Active = [];
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task Removing_the_latest_package_keeps_the_proven_replacement_and_does_not_retire_the_previous_generation()
    {
        SelectActivePackage(_package.Previous);
        Shell(FeatureOf(_package.Previous));

        Assert.DoesNotContain(_package.Previous, await Generations.GetReplacedAsync());
        SelectActivePackage(_package.Current);
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());

        _package.Catalog.Active = [];

        var replaced = await Generations.GetReplacedAsync();
        Assert.Contains(_package.Previous, replaced);
        Assert.DoesNotContain(_package.Current, replaced);
        Assert.Empty(await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task A_pinned_replacement_observed_by_readability_remains_activation_evidence_after_removal()
    {
        Shell(FeatureOf(_package.Previous));
        Assert.DoesNotContain(_package.Previous, await Generations.GetRetiredAsync());

        _package.Catalog.Active = [];

        var replaced = await Generations.GetReplacedAsync();
        Assert.Contains(_package.Previous, replaced);
        Assert.DoesNotContain(_package.Current, replaced);
        Assert.Empty(await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task An_initially_empty_catalog_does_not_prove_that_either_generation_was_replaced()
    {
        _package.Catalog.Active = [];

        Assert.Empty(await Generations.GetReplacedAsync());
        Assert.Empty(await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task Reselecting_a_historically_replaced_generation_makes_it_eligible_after_removal()
    {
        SelectActivePackage(_package.Previous);
        Shell(FeatureOf(_package.Previous));
        SelectActivePackage(_package.Current);
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());

        SelectActivePackage(_package.Previous);

        var reselected = await Generations.GetReplacedAsync();
        Assert.DoesNotContain(_package.Previous, reselected);
        Assert.Contains(_package.Current, reselected);

        _package.Catalog.Active = [];

        var removed = await Generations.GetReplacedAsync();
        Assert.DoesNotContain(_package.Previous, removed);
        Assert.Contains(_package.Current, removed);
        Assert.Empty(await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task A_catalog_read_failure_is_conservative_and_a_later_success_recovers_replacement_history()
    {
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());

        _package.Catalog.ReadError = new IOException("The package catalog is temporarily unavailable.");
        Assert.Empty(await Generations.GetReplacedAsync());
        Assert.Empty(await Generations.GetRetiredAsync());

        _package.Catalog.ReadError = null;
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());
        _package.Catalog.Active = [];
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());
        Assert.DoesNotContain(_package.Current, await Generations.GetReplacedAsync());
        Assert.Empty(await Generations.GetRetiredAsync());
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    [Fact]
    public async Task A_queued_catalog_read_cannot_overtake_or_cancel_the_in_flight_snapshot()
    {
        var catalog = _package.Catalog;
        var gate = catalog.BlockNextRead();
        using var cancellation = new CancellationTokenSource();
        Task<IReadOnlySet<Assembly>>? firstRead = null;
        Task<IReadOnlySet<Assembly>>? queuedRead = null;

        try
        {
            firstRead = Generations.GetReplacedAsync().AsTask();
            await gate.Captured.Task.WaitAsync(Patience);
            Assert.Equal(1, catalog.ReadCount);

            SelectActivePackage(_package.Previous);
            var queued = Generations.GetReplacedAsync(cancellation.Token).AsTask();
            queuedRead = queued;
            Assert.Equal(1, catalog.ReadCount);
            Assert.False(firstRead.IsCompleted);

            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () => await queued.WaitAsync(Patience));
            Assert.Equal(1, catalog.ReadCount);
            Assert.False(firstRead.IsCompleted);

            gate.Open();
            Assert.Contains(_package.Previous, await firstRead.WaitAsync(Patience));

            var reselected = await Generations.GetReplacedAsync();
            Assert.DoesNotContain(_package.Previous, reselected);
            Assert.Contains(_package.Current, reselected);

            catalog.Active = [];
            var removed = await Generations.GetReplacedAsync();
            Assert.DoesNotContain(_package.Previous, removed);
            Assert.Contains(_package.Current, removed);
        }
        finally
        {
            gate.Open();
            try
            {
                if (firstRead is not null)
                    await firstRead.WaitAsync(Patience);
            }
            finally
            {
                if (queuedRead is not null)
                {
                    try
                    {
                        await queuedRead.WaitAsync(Patience);
                    }
                    catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                    {
                    }
                }
            }
        }
    }

    /// <summary>
    /// A shell generation is counted from before its first initializer runs - a module's migrator or finalization gate
    /// reads rows there - with no lifecycle notification yet, and until its container has finished disposing, whose end
    /// has the host's report published again so a gate waiting on it evaluates at once. The guard, which judges the
    /// generation about to be built, already sees only the upgrade.
    /// </summary>
    [Fact]
    public async Task A_shell_generation_counts_from_before_its_first_initializer_until_its_container_is_disposed_and_that_republishes()
    {
        var previous = Shell(FeatureOf(_package.Previous));
        await PublishAsync();

        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());
        Assert.DoesNotContain(_package.Previous, await Generations.GetRetiredAsync());

        await previous.DisposeAsync();

        Assert.Equal(Both, await ReadableAsync());
        await UntilAsync(async () => (await PublishedAsync()).SequenceEqual(Both));
    }

    /// <summary>
    /// The republish a released generation asks for is a membership-store write, and CShells disposes a shell's provider
    /// under its own locks, so the container's disposal marks the generation released and returns: the publish runs after
    /// it, once, however slow it is.
    /// </summary>
    [Fact]
    public async Task A_container_disposal_does_not_wait_for_the_republish_it_asks_for()
    {
        var publishes = new GatedPublishes(open: false);
        var services = HostServices(_package.Catalog, _features, _log, publishes);
        await using var host = Bound(services.BuildServiceProvider());
        var previous = Shell(FeatureOf(_package.Previous), from: services);

        // On a thread of its own, so a disposal that blocks on the publish fails here rather than hanging the run.
        await Task.Run(() => previous.DisposeAsync().AsTask()).WaitAsync(Patience);

        Assert.Equal(Both, await _package.ReadableAsync(host));
        await publishes.Started.Task.WaitAsync(Patience);
        publishes.Release.SetResult();
        await UntilAsync(async () => (await _package.PublishedAsync(host)).SequenceEqual(Both));
        Assert.Equal(1, publishes.Count);
    }

    [Fact]
    public async Task A_shell_generation_that_runs_only_the_upgrade_holds_nothing_back()
    {
        Shell(FeatureOf(_package.Current));

        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// A shell whose container is disposed while its drain is still under way is released only once that drain completes,
    /// which CShells completes only after the shell's whole provider has been disposed: an earlier service of the container
    /// may still be disposing, and running the previous generation, when this host's tracking is disposed.
    /// </summary>
    [Fact]
    public async Task A_drained_shell_generation_counts_until_its_drain_completes()
    {
        var shell = new FakeShell();
        var container = Shell(shell, FeatureOf(_package.Previous));
        var drain = new FakeDrain();
        shell.Drain = drain;
        await PublishAsync();
        await AdvanceAsync(shell, ShellLifecycleState.Active, ShellLifecycleState.Deactivating, ShellLifecycleState.Draining, ShellLifecycleState.Drained, ShellLifecycleState.Disposed);

        await container.DisposeAsync();
        Assert.Equal(PreviousOnly, await ReadableAsync());

        drain.Complete(shell.Descriptor);
        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
        await UntilAsync(async () => (await PublishedAsync()).SequenceEqual(Both));
    }

    /// <summary>
    /// A drain that fails after the container disposed this host's tracking, which it does after everything it created for
    /// the generation, leaves nothing of the generation's own running, so it is released, and the report republished.
    /// </summary>
    [Fact]
    public async Task A_shell_generation_whose_drain_fails_after_its_container_disposed_it_stops_counting()
    {
        var (shell, container, drain) = await DrainingAsync();
        await PublishAsync();
        await container.DisposeAsync();
        Assert.Equal(PreviousOnly, await ReadableAsync());

        drain.Fail();

        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
        await UntilAsync(async () => (await PublishedAsync()).SequenceEqual(Both));
        Assert.Contains(_log.Warnings, warning => warning.Exception is InvalidOperationException);
    }

    /// <summary>
    /// A drain that fails before the container disposed this host's tracking may have left the provider partly disposed
    /// and still running the generation, so it keeps counting until the container has disposed it.
    /// </summary>
    [Fact]
    public async Task A_shell_generation_whose_drain_fails_before_its_container_disposed_it_keeps_counting_until_that_is_done()
    {
        var (_, container, drain) = await DrainingAsync();

        drain.Fail();

        await UntilAsync(() => Task.FromResult(_log.Warnings.Any(warning => warning.Exception is InvalidOperationException)));
        Assert.Equal(PreviousOnly, await ReadableAsync());

        await container.DisposeAsync();
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>A shell generation whose features cannot be read may be running anything, so nothing it could run retires.</summary>
    [Fact]
    public async Task A_shell_generation_whose_features_cannot_be_read_keeps_every_replaced_generation_counting()
    {
        var shell = Shell(_ => { });

        Assert.Equal(PreviousOnly, await ReadableAsync());

        await shell.DisposeAsync();
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// Whatever reading a shell generation's features throws, the generation is still tracked, and pins every replaced
    /// generation: a read that fails is no evidence of what it composes.
    /// </summary>
    [Fact]
    public async Task A_shell_generation_whose_features_throw_when_read_keeps_every_replaced_generation_counting()
    {
        var shell = Shell(services => services.AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>(
            _ => throw new InvalidOperationException("The feature descriptors could not be built.")));

        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Contains(_log.Warnings, warning => warning.Exception is InvalidOperationException);

        await shell.DisposeAsync();
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// A graph context Nuplane keeps after an upgrade can still serve an unchanged package beside the replaced one. A shell
    /// generation composing a feature from that unchanged package binds the replaced one from the same context, so the
    /// whole context is pinned, not only the assemblies that were themselves replaced.
    /// </summary>
    [Fact]
    public async Task A_replaced_assembly_in_the_load_context_of_a_live_feature_keeps_counting()
    {
        var sibling = _package.Load(
            SyntheticSchemaFamilies.Image($"Upgraded.Sibling{Guid.NewGuid():N}", [], [], [new SyntheticFeature("Upgraded.SiblingFeature")]),
            beside: _package.Previous);
        _package.Catalog.Active = [_package.Current, sibling];
        var shell = Shell(sibling.GetType("Upgraded.SiblingFeature", throwOnError: true)!);

        Assert.Equal(PreviousOnly, await ReadableAsync());

        await shell.DisposeAsync();
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// With no shell active - eager activation turned off, or failed after it had initialized the feature catalog - a
    /// reconcile loads the upgrade, and a host that skips refreshing the catalog while no shell is active, as
    /// <c>Elsa.Foundation.Host</c> does, leaves it naming the previous generation. The first request builds from it, so
    /// the previous generation keeps counting until the catalog no longer names it.
    /// </summary>
    [Fact]
    public async Task While_the_feature_catalog_names_the_previous_generation_it_keeps_counting_with_no_shell_active()
    {
        _features.Names(FeatureOf(_package.Previous));
        Assert.Equal(PreviousOnly, await ReadableAsync());

        _features.Names(FeatureOf(_package.Current));
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// CShells raises nothing when its catalog commits a refresh, so a refresh that lifts the last pin on the previous
    /// generation would leave the published report narrowed until some other publish came. A refresh that still names the
    /// previous generation lifts nothing, and publishes nothing.
    /// </summary>
    [Fact]
    public async Task A_feature_catalog_refresh_that_lifts_the_last_pin_republishes_and_one_that_does_not_publishes_nothing()
    {
        _features.Names(FeatureOf(_package.Previous));
        await PublishAsync();

        _features.Names(FeatureOf(_package.Previous));
        await Task.Delay(NuplanePackageGenerations.CatalogWatchInterval * 3);
        Assert.Equal(PreviousOnly, await PublishedAsync());
        Assert.Equal(1, _publishes.Count);

        _features.Names(FeatureOf(_package.Current));
        await UntilAsync(async () => (await PublishedAsync()).SequenceEqual(Both));
        Assert.Equal(2, _publishes.Count);
    }

    /// <summary>The first build initializes the catalog; that lifts every pin its absence held, and republishes too.</summary>
    [Fact]
    public async Task Initializing_the_feature_catalog_republishes_once_it_lifts_the_last_pin()
    {
        _features.Uninitialize();
        await PublishAsync();
        Assert.Equal(PreviousOnly, await PublishedAsync());

        _features.Names(FeatureOf(_package.Current));

        await UntilAsync(async () => (await PublishedAsync()).SequenceEqual(Both));
    }

    /// <summary>A catalog that lists the replaced assembly among those it scanned may compose from it too.</summary>
    [Fact]
    public async Task A_feature_catalog_that_scanned_the_previous_generation_keeps_it_counting()
    {
        _features.Names([FeatureOf(_package.Current)], scanned: [_package.Previous]);

        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    /// <summary>
    /// A catalog never refreshed is refreshed by the first build from whatever Nuplane lists then, and a build may be
    /// reading it already, so every replaced generation counts until it has been; readability never refreshes it itself.
    /// </summary>
    [Fact]
    public async Task Before_the_feature_catalog_is_initialized_every_replaced_generation_counts()
    {
        _features.Uninitialize();
        Assert.Equal(PreviousOnly, await ReadableAsync());

        _features.Names(FeatureOf(_package.Current));
        Assert.Equal(Both, await ReadableAsync());
    }

    [Fact]
    public async Task A_feature_catalog_that_cannot_be_read_keeps_every_replaced_generation_counting()
    {
        _features.Unreadable = true;

        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Contains(_log.Warnings, warning => warning.Exception is IOException);
    }

    /// <summary>
    /// A shell whose container never constructed this host's tracking - a feature removed it - is still tracked, from its
    /// first lifecycle notification, and since nothing then says when its provider is disposed, only a completed drain
    /// releases it.
    /// </summary>
    [Fact]
    public async Task A_shell_generation_this_host_did_not_see_initialize_is_tracked_from_its_first_notification_until_a_drain_completes()
    {
        var shell = new FakeShell();
        var container = Container(shell, services =>
        {
            services.AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>(Descriptors(FeatureOf(_package.Previous)));
            services.RemoveAll<IShellInitializer>();
        });
        Assert.Equal(Both, await ReadableAsync());

        await AdvanceAsync(shell, ShellLifecycleState.Active);
        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Single(_log.Warnings);

        await container.DisposeAsync();
        Assert.Equal(PreviousOnly, await ReadableAsync());

        var drain = new FakeDrain();
        shell.Drain = drain;
        await AdvanceAsync(shell, ShellLifecycleState.Draining);
        drain.Complete(shell.Descriptor);
        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
    }

    /// <summary>Only a context Nuplane created can hold a replaced generation: a same-named copy anywhere else keeps counting.</summary>
    [Fact]
    public async Task A_previous_generation_outside_a_nuplane_context_keeps_counting()
    {
        var elsewhere = new AssemblyLoadContext($"not-nuplane-{Guid.NewGuid():N}", isCollectible: true);
        try
        {
            elsewhere.LoadFromAssemblyPath(_package.Previous.Location);

            Assert.Contains(_package.Previous, await Generations.GetRetiredAsync());
            Assert.Equal(PreviousOnly, await ReadableAsync());
        }
        finally
        {
            elsewhere.Unload();
        }
    }

    /// <summary>
    /// Nuplane leaves an assembly its shared-assembly policy matches out of the catalog, where it once listed the host's
    /// copy: a copy of one that a package context nevertheless holds - a package whose share did not match, say - is
    /// never named by the catalog, so it is never judged replaced, and no version it could read is credited away.
    /// </summary>
    [Fact]
    public async Task A_package_private_copy_of_an_assembly_the_catalog_leaves_out_is_never_replaced()
    {
        var hostCopy = typeof(IClusterMembership).Assembly;
        var copy = _package.Load(await File.ReadAllBytesAsync(hostCopy.Location), $"{hostCopy.GetName().Name}.dll");

        Assert.NotSame(hostCopy, copy);
        Assert.Equal(hostCopy.GetName().Name, copy.GetName().Name);
        Assert.DoesNotContain(copy, await Generations.GetReplacedAsync());
        Assert.DoesNotContain(copy, await Generations.GetRetiredAsync());
    }

    [Fact]
    public async Task A_host_without_nuplane_counts_every_generation()
    {
        await using var host = Bound(HostServices(catalog: null, features: null, _log).BuildServiceProvider());

        Assert.Empty(await host.GetRequiredService<ISupersededAssemblySource>().GetRetiredAsync());
        Assert.Equal(PreviousOnly, await _package.ReadableAsync(host));
    }

    /// <summary>
    /// CShells binds the host's container first; a later binding - a shell container resolving the lifecycle subscriber,
    /// say - must not move it onto a container whose copy of Nuplane's loader has loaded nothing.
    /// </summary>
    [Fact]
    public async Task The_first_binding_is_kept()
    {
        ((NuplanePackageGenerations)Generations).BindTo(new ServiceCollection().BuildServiceProvider());

        Assert.Contains(_package.Previous, await Generations.GetReplacedAsync());
    }

    /// <summary>
    /// Nuplane's load contexts are matched by the name of the assembly that defines them, so composing readability takes
    /// no Nuplane runtime; this fails first if the pinned Nuplane.Loading moves or renames the contexts it loads packages
    /// into, which would otherwise leave every generation counting without a word.
    /// </summary>
    [Theory]
    [InlineData("Nuplane.Loading.PackageAssemblyLoadContext")]
    [InlineData("Nuplane.Loading.PackageGraphLoadContext")]
    [InlineData("Nuplane.Loading.HostIntegratedPackageGraphLoadContext")]
    public void The_load_contexts_matched_are_the_ones_the_pinned_Nuplane_Loading_defines(string context)
    {
        var nuplaneLoading = typeof(PackageAssemblyLoadContext).Assembly;

        Assert.Equal(NuplanePackageGenerations.NuplaneLoadingAssembly, nuplaneLoading.GetName().Name);
        Assert.True(typeof(AssemblyLoadContext).IsAssignableFrom(nuplaneLoading.GetType(context, throwOnError: true)));
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var container in _containers)
            await container.DisposeAsync();
        await _host.DisposeAsync();
        _package.Dispose();
    }

    /// <summary>
    /// A host composed as <c>Elsa.Foundation.Host</c> composes it, with Nuplane's catalog and CShells' feature catalog when
    /// they are given.
    /// </summary>
    /// <remarks>Its membership is the in-process default, whose publishes pass through <paramref name="publishes"/> when it is given.</remarks>
    private static IServiceCollection HostServices(IPackageAssemblyCatalog? catalog, IRuntimeFeatureCatalog? features, RecordingLogger log, GatedPublishes? publishes = null)
    {
        var services = new ServiceCollection()
            .Configure<ClusterMembershipOptions>(options => options.HostId = $"superseded-{Guid.NewGuid():N}")
            .AddSingleton<ILoggerFactory>(new RecordingLoggerFactory(log));
        if (catalog is not null)
            services.AddSingleton(catalog);
        if (features is not null)
            services.AddSingleton(features);
        if (publishes is not null)
            services.AddSingleton<IClusterMembership>(provider => new GatedMembership(ActivatorUtilities.CreateInstance<InProcessClusterMembership>(provider), publishes));
        return services.AddEfSchemaReadability();
    }

    /// <summary>Binds <paramref name="host"/> the way CShells binds it: by resolving its lifecycle subscribers.</summary>
    private static ServiceProvider Bound(ServiceProvider host)
    {
        _ = host.GetServices<IShellLifecycleSubscriber>().ToArray();
        return host;
    }

    private ServiceProvider Shell(params Type[] features) => Shell(new FakeShell(), features);

    /// <summary>A shell generation of <paramref name="feature"/> copied from <paramref name="from"/>, another host's registrations.</summary>
    private ServiceProvider Shell(Type feature, IServiceCollection from) =>
        Shell(new FakeShell(), services => services.AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>(Descriptors(feature)), from);

    /// <summary>A shell generation of the previous generation's feature, active and then draining, with its drain attached.</summary>
    private async Task<(FakeShell Shell, ServiceProvider Container, FakeDrain Drain)> DrainingAsync()
    {
        var shell = new FakeShell();
        var container = Shell(shell, FeatureOf(_package.Previous));
        var drain = new FakeDrain();
        shell.Drain = drain;
        await AdvanceAsync(shell, ShellLifecycleState.Active, ShellLifecycleState.Draining);
        return (shell, container, drain);
    }

    private ServiceProvider Shell(FakeShell shell, params Type[] features) =>
        Shell(shell, services => services.AddSingleton<IReadOnlyCollection<ShellFeatureDescriptor>>(Descriptors(features)));

    private ServiceProvider Shell(Action<IServiceCollection> describe) => Shell(new FakeShell(), describe);

    /// <summary>A shell generation's container, with every initializer constructed, as CShells constructs them all before it runs any.</summary>
    private ServiceProvider Shell(FakeShell shell, Action<IServiceCollection> describe, IServiceCollection? from = null)
    {
        var container = Container(shell, describe, from);
        _ = container.GetServices<IShellInitializer>().ToArray();
        return container;
    }

    private ServiceProvider Container(FakeShell shell, Action<IServiceCollection> describe, IServiceCollection? from = null)
    {
        var services = new ServiceCollection();
        foreach (var descriptor in (from ?? _services).Where(descriptor => descriptor.ServiceType != typeof(IShellLifecycleSubscriber)))
            services.Add(descriptor);
        services.AddSingleton<IShell>(shell);
        describe(services);
        var container = services.BuildServiceProvider();
        shell.ServiceProvider = container;
        _containers.Add(container);
        return container;
    }

    private static IReadOnlyCollection<ShellFeatureDescriptor> Descriptors(params Type[] features) =>
        [.. features.Select(feature => new ShellFeatureDescriptor(feature.Name) { StartupType = feature })];

    private async Task AdvanceAsync(FakeShell shell, params ShellLifecycleState[] states)
    {
        foreach (var state in states)
        {
            var previous = shell.State;
            shell.State = state;
            await Lifecycle.OnStateChangedAsync(shell, previous, state);
        }
    }

    private async Task PublishAsync() => await _host.GetRequiredService<IClusterMembership>().PublishReportAsync();

    private Task<IReadOnlyList<string>> ReadableAsync() => _package.ReadableAsync(_host);

    private void SelectActivePackage(Assembly generation)
    {
        _package.Catalog.Active = [generation];
        _features.Names(FeatureOf(generation));
    }

    private Task<IReadOnlyList<string>> PublishedAsync() => _package.PublishedAsync(_host);


    /// <summary>What happens on a drain's completion runs after it, not in it.</summary>
    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Not met within {Patience}.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    /// <summary>A shell generation as a lifecycle subscriber and its own container see it.</summary>
    private sealed class FakeShell : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create("default", 1);

        public ShellLifecycleState State { get; set; } = ShellLifecycleState.Initializing;

        public IServiceProvider ServiceProvider { get; set; } = null!;

        public IDrainOperation? Drain { get; set; }

        public IShellScope BeginScope() => throw new NotSupportedException();
    }

    /// <summary>A drain whose completion a test decides; CShells completes one only after the shell's provider is disposed.</summary>
    private sealed class FakeDrain : IDrainOperation
    {
        private readonly TaskCompletionSource<DrainResult> _completion = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public DrainStatus Status => DrainStatus.Completed;

        public DateTimeOffset? Deadline => null;

        public Task<DrainResult> WaitAsync(CancellationToken cancellationToken = default) => _completion.Task.WaitAsync(cancellationToken);

        public Task ForceAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public void Complete(ShellDescriptor shell) => _completion.SetResult(new DrainResult(shell, DrainStatus.Completed, TimeSpan.Zero, 0, []));

        public void Fail() => _completion.SetException(new InvalidOperationException("The shell's provider threw while it was disposed."));
    }

    /// <summary>
    /// What CShells' runtime feature catalog exposes: the current snapshot a build reads, which throws until the catalog is
    /// initialized. It refuses to be refreshed or initialized: readability must never do either.
    /// </summary>
    private sealed class FeatureCatalog : IRuntimeFeatureCatalog
    {
        private RuntimeFeatureCatalogSnapshot? _snapshot;
        private long _generation;

        public bool Unreadable { get; set; }

        public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => Current is { } snapshot ? new View(snapshot.Generation, snapshot.RefreshedAt) : throw new InvalidOperationException("The runtime feature catalog has not been initialized.");

        public void Names(params Type[] features) => Names(features, scanned: []);

        public void Names(Type[] features, Assembly[] scanned)
        {
            var descriptors = Descriptors(features);
            _snapshot = new RuntimeFeatureCatalogSnapshot(
                ++_generation,
                [.. features.Select(feature => feature.Assembly).Concat(scanned)],
                descriptors,
                descriptors.ToDictionary(descriptor => descriptor.Id, StringComparer.OrdinalIgnoreCase),
                DateTimeOffset.UtcNow);
        }

        public void Uninitialize() => _snapshot = null;

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(Current ?? throw new InvalidOperationException("Reading the detailed snapshot of an uninitialized catalog would initialize it."));

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Readability must not initialize the catalog.");

        public Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException("Readability must not refresh the catalog.");

        private RuntimeFeatureCatalogSnapshot? Current => Unreadable ? throw new IOException("The feature catalog could not be read.") : _snapshot;

        private sealed record View(long Generation, DateTimeOffset RefreshedAt) : IRuntimeFeatureCatalogSnapshot
        {
            public IReadOnlyList<RuntimeFeatureDescriptor> FeatureDescriptors => [];
        }
    }

    /// <summary>Counts a membership's publishes, and holds them until a test lets them through unless it is <paramref name="open"/>.</summary>
    private sealed class GatedPublishes
    {
        private int _count;

        public GatedPublishes(bool open)
        {
            if (open)
                Release.SetResult();
        }

        public int Count => Volatile.Read(ref _count);

        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task PassAsync()
        {
            Interlocked.Increment(ref _count);
            Started.TrySetResult();
            await Release.Task;
        }
    }

    /// <summary>The in-process membership, whose publishes wait for <paramref name="publishes"/>: a store write that is slow.</summary>
    private sealed class GatedMembership(IClusterMembership inner, GatedPublishes publishes) : IClusterMembership
    {
        public ClusterProviderKind ProviderKind => inner.ProviderKind;

        public LocalMemberStanding GetLocalStanding() => inner.GetLocalStanding();

        public ValueTask<FleetView> ReadFleetAsync(FleetReadMode mode, CancellationToken cancellationToken = default) => inner.ReadFleetAsync(mode, cancellationToken);

        public async ValueTask<PublishedMemberReport> PublishReportAsync(CancellationToken cancellationToken = default)
        {
            await publishes.PassAsync();
            return await inner.PublishReportAsync(cancellationToken);
        }

        public ValueTask<MemberQueryAnswer> QueryAsync(MemberQuery query, FleetReadMode mode, CancellationToken cancellationToken = default) =>
            inner.QueryAsync(query, mode, cancellationToken);

        public IChangeToken GetChangeToken() => inner.GetChangeToken();
    }

    private sealed class RecordingLoggerFactory(RecordingLogger log) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => log;

        public void AddProvider(ILoggerProvider provider) => throw new NotSupportedException();

        public void Dispose()
        {
        }
    }
}
