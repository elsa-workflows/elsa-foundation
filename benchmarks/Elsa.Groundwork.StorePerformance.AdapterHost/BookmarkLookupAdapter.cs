using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>
/// The Groundwork v2 bookmark lookup adapter. The workload owns the bounded operation definitions; this
/// leaf only composes the production public runtime stores over two logical persistence scopes.
/// </summary>
internal sealed class BookmarkLookupAdapter(
    RunRequest request,
    string connectionString,
    string outputDirectory)
    : IBenchmarkAdapter, IRuntimeBookmarkLookupWorkloadAdapter
{
    internal const string PhysicalForm = "document-type-specific-tables";
    private const string PrimaryPersistenceScope = "bookmark-primary";
    private const string SecondaryPersistenceScope = "bookmark-secondary";

    private RuntimeStoreComposition? primaryComposition;
    private RuntimeStoreComposition? secondaryComposition;
    private WritePathRoundTripObserver? observer;
    private IReadOnlyList<IBenchmarkOperation>? operations;

    public IProviderRoundTripObserver? RoundTripObserver => observer;

    public IReadOnlyList<IBenchmarkOperation> Operations =>
        operations ?? throw new PerformanceContractException(
            "The bookmark-lookup operations were requested before correctness preparation completed.");

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        if (primaryComposition is not null && secondaryComposition is not null)
            return;

        observer ??= new WritePathRoundTripObserver(request.Provider);
        primaryComposition ??= await RuntimeStoreComposition.CreateAsync(
            request.Provider, connectionString, PrimaryPersistenceScope, cancellationToken, observer);
        try
        {
            secondaryComposition ??= await RuntimeStoreComposition.CreateAsync(
                request.Provider, connectionString, SecondaryPersistenceScope, cancellationToken, observer);
        }
        catch
        {
            await primaryComposition.DisposeAsync();
            primaryComposition = null;
            throw;
        }
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        RequirePrepared();
        var document = NativePlanEvidenceStaging.PublishInto(outputDirectory, request);
        var workload = new RuntimeBookmarkLookupWorkload();
        var result = await workload.ExecuteAsync(this, cancellationToken);
        operations = (await workload.PrepareMeasuredOperationsAsync(this, cancellationToken))
            .Select(operation => (IBenchmarkOperation)new BenchmarkOperation(operation))
            .ToArray();

        return new CorrectnessEvidence(
            result.ResultDigest,
            request.ProviderVersion,
            request.ProviderTopology,
            request.ProviderConfiguration,
            new NativePlanEvidence(
                request.NativePlanIdentity,
                request.NativePlanEvidenceReference,
                request.NativePlanContentSha256,
                document.Routes));
    }

    public ValueTask<RuntimeBookmarkLookupScopes> OpenIsolatedScopesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var primary = RequirePrepared().CreateBookmarkClient();
        var secondary = RequireSecondary().CreateBookmarkClient();
        return ValueTask.FromResult(new RuntimeBookmarkLookupScopes(
            new(primary.BookmarkStateStore),
            new(secondary.BookmarkStateStore)));
    }

    public async ValueTask DisposeAsync()
    {
        if (secondaryComposition is not null)
            await secondaryComposition.DisposeAsync();
        if (primaryComposition is not null)
            await primaryComposition.DisposeAsync();
        secondaryComposition = null;
        primaryComposition = null;
        observer = null;
        operations = null;
    }

    private RuntimeStoreComposition RequirePrepared() =>
        primaryComposition ?? throw new PerformanceContractException(
            "The bookmark adapter has no primary composition; PrepareAsync must run first.");

    private RuntimeStoreComposition RequireSecondary() =>
        secondaryComposition ?? throw new PerformanceContractException(
            "The bookmark adapter has no secondary composition; PrepareAsync must run first.");

    private sealed class BenchmarkOperation(IRuntimeBookmarkLookupWorkloadOperation operation) : IBenchmarkOperation
    {
        public string Id => operation.Id;

        public Task PrepareInvocationAsync(long invocation, CancellationToken cancellationToken) =>
            operation.PrepareInvocationAsync(invocation, cancellationToken).AsTask();

        public Task InvokeAsync(long invocation, CancellationToken cancellationToken) =>
            operation.InvokeAsync(invocation, cancellationToken).AsTask();
    }
}
