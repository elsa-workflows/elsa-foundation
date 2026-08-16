using Elsa.Groundwork.StorePerformance.Benchmarks.Contracts;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.DependencyInjection;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Workflows.Runtime.Core.Extensions;
using Elsa.Workflows.Runtime.Core.Models;
using Groundwork.Core.Scoping;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Provider lifecycle and public runtime-store composition shared by the E3 benchmark leaves.</summary>
internal sealed class RuntimeAdapterInfrastructure : IAsyncDisposable
{
    private static readonly TimeSpan LeaseDuration = TimeSpan.FromHours(2);
    private readonly AdapterContext _context;
    private readonly List<IAsyncDisposable> _leases = [];
    private GroundworkProviderDriver? _driver;
    private GroundworkProviderRoundTripObserver? _roundTripObserver;

    private RuntimeAdapterInfrastructure(AdapterContext context) => _context = context;

    public RunRequest Request => _context.Request;
    public GroundworkProviderDriver Driver => _driver
        ?? throw new PerformanceContractException("The runtime adapter was used before its provider driver was opened.");
    public IProviderRoundTripObserver? RoundTripObserver =>
        _roundTripObserver is null ? null : new GroundworkRoundTripObserverAdapter(_roundTripObserver);
    public IReadOnlyDictionary<string, string> ObservedConfiguration { get; private set; } =
        new Dictionary<string, string>(StringComparer.Ordinal);

    public static async ValueTask<RuntimeAdapterInfrastructure> OpenAsync(
        AdapterContext context,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        var provider = context.Request.Provider;
        if (!context.Workload.RequiredProviderEvidence.ContainsKey(provider))
            throw new PerformanceContractException(
                $"Provider '{provider}' is not admitted by the frozen topology contract for '{context.Workload.Id}'.");

        var infrastructure = new RuntimeAdapterInfrastructure(context);
        try
        {
            infrastructure._driver = GroundworkProviderDriverFactory.Create(provider);
            infrastructure._roundTripObserver = GroundworkProviderRoundTripObserver.TryCreate(provider);
            infrastructure._driver.RoundTripObserver = infrastructure._roundTripObserver;
            await infrastructure.Driver.InitializeAsync(cancellationToken);
            infrastructure.RequireRequestedProvider();
            return infrastructure;
        }
        catch
        {
            await infrastructure.DisposeAsync();
            throw;
        }
    }

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await Driver.ResetPhysicalAsync(cancellationToken);
        ObservedConfiguration = await CheckpointCommitAdapter.CaptureProviderConfigurationAsync(
            Driver,
            cancellationToken);
    }

    public async ValueTask<RuntimeStoreLease<TClient>> OpenClientAsync<TClient>(
        string storageScope,
        Func<IServiceProvider, TClient> createClient,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(storageScope);
        ArgumentNullException.ThrowIfNull(createClient);
        var provider = await Driver.OpenPhysicalClientAsync(
            DocumentStoreAccess.Scoped(new StorageScope(storageScope)),
            cancellationToken);

        ServiceProvider? services = null;
        try
        {
            var boundedStore = provider.BoundedDocumentStore
                               ?? throw new PerformanceContractException(
                                   "The provider client exposes no bounded document store; runtime benchmark leaves require an applied physical target.");
            var collection = new ServiceCollection();
            collection.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            collection.AddSingleton<IPersistenceAccessContextAccessor>(GroundworkTestAccess.AccessContext(storageScope));
            collection.AddSingleton<IDocumentStore>(provider.DocumentStore);
            collection.AddSingleton(boundedStore);
            collection.AddSingleton(new RuntimeExecutionOwnershipOptions { LeaseDuration = LeaseDuration });
            collection.AddWorkflowRuntime();
            collection.AddGroundworkRuntimeStores();
            services = collection.BuildServiceProvider();
            var scope = services.CreateAsyncScope();
            var lease = new RuntimeStoreLease<TClient>(
                provider,
                services,
                scope,
                createClient(scope.ServiceProvider));
            _leases.Add(lease);
            return lease;
        }
        catch
        {
            if (services is not null)
                await services.DisposeAsync();
            await provider.DisposeAsync();
            throw;
        }
    }

    public CorrectnessEvidence Correctness(string resultDigest)
    {
        var nativePlan = _context.NativePlan;
        if (_context.Workload.RequiredNativeRoutes.Count > 0 && nativePlan is null)
            throw new PerformanceContractException(
                $"Workload '{_context.Workload.Id}' requires its staged native-plan document before correctness can be reported.");

        return new CorrectnessEvidence(
            resultDigest,
            Driver.Descriptor.ProviderVersion,
            Driver.Descriptor.Topology.Description,
            ObservedConfiguration,
            new NativePlanEvidence(
                Request.NativePlanIdentity,
                Request.NativePlanEvidenceReference,
                Request.NativePlanContentSha256,
                nativePlan?.Routes ?? []));
    }

    public async ValueTask DisposeAsync()
    {
        for (var index = _leases.Count - 1; index >= 0; index--)
            await _leases[index].DisposeAsync();
        _leases.Clear();
        if (_driver is not null)
            await _driver.DisposeAsync();
        _driver = null;
        _roundTripObserver = null;
    }

    private void RequireRequestedProvider()
    {
        if (!string.Equals(Driver.Descriptor.Topology.Description, Request.ProviderTopology, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{Request.Provider}' driver reports topology '{Driver.Descriptor.Topology.Description}', not the requested '{Request.ProviderTopology}'.");
        if (!string.Equals(Driver.Descriptor.ProviderVersion, Request.ProviderVersion, StringComparison.Ordinal))
            throw new PerformanceContractException(
                $"The '{Request.Provider}' driver reports provider version '{Driver.Descriptor.ProviderVersion}', not the requested '{Request.ProviderVersion}'.");
    }
}

internal sealed class GroundworkRoundTripObserverAdapter(GroundworkProviderRoundTripObserver observer) : IProviderRoundTripObserver
{
    public string Provider => observer.Provider;
    public string Instrumentation => observer.Instrumentation;
    public bool IsExact => observer.IsExact;
    public long Snapshot() => observer.Snapshot();
}

internal sealed record RuntimeStoreLease<TClient>(
    GroundworkProviderClient Provider,
    ServiceProvider Services,
    AsyncServiceScope Scope,
    TClient Client) : IAsyncDisposable
{
    public async ValueTask DisposeAsync()
    {
        await Scope.DisposeAsync();
        await Services.DisposeAsync();
        await Provider.DisposeAsync();
    }
}

internal sealed record BenchmarkOperation(
    string Id,
    Func<long, CancellationToken, Task> Invoke,
    Func<long, CancellationToken, Task>? Prepare = null) : IBenchmarkOperation
{
    public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
        Prepare?.Invoke(invocation, cancellationToken) ?? Task.CompletedTask;

    public Task InvokeAsync(long invocation, CancellationToken cancellationToken) => Invoke(invocation, cancellationToken);
}
