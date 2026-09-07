using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Secrets.Core.Contracts;
using Elsa.Secrets.Core.Models;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The production Groundwork Secret comparator. It drives the public ISecretRepository through the
/// normal Groundwork composition; no provider session, document, or query shortcut crosses the workload
/// boundary.
/// </summary>
internal sealed class GroundworkSecretRepositoryAdapter : IBenchmarkAdapter, ISecretCreateReadListWorkloadAdapter
{
    internal const string PhysicalForm = "entity-type-specific-physical-tables";

    private readonly RunRequest request;
    private readonly string connectionString;
    private readonly string outputDirectory;
    private readonly string persistenceScope;
    private readonly WritePathRoundTripObserver commandObserver;
    private readonly SecretProviderConcurrencyProbe concurrencyProbe;
    private RuntimeStoreComposition? primaryComposition;
    private RuntimeStoreComposition? secondaryComposition;
    private ProviderProbe.Result? observedProvider;
    private IReadOnlyList<IBenchmarkOperation>? operations;

    internal GroundworkSecretRepositoryAdapter(
        RunRequest request,
        string connectionString,
        string outputDirectory)
    {
        this.request = request;
        this.connectionString = connectionString;
        this.outputDirectory = outputDirectory;
        persistenceScope = SecretStorageScope.For(request);
        concurrencyProbe = new SecretProviderConcurrencyProbe(
            providerCommandsSerializedByDesign: string.Equals(request.Provider, "sqlite", StringComparison.Ordinal));
        commandObserver = new WritePathRoundTripObserver(
            request.Provider,
            captureCommands: true,
            commandStarting: command =>
            {
                if (!command.IsProbe && command.Kind == global::Groundwork.Kernel.ProviderCommandKind.Write)
                    concurrencyProbe.ProviderCommandStarting();
            });
    }

    public IProviderRoundTripObserver? RoundTripObserver => primaryComposition?.Observer;

    internal SecretProviderConcurrencyEvidence? ConcurrencyEvidence { get; private set; }

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The secret-create-read-list operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (primaryComposition is not null)
            return;

        observedProvider = await ProviderProbe.ReadAsync(request.Provider, connectionString, cancellationToken);
        primaryComposition = await RuntimeStoreComposition.CreateAsync(
            request.Provider,
            connectionString,
            persistenceScope,
            cancellationToken,
            commandObserver,
            includeGroundworkSecretStores: true);
        if (!string.Equals(request.Provider, "sqlite", StringComparison.Ordinal))
        {
            try
            {
                secondaryComposition = await RuntimeStoreComposition.CreateAsync(
                    request.Provider,
                    connectionString,
                    persistenceScope,
                    cancellationToken,
                    commandObserver,
                    includeGroundworkSecretStores: true);
            }
            catch
            {
                await primaryComposition.DisposeAsync();
                primaryComposition = null;
                throw;
            }
        }
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var staged = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var result = await new SecretCreateReadListWorkload().ExecuteAsync(this, cancellationToken);
        var concurrency = RequireConcurrencyEvidence();
        operations = (await new SecretCreateReadListWorkload().PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();
        var provider = observedProvider ?? throw new PerformanceContractException(
            "The Groundwork Secret comparator has no provider handshake; PrepareAsync must run first.");

        if (staged.ProviderConcurrency != concurrency)
            throw new PerformanceContractException(
                "The staged Groundwork Secret native evidence does not match the live command-concurrency proof.");
        return new CorrectnessEvidence(
            result.ResultDigest,
            provider.Version,
            provider.Topology,
            provider.Configuration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                staged.Routes)
            {
                ProviderConcurrency = concurrency
            });
    }

    public ValueTask<SecretCreateReadListScopes> OpenIsolatedScopesAsync(
        CancellationToken cancellationToken = default)
    {
        var (primary, secondary) = RequirePrepared();
        cancellationToken.ThrowIfCancellationRequested();
        return new(new SecretCreateReadListScopes(
            new Client(primary.CreateSecretClient(), persistenceScope, concurrencyProbe, commandObserver),
            new Client(secondary.CreateSecretClient(), persistenceScope, concurrencyProbe, commandObserver)));
    }

    public async ValueTask DisposeAsync()
    {
        if (secondaryComposition is not null)
            await secondaryComposition.DisposeAsync();
        if (primaryComposition is not null)
            await primaryComposition.DisposeAsync();
        secondaryComposition = null;
        primaryComposition = null;
        observedProvider = null;
        operations = null;
    }

    private (RuntimeStoreComposition Primary, RuntimeStoreComposition Secondary) RequirePrepared() =>
        primaryComposition is not null
            ? (primaryComposition, secondaryComposition ?? primaryComposition)
            : throw new PerformanceContractException(
                "The Groundwork Secret comparator has no two-connection composed backing; PrepareAsync must run first.");

    internal WritePathRoundTripObserver CommandObserver => commandObserver;

    internal SecretProviderConcurrencyEvidence RequireConcurrencyEvidence() =>
        ConcurrencyEvidence ??= concurrencyProbe.RequireProven(
            distinctPhysicalConnectionCount: string.Equals(request.Provider, "sqlite", StringComparison.Ordinal) ? 1 : 2);

    private sealed class Client(
        ISecretRepository repository,
        string persistenceScope,
        SecretProviderConcurrencyProbe concurrencyProbe,
        WritePathRoundTripObserver observer) : ISecretCreateReadListClient
    {
        public async ValueTask<bool> TryAddAsync(Secret secret, CancellationToken cancellationToken = default)
        {
            var lease = await concurrencyProbe.EnterAsync(repository, secret, observer, cancellationToken);
            using var providerCall = lease?.BeginProviderCall();
            try
            {
                return await repository.TryAddAsync(
                    SecretStorageScope.ToStorage(secret, persistenceScope),
                    cancellationToken);
            }
            finally
            {
                lease?.Complete(observer.Snapshot());
            }
        }

        public async ValueTask<Secret?> FindAsync(
            string tenantId,
            string normalizedName,
            CancellationToken cancellationToken = default)
        {
            var result = await repository.FindAsync(
                SecretStorageScope.PhysicalTenant(tenantId, persistenceScope),
                normalizedName,
                cancellationToken);
            return result is null ? null : SecretStorageScope.ToLogical(result, tenantId, persistenceScope);
        }

        public async ValueTask<SecretRepositoryPage> ListPageAsync(
            string tenantId,
            SecretRepositoryListRequest request,
            CancellationToken cancellationToken = default)
        {
            var result = await repository.ListPageAsync(
                SecretStorageScope.PhysicalTenant(tenantId, persistenceScope),
                request,
                cancellationToken);
            return new SecretRepositoryPage(
                result.Items.Select(secret => SecretStorageScope.ToLogical(secret, tenantId, persistenceScope)).ToArray(),
                result.TotalCount);
        }

    }

    private sealed class BenchmarkOperation(ISecretCreateReadListWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
