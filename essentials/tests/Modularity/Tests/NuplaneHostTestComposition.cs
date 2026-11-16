using CShells.DependencyInjection;
using CShells.Features;
using CShells.Lifecycle;
using CShells.Nuplane;
using FoundationHost = Elsa.Foundation.Host.Shells;
using WorkbenchHost = Elsa.Workbench;
using Elsa.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nuplane.Abstractions;
using Nuplane.Events;
using Nuplane.Observability;
using Xunit;

namespace Elsa.Modularity.Tests;

internal static class NuplaneHostTestComposition
{
    public static ConfigurationRoot CreateConfiguration(string? reload) => (ConfigurationRoot)new ConfigurationBuilder()
        .AddInMemoryCollection(new Dictionary<string, string?>
        {
            [FoundationHost.NuplaneIntegrationOptionsSetup.ReloadOnPackageChangeKey] = reload
        })
        .Build();

    public static ServiceProvider BuildProfile(IConfigurationRoot configuration, bool foundation, ILoggerProvider? logger = null)
    {
        var services = new ServiceCollection();
        services.AddLogging(logging =>
        {
            if (logger is not null)
                logging.AddProvider(logger);
        });
        services.AddSingleton<IConfiguration>(configuration);
        AddProfile(services, configuration, foundation);
        return services.BuildServiceProvider();
    }

    public static NuplaneHostTestFixture CreateAdapter(bool foundation, params string[] activeShells) => new(foundation, activeShells);

    private static void AddProfile(IServiceCollection services, IConfigurationRoot configuration, bool foundation)
    {
        if (foundation)
            services.ConfigureOptions<FoundationHost.NuplaneIntegrationOptionsSetup>();
        else
            services.ConfigureOptions<WorkbenchHost.NuplaneIntegrationOptionsSetup>();
        services.AddSingleton<IOptionsChangeTokenSource<NuplaneIntegrationOptions>>(
            new ConfigurationChangeTokenSource<NuplaneIntegrationOptions>(Options.DefaultName, configuration.GetSection("Elsa:Shells")));
    }

    internal sealed class NuplaneHostTestFixture : IDisposable
    {
        private static readonly ResolvedPackage Notes = new("Elsa.Samples.Nuplane.Notes", "1.1.0", "local-packages", "/packages/notes", DateTimeOffset.UnixEpoch, "feed");
        private readonly ServiceProvider _services;

        public NuplaneHostTestFixture(bool foundation, IEnumerable<string> activeShells)
        {
            Configuration = (ConfigurationRoot)new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["CShells:Shells:default:Name"] = "default",
                    ["CShells:Shells:tenant-a:Name"] = "tenant-a"
                })
                .Build();
            Catalog = new RefreshCountingCatalog();
            Registry = new ScriptedShellRegistry(activeShells);
            Logger = new CapturingLogger();
            Following = new FollowingObserver();

            var services = new ServiceCollection();
            services.AddLogging(logging => logging.AddProvider(Logger));
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddSingleton<IRuntimeFeatureCatalog>(Catalog);
            services.AddSingleton<IShellRegistry>(Registry);
            AddProfile(services, Configuration, foundation);
            services.AddCShells().WithNuplaneFeatureDiscovery();
            services.AddSingleton(Following);
            services.AddSingleton<INuplaneObserver>(provider => provider.GetRequiredService<FollowingObserver>());
            _services = services.BuildServiceProvider();

            Observer = _services.GetServices<INuplaneObserver>().Single(observer => observer is IShellGenerationBuildParticipant);
            Participant = Assert.IsAssignableFrom<IShellGenerationBuildParticipant>(Observer);
            Dispatcher = new ObserverEventDispatcher(
                _services.GetServices<INuplaneObserver>(),
                new ReconciliationLogger(new Logger<ReconciliationLogger>(Logger)));
        }

        public ConfigurationRoot Configuration { get; }
        public RefreshCountingCatalog Catalog { get; }
        public ScriptedShellRegistry Registry { get; }
        public CapturingLogger Logger { get; }
        public FollowingObserver Following { get; }
        public INuplaneObserver Observer { get; }
        public IShellGenerationBuildParticipant Participant { get; }
        public IObserverEventDispatcher Dispatcher { get; }

        public Task DispatchAsync(PackageChangeSet changes, IReadOnlyList<ResolvedPackage>? appliedPackages = null) =>
            Dispatcher.PublishReconciledAsync(changes, appliedPackages ?? [Notes], CancellationToken.None);

        public void SetReloadOnPackageChange(bool? value)
        {
            Configuration[FoundationHost.NuplaneIntegrationOptionsSetup.ReloadOnPackageChangeKey] = value?.ToString();
            Configuration.Reload();
        }

        public void Dispose()
        {
            _services.Dispose();
            Configuration.Dispose();
        }
    }

    internal sealed class RefreshCountingCatalog : IRuntimeFeatureCatalog
    {
        private readonly FakeRuntimeFeatureCatalog _snapshots = new();
        private readonly object _operationsLock = new();
        private string[] _operations = [];
        private int _refreshes;
        private RefreshGate? _nextRefreshGate;

        public int Refreshes => Volatile.Read(ref _refreshes);
        public Exception? Failure { get; set; }
        public IReadOnlyList<string> Operations { get { lock (_operationsLock) return [.. _operations]; } }

        public RefreshGate BlockNextRefresh()
        {
            var gate = new RefreshGate();
            if (Interlocked.CompareExchange(ref _nextRefreshGate, gate, null) is not null)
                throw new InvalidOperationException("A refresh gate is already pending.");
            return gate;
        }

        public async Task<IRuntimeFeatureCatalogSnapshot> RefreshAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _refreshes);
            Record("refresh");
            var gate = Interlocked.Exchange(ref _nextRefreshGate, null);
            if (gate is not null)
            {
                gate.Entered.TrySetResult();
                await gate.Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }

            if (Failure is { } failure)
                throw failure;
            return _snapshots.CurrentSnapshot;
        }

        public IRuntimeFeatureCatalogSnapshot CurrentSnapshot => _snapshots.CurrentSnapshot;

        public Task<RuntimeFeatureCatalogSnapshot> GetSnapshotAsync(CancellationToken cancellationToken = default)
        {
            Record("snapshot");
            return _snapshots.GetSnapshotAsync(cancellationToken);
        }

        public Task EnsureInitializedAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();

        private void Record(string operation)
        {
            lock (_operationsLock)
                _operations = [.. _operations, operation];
        }
    }

    internal sealed class RefreshGate
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    internal sealed class ScriptedShellRegistry : IShellRegistry
    {
        private readonly HashSet<string> _active;
        private int _reloads;

        public ScriptedShellRegistry(IEnumerable<string> activeShells) => _active = new(activeShells, StringComparer.Ordinal);

        public void Deactivate(string name) => _active.Remove(name);

        public int Reloads => Volatile.Read(ref _reloads);
        public IReadOnlyList<ReloadResult> Results { get; set; } = [new ReloadResult("default", null, null, null)];
        public Exception? Failure { get; set; }

        public IShell? GetActive(string name) => _active.Contains(name) ? new StubShell(name) : null;

        public Task<IReadOnlyList<ReloadResult>> ReloadActiveAsync(ReloadOptions? options = null, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _reloads);
            return Failure is null ? Task.FromResult(Results) : Task.FromException<IReadOnlyList<ReloadResult>>(Failure);
        }

        public Task<IShell> GetOrActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShell> ActivateAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ReloadResult> ReloadAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IDrainOperation> DrainAsync(IShell shell, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task UnregisterBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ProvidedBlueprint?> GetBlueprintAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IShellBlueprintManager?> GetManagerAsync(string name, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<ShellPage> ListAsync(ShellListQuery query, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public IReadOnlyCollection<IShell> GetAll(string name) => [];
        public IReadOnlyCollection<IShell> GetActiveShells() => [.. _active.Select(name => (IShell)new StubShell(name))];
        public void Subscribe(IShellLifecycleSubscriber subscriber) { }
        public void Unsubscribe(IShellLifecycleSubscriber subscriber) { }
    }

    internal sealed class StubShell(string name) : IShell
    {
        public ShellDescriptor Descriptor { get; } = ShellDescriptor.Create(name, 1);
        public ShellLifecycleState State => ShellLifecycleState.Active;
        public IServiceProvider ServiceProvider => throw new NotSupportedException();
        public IShellScope BeginScope() => throw new NotSupportedException();
        public IDrainOperation? Drain => null;
    }

    internal sealed class FollowingObserver : INuplaneObserver
    {
        private int _reconciledCalls;

        public int ReconciledCalls => Volatile.Read(ref _reconciledCalls);
        public Task OnPackagesChangingAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;
        public Task OnPackagesChangedAsync(PackageChangeSet changeSet, CancellationToken ct) => Task.CompletedTask;
        public Task OnPackagesReconciledAsync(PackageChangeSet changeSet, IReadOnlyList<ResolvedPackage> appliedPackages, CancellationToken ct)
        {
            Interlocked.Increment(ref _reconciledCalls);
            return Task.CompletedTask;
        }
        public Task OnPackageFailedAsync(string packageId, Exception exception, CancellationToken ct) => Task.CompletedTask;
    }
}
