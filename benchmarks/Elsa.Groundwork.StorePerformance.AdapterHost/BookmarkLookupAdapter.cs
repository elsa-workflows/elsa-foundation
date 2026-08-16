using System.Text.Json;
using Elsa.Groundwork.StorePerformance.Benchmarks.Harness;
using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Microsoft.Extensions.DependencyInjection;

namespace Elsa.Groundwork.StorePerformance.AdapterHost;

/// <summary>Real public-store leaf for the frozen bookmark lookup baseline.</summary>
internal sealed class BookmarkLookupAdapter : IBenchmarkAdapter, IRuntimeBookmarkLookupWorkloadAdapter
{
    private const string CorrectnessPrimaryScope = "bookmark-correctness-primary";
    private const string CorrectnessSecondaryScope = "bookmark-correctness-secondary";
    private const string MeasuredScope = "bookmark-measured";
    private const string StimulusType = "runtime-bookmark-lookup";
    private const string StimulusHash = "bookmark-lookup-v1.1";
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);

    internal static readonly string[] OperationIds =
    [
        "seed-bookmarks",
        "lookup-by-stimulus-and-type",
        "read-next-bounded-page",
        "verify-cross-scope-isolation"
    ];

    private readonly RuntimeAdapterInfrastructure _runtime;
    private string _primaryScope = CorrectnessPrimaryScope;
    private string _secondaryScope = CorrectnessSecondaryScope;

    private BookmarkLookupAdapter(RuntimeAdapterInfrastructure runtime) => _runtime = runtime;

    public IReadOnlyList<IBenchmarkOperation> Operations { get; private set; } = [];

    public static async ValueTask<IBenchmarkAdapter> CreateAsync(
        AdapterContext context,
        CancellationToken cancellationToken) =>
        new BookmarkLookupAdapter(await RuntimeAdapterInfrastructure.OpenAsync(context, cancellationToken));

    public async Task PrepareAsync(CancellationToken cancellationToken)
    {
        await _runtime.PrepareAsync(cancellationToken);
        var measured = await OpenScopeAsync(MeasuredScope, cancellationToken);
        var isolated = await OpenScopeAsync($"{MeasuredScope}-isolated", cancellationToken);
        var firstPage = await SeedMeasuredLookupAsync(measured.BookmarkStateStore, cancellationToken);

        Operations =
        [
            new BenchmarkOperation(OperationIds[0], (invocation, token) =>
                measured.BookmarkStateStore.SaveAsync(CreateMeasuredBookmark(invocation), token).AsTask()),
            new BenchmarkOperation(OperationIds[1], (_, token) =>
                measured.BookmarkStimulusIndex.ListByStimulusPageAsync(
                    new BookmarkStimulusPageQuery(StimulusType, StimulusHash, RuntimeBookmarkLookupWorkload.PageSize),
                    token).AsTask()),
            new BenchmarkOperation(OperationIds[2], (_, token) =>
                measured.BookmarkStimulusIndex.ListByStimulusPageAsync(
                    new BookmarkStimulusPageQuery(
                        StimulusType,
                        StimulusHash,
                        RuntimeBookmarkLookupWorkload.PageSize,
                        firstPage.NextContinuationToken),
                    token).AsTask()),
            new BenchmarkOperation(OperationIds[3], (_, token) =>
                isolated.BookmarkStimulusIndex.ListByStimulusPageAsync(
                    new BookmarkStimulusPageQuery(StimulusType, StimulusHash, RuntimeBookmarkLookupWorkload.PageSize),
                    token).AsTask())
        ];
    }

    public async Task<CorrectnessEvidence> VerifyCorrectnessAsync(CancellationToken cancellationToken)
    {
        _primaryScope = CorrectnessPrimaryScope;
        _secondaryScope = CorrectnessSecondaryScope;
        var result = await new RuntimeBookmarkLookupWorkload().ExecuteAsync(this, cancellationToken);
        return _runtime.Correctness(result.ResultDigest);
    }

    public async ValueTask<RuntimeBookmarkLookupScopes> OpenIsolatedScopesAsync(
        CancellationToken cancellationToken = default) =>
        new(
            await OpenScopeAsync(_primaryScope, cancellationToken),
            await OpenScopeAsync(_secondaryScope, cancellationToken));

    public ValueTask DisposeAsync() => _runtime.DisposeAsync();

    private async ValueTask<RuntimeBookmarkLookupScope> OpenScopeAsync(
        string storageScope,
        CancellationToken cancellationToken)
    {
        var lease = await _runtime.OpenClientAsync(
            storageScope,
            services => services.GetRequiredService<IBookmarkStateStore>(),
            cancellationToken);
        return new RuntimeBookmarkLookupScope(lease.Client);
    }

    private static async Task<RuntimeStorePage<BookmarkState>> SeedMeasuredLookupAsync(
        IBookmarkStateStore store,
        CancellationToken cancellationToken)
    {
        for (var index = 0; index <= RuntimeBookmarkLookupWorkload.PageSize; index++)
            await store.SaveAsync(CreateLookupBookmark(index), cancellationToken);
        return await ((IBookmarkStimulusIndex)store).ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(StimulusType, StimulusHash, RuntimeBookmarkLookupWorkload.PageSize),
            cancellationToken);
    }

    private static BookmarkState CreateLookupBookmark(int index) => new(
        $"measured-lookup-{index:D4}",
        $"measured-workflow-{index:D4}",
        $"measured-activity-{index:D4}",
        "node",
        "resume",
        StimulusType,
        StimulusHash,
        JsonSerializer.SerializeToElement(new { index }),
        new Dictionary<string, string>(),
        SeededAt.AddMinutes(index),
        null);

    private static BookmarkState CreateMeasuredBookmark(long invocation) => new(
        $"measured-seed-{IdentityKey(invocation)}",
        $"measured-seed-workflow-{IdentityKey(invocation)}",
        "measured-seed-activity",
        "node",
        "resume",
        StimulusType,
        $"seed-{IdentityKey(invocation)}",
        JsonSerializer.SerializeToElement(new { invocation }),
        new Dictionary<string, string>(),
        SeededAt,
        null);

    internal static string IdentityKey(long invocation) => invocation < 0 ? $"w{-invocation}" : $"m{invocation}";
}
