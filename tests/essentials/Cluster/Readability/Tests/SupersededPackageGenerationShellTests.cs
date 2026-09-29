using System.Reflection;
using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using Elsa.Cluster.Core.Contracts;
using Elsa.Cluster.Core.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Nuplane.Loading;
using static Elsa.Cluster.Readability.Tests.UpgradedPackage;

namespace Elsa.Cluster.Readability.Tests;

/// <summary>
/// Spec 183's FR-021, amended 2026-09-29, through the pinned CShells itself: its registry builds, activates, reloads, drains
/// and disposes the shell generations, from its own runtime feature catalog, over a feature assembly provider that hands
/// it whichever generation of the package Nuplane has loaded, as <c>Elsa.Foundation.Host</c>'s does.
/// </summary>
/// <remarks>
/// What this pins that a container built by hand cannot: CShells raises <see cref="ShellLifecycleState.Disposed"/> before
/// it disposes the shell's provider, a failed activation disposes a provider no subscriber was told of, and the next
/// generation is built from the catalog's current snapshot, however stale.
/// </remarks>
public sealed class SupersededPackageGenerationShellTests : IAsyncDisposable
{
    private const string ShellName = "orders";
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    private readonly UpgradedPackage _package = new();
    private readonly LoadedFeatures _loaded = new();
    private readonly FailingActivation _failing = new();
    private readonly SlowDisposal _slow = new();
    private readonly Armed _failingDrainHandlers = new();
    private readonly Armed _failingDisposal = new();
    private readonly ServiceProvider _host;

    public SupersededPackageGenerationShellTests()
    {
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(new ConfigurationBuilder().Build())
            .Configure<ClusterMembershipOptions>(options => options.HostId = $"superseded-shells-{Guid.NewGuid():N}")
            .AddSingleton<IPackageAssemblyCatalog>(_package.Catalog);
        // Registered before readability's own, so every shell container creates these before this host's tracking and
        // disposes them after it, as a host-level initializer composed earlier is.
        services.AddSingleton<IShellInitializer>(_ => _failing);
        services.AddSingleton<IShellInitializer>(_ => new SlowDisposalOf(_slow));
        services.AddEfSchemaReadability();
        // Registered after readability's own, so every shell container creates it after this host's tracking and disposes
        // it before, as a service a feature composes is.
        services.AddSingleton<IShellInitializer>(_ => new FailingDisposal(_failingDisposal));
        // CShells resolves a draining shell's handlers inside its drain, and a drain whose handlers cannot be resolved faults
        // after it has disposed the shell's provider.
        services.AddTransient<IDrainHandler>(_ => _failingDrainHandlers.On ? throw new InvalidOperationException("The drain handler could not be built.") : new NoDrainWork());
        services.AddCShells(shells => shells
            .WithAssemblyProvider(_loaded)
            .ConfigureGracePeriod(TimeSpan.FromMilliseconds(100))
            .AddShell(ShellName, shell => shell.WithFeature(Feature)));
        _host = services.BuildServiceProvider();

        Loaded(_package.Previous);
    }

    private IShellRegistry Shells => _host.GetRequiredService<IShellRegistry>();

    private IRuntimeFeatureCatalog FeatureCatalog => _host.GetRequiredService<IRuntimeFeatureCatalog>();

    /// <summary>
    /// Eager activation that failed after it had initialized the feature catalog leaves no shell active and the catalog
    /// naming the previous generation. A reconcile then loads the upgrade, and the host skips refreshing the catalog
    /// because no shell is active, so the first request would build from the previous generation: it keeps counting
    /// until the catalog no longer names it.
    /// </summary>
    [Fact]
    public async Task With_no_shell_active_a_stale_feature_catalog_keeps_the_previous_generation_counting_until_it_is_refreshed()
    {
        await FailFirstActivationAsync();

        Loaded(_package.Current);
        Assert.Null(Shells.GetActive(ShellName));
        Assert.Equal(PreviousOnly, await ReadableAsync());

        await PublishAsync();
        await FeatureCatalog.RefreshAsync();
        Assert.Equal(Both, await ReadableAsync());
        // CShells raises nothing when the refresh commits, so the host watches the catalog while it alone holds the previous
        // generation back, and publishes again once it no longer does.
        await UntilAsync(async () => (await _package.PublishedAsync(_host)).SequenceEqual(Both));
    }

    /// <summary>
    /// The first request after that builds from the stale catalog, so the previous generation goes live and keeps counting,
    /// and stops only once a reload onto the upgrade has drained it.
    /// </summary>
    [Fact]
    public async Task A_shell_built_from_a_stale_feature_catalog_keeps_the_previous_generation_counting_until_a_reload_drains_it()
    {
        await FailFirstActivationAsync();
        Loaded(_package.Current);

        var stale = await Shells.GetOrActivateAsync(ShellName);
        Assert.Contains(stale.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(), feature => feature.StartupType == FeatureOf(_package.Previous));
        Assert.Equal(PreviousOnly, await ReadableAsync());

        await ReloadAsync();

        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
    }

    /// <summary>
    /// With eager activation off, a reconcile can load the upgrade before any shell was ever built, while the catalog has
    /// never been initialized: every replaced generation counts until the first build initializes it, and that build reads
    /// what Nuplane lists by then, so the host credits the upgrade as soon as it activates.
    /// </summary>
    [Fact]
    public async Task Before_any_shell_is_built_every_replaced_generation_counts_and_the_first_build_lifts_it()
    {
        Loaded(_package.Current);
        Assert.Equal(PreviousOnly, await ReadableAsync());

        var shell = await Shells.GetOrActivateAsync(ShellName);

        Assert.Contains(shell.ServiceProvider.GetRequiredService<IReadOnlyCollection<ShellFeatureDescriptor>>(), feature => feature.StartupType == FeatureOf(_package.Current));
        Assert.Equal(Both, await ReadableAsync());
    }

    /// <summary>
    /// CShells raises <see cref="ShellLifecycleState.Disposed"/> before it disposes the shell's provider, and a service the
    /// container created before this host's tracking is disposed after it, so neither says the previous generation can no
    /// longer run. The drain does: CShells completes it only after the provider's disposal has returned. Until then the
    /// previous generation keeps counting, and the host's report is published again once it stops.
    /// </summary>
    [Fact]
    public async Task A_reloaded_generation_keeps_counting_until_its_provider_has_finished_disposing()
    {
        await Shells.GetOrActivateAsync(ShellName);
        await PublishAsync();
        Loaded(_package.Current);
        _slow.Armed = true;

        var reload = await ReloadAsync(drain: false);
        await _slow.Entered.Task.WaitAsync(Patience);

        Assert.Equal(PreviousOnly, await ReadableAsync());
        Assert.Equal(PreviousOnly, await _package.PublishedAsync(_host));

        _slow.Release.SetResult();
        await reload.Drain!.WaitAsync().WaitAsync(Patience);

        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
        await UntilAsync(async () => (await _package.PublishedAsync(_host)).SequenceEqual(Both));
    }

    /// <summary>
    /// A drain that faults after CShells disposed the shell's provider - here because its drain handlers could not be
    /// built, which CShells follows by disposing the provider and only then failing the drain - leaves nothing of the
    /// generation's own running, so it stops counting, and the host's report is published again.
    /// </summary>
    [Fact]
    public async Task A_generation_whose_drain_fails_after_its_provider_was_disposed_stops_counting()
    {
        await Shells.GetOrActivateAsync(ShellName);
        await PublishAsync();
        Loaded(_package.Current);
        _failingDrainHandlers.On = true;

        var reload = await ReloadAsync(drain: false);

        await Assert.ThrowsAsync<InvalidOperationException>(() => reload.Drain!.WaitAsync().WaitAsync(Patience));
        await UntilAsync(async () => (await ReadableAsync()).SequenceEqual(Both));
        await UntilAsync(async () => (await _package.PublishedAsync(_host)).SequenceEqual(Both));
    }

    /// <summary>
    /// A drain that faults because the provider's disposal threw before it reached this host's tracking leaves the
    /// services the container created before it undisposed, and possibly running the generation, so it keeps counting.
    /// </summary>
    [Fact]
    public async Task A_generation_whose_drain_fails_before_its_provider_finished_disposing_keeps_counting()
    {
        await Shells.GetOrActivateAsync(ShellName);
        Loaded(_package.Current);
        _failingDisposal.On = true;

        var reload = await ReloadAsync(drain: false);

        await Assert.ThrowsAnyAsync<Exception>(() => reload.Drain!.WaitAsync().WaitAsync(Patience));
        _failingDisposal.On = false;
        await Task.Delay(TimeSpan.FromSeconds(1));
        Assert.Equal(PreviousOnly, await ReadableAsync());
    }

    public async ValueTask DisposeAsync()
    {
        _slow.Release.TrySetResult();
        foreach (var shell in Shells.GetActiveShells())
            await (await Shells.DrainAsync(shell)).WaitAsync().WaitAsync(Patience);
        await _host.DisposeAsync();
        _package.Dispose();
    }

    /// <summary>What Nuplane has loaded, and what the feature assembly provider therefore hands CShells.</summary>
    private void Loaded(Assembly generation)
    {
        _package.Catalog.Active = [generation];
        _loaded.Assemblies = [generation];
    }

    /// <summary>Eager activation that fails after building the shell - a database not reachable yet - as the host swallows it.</summary>
    private async Task FailFirstActivationAsync()
    {
        _failing.Armed = true;
        await Assert.ThrowsAnyAsync<Exception>(() => Shells.GetOrActivateAsync(ShellName));
        _failing.Armed = false;
        Assert.Null(Shells.GetActive(ShellName));
    }

    /// <summary>What the host does after a reconcile while a shell is active: refresh the catalog, then reload.</summary>
    private async Task<ReloadResult> ReloadAsync(bool drain = true)
    {
        await FeatureCatalog.RefreshAsync();
        var reload = await Shells.ReloadAsync(ShellName);
        Assert.Null(reload.Error);
        if (drain)
            await reload.Drain!.WaitAsync().WaitAsync(Patience);
        return reload;
    }

    private Task<IReadOnlyList<string>> ReadableAsync() => _package.ReadableAsync(_host);

    private async Task PublishAsync() => await _host.GetRequiredService<IClusterMembership>().PublishReportAsync();

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        var deadline = DateTimeOffset.UtcNow + Patience;
        while (!await condition())
        {
            Assert.True(DateTimeOffset.UtcNow < deadline, $"Not met within {Patience}.");
            await Task.Delay(TimeSpan.FromMilliseconds(20));
        }
    }

    private sealed class LoadedFeatures : IFeatureAssemblyProvider
    {
        public IReadOnlyList<Assembly> Assemblies { get; set; } = [];

        public Task<IEnumerable<Assembly>> GetAssembliesAsync(IServiceProvider serviceProvider, CancellationToken cancellationToken = default) =>
            Task.FromResult<IEnumerable<Assembly>>(Assemblies);
    }

    private sealed class FailingActivation : IShellInitializer
    {
        public bool Armed { get; set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Armed ? throw new InvalidOperationException("The database is not reachable yet.") : Task.CompletedTask;
    }

    /// <summary>Once armed, holds the disposal of a shell container's service until a test releases it.</summary>
    private sealed class SlowDisposal
    {
        public bool Armed { get; set; }

        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Armed
    {
        public bool On { get; set; }
    }

    private sealed class NoDrainWork : IDrainHandler
    {
        public Task DrainAsync(IDrainExtensionHandle extensionHandle, CancellationToken cancellationToken) => Task.CompletedTask;
    }

    /// <summary>Once armed, throws from its container's disposal, which stops the container disposing what it created before it.</summary>
    private sealed class FailingDisposal(Armed armed) : IShellInitializer, IAsyncDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public ValueTask DisposeAsync() => armed.On ? throw new InvalidOperationException("A service of the shell failed to dispose.") : ValueTask.CompletedTask;
    }

    private sealed class SlowDisposalOf(SlowDisposal slow) : IShellInitializer, IAsyncDisposable
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async ValueTask DisposeAsync()
        {
            if (!slow.Armed)
                return;
            slow.Entered.TrySetResult();
            await slow.Release.Task;
        }
    }
}
