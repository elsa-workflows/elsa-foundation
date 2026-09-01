using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Groundwork.Store;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 trigger-binding lookup adapter. The workload owns the frozen correctness and
/// bounded operation definitions; this leaf composes the public trigger-binding and executable-source
/// reference contracts over two isolated persistence scopes and records provider-native commands.
/// </summary>
internal sealed class TriggerBindingStimulusLookupAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IRuntimeTriggerBindingStimulusLookupWorkloadAdapter
{
    internal const string PhysicalForm = "linked-executable-source-reference-index";
    private const string PrimaryPersistenceScope = "tenant-primary";
    private const string SecondaryPersistenceScope = "tenant-secondary";

    private RuntimeStoreComposition? primaryComposition;
    private RuntimeStoreComposition? secondaryComposition;
    private IStorageProviderConnection? connection;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;

    public IProviderRoundTripObserver? RoundTripObserver => primaryComposition?.Observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The trigger-binding-stimulus-lookup operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (primaryComposition is not null && secondaryComposition is not null)
            return;

        // SQLite admits one Groundwork connection per database file during provider admission. Probe
        // before composing either long-lived runtime connection, then retain that live handshake for
        // correctness provenance.
        var observed = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        var sharedObserver = primaryComposition?.Observer ?? new WritePathRoundTripObserver(request.Provider);
        var openedConnection = ProviderConnections.Open(request.Provider, connectionString);
        RuntimeStoreComposition? primary = null;
        RuntimeStoreComposition? secondary = null;
        try
        {
            primary = await RuntimeStoreComposition.CreateAsync(
                request.Provider,
                connectionString,
                PrimaryPersistenceScope,
                cancellationToken,
                sharedObserver,
                openedConnection);
            secondary = await RuntimeStoreComposition.CreateAsync(
                request.Provider,
                connectionString,
                SecondaryPersistenceScope,
                cancellationToken,
                sharedObserver,
                openedConnection);
            primaryComposition = primary;
            secondaryComposition = secondary;
            connection = openedConnection;
            observedProvider = observed;
        }
        catch
        {
            if (secondary is not null)
                await secondary.DisposeAsync();
            if (primary is not null)
                await primary.DisposeAsync();
            openedConnection.Dispose();
            throw;
        }
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var observed = observedProvider ?? throw new PerformanceContractException(
            "The trigger-binding-stimulus-lookup adapter has no provider handshake; PrepareAsync must run first.");
        var workload = new RuntimeTriggerBindingStimulusLookupWorkload();
        var result = await workload.ExecuteAsync(this, cancellationToken);
        operations = (await workload.PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();

        return new CorrectnessEvidence(
            result.ResultDigest,
            observed.Version,
            observed.Topology,
            observed.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                document.Routes));
    }

    public ValueTask<RuntimeTriggerBindingStimulusLookupScopes> OpenIsolatedScopesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var primary = RequirePrepared().CreateTriggerBindingClient();
        var secondary = RequireSecondary().CreateTriggerBindingClient();
        return ValueTask.FromResult(new RuntimeTriggerBindingStimulusLookupScopes(primary, secondary));
    }

    public async ValueTask DisposeAsync()
    {
        if (secondaryComposition is not null)
            await secondaryComposition.DisposeAsync();
        if (primaryComposition is not null)
            await primaryComposition.DisposeAsync();
        secondaryComposition = null;
        primaryComposition = null;
        connection?.Dispose();
        connection = null;
        observedProvider = null;
        operations = null;
    }

    private RuntimeStoreComposition RequirePrepared() =>
        primaryComposition ?? throw new PerformanceContractException(
            "The trigger-binding-stimulus-lookup adapter has no primary composition; PrepareAsync must run first.");

    private RuntimeStoreComposition RequireSecondary() =>
        secondaryComposition ?? throw new PerformanceContractException(
            "The trigger-binding-stimulus-lookup adapter has no secondary composition; PrepareAsync must run first.");

    private sealed class BenchmarkOperation(IRuntimeTriggerBindingStimulusLookupWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
