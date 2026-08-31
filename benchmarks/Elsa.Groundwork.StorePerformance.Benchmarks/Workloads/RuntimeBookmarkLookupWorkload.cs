using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;

/// <summary>
/// Executes the provider-neutral, catalog-owned bookmark lookup correctness baseline. Logical storage
/// isolation is selected by the host through the adapter; bookmark state itself has no scope member.
/// </summary>
public sealed class RuntimeBookmarkLookupWorkload
{
    private const string PrimaryStimulusType = "runtime-bookmark-lookup";
    private const string PrimaryStimulusHash = "bookmark-lookup-v1.1";
    private const string SecondaryStimulusHash = "bookmark-lookup-other-scope";
    private const string NoiseStimulusHash = "bookmark-lookup-noise";
    private static readonly DateTimeOffset SeededAt = new(2026, 7, 20, 10, 0, 0, TimeSpan.Zero);
    private static readonly ReproducibleWorkloadScenario Scenario = ReproducibleWorkloadScenarioCatalog.Get(WorkloadId);
    private static readonly JsonElement Payload = JsonSerializer.SerializeToElement(new string('p', PayloadBytes));
    private static readonly JsonSerializerOptions CanonicalJsonOptions = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public const string WorkloadId = "bookmark-lookup";
    public const string ExpectedInputFingerprint = "d006e25e22dc8d9374d8931f03e27c6dc45c27314bfe2f819a4dd61b588062e8";
    public const string ExpectedResultDigest = "e723ae42c3fd4e970cff04d4a6e867fa40b8d6ea23b0305ab82bf80d3916d6a9";
    public static string ScenarioId => Scenario.ScenarioId;
    public static string Version => Scenario.Version;
    public static string Seed => Scenario.Seed;
    public static int ScopeCount => Int("tenantCount");
    public static int WorkflowCount => Int("workflowCount");
    public static int BookmarksPerWorkflow => Int("bookmarksPerWorkflow");
    public static int MatchingBookmarks => Int("matchingBookmarks");
    public static int PageSize => Int("pageSize");
    public static int PayloadBytes => Int("payloadBytes");

    public async ValueTask<RuntimeBookmarkLookupResult> ExecuteAsync(
        IRuntimeBookmarkLookupWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(scopes.Primary);
        ArgumentNullException.ThrowIfNull(scopes.Secondary);

        var operations = new List<string>();
        var expectedMatches = await SeedAsync(scopes.Primary.BookmarkStateStore, isSecondaryScope: false, cancellationToken);
        await SeedAsync(scopes.Secondary.BookmarkStateStore, isSecondaryScope: true, cancellationToken);
        operations.Add("seed-bookmarks");

        var firstPage = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize),
            cancellationToken);
        RequirePage(firstPage, expectedMatches.Take(PageSize), "first bookmark lookup page", continuationRequired: true);
        operations.Add("lookup-by-stimulus-and-type");

        var secondPage = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize, firstPage.NextContinuationToken),
            cancellationToken);
        RequirePage(secondPage, expectedMatches.Skip(PageSize), "second bookmark lookup page", continuationRequired: false);
        operations.Add("read-next-bounded-page");

        var primaryHashInSecondaryScope = await scopes.Secondary.BookmarkStimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize),
            cancellationToken);
        RequireEmptyPage(primaryHashInSecondaryScope, "secondary logical storage scope exposed bookmark lookup results from the primary scope");

        var secondaryHashInPrimaryScope = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(PrimaryStimulusType, SecondaryStimulusHash, PageSize),
            cancellationToken);
        RequireEmptyPage(secondaryHashInPrimaryScope, "primary logical storage scope exposed bookmark lookup results from the secondary scope");
        operations.Add("verify-cross-scope-isolation");

        if (!operations.SequenceEqual(scenario.OperationSequence, StringComparer.Ordinal))
            throw new InvalidOperationException("The bookmark lookup workload operation order no longer matches the catalog contract.");

        var actualObservations = new SortedDictionary<string, object>(StringComparer.Ordinal)
        {
            ["crossScopeResultCount"] = primaryHashInSecondaryScope.Items.Count + secondaryHashInPrimaryScope.Items.Count,
            ["firstPageCount"] = firstPage.Items.Count,
            ["matchingBookmarkIdentityDigest"] = Hash(JsonSerializer.Serialize(firstPage.Items.Concat(secondPage.Items).Select(state => state.BookmarkId), CanonicalJsonOptions)),
            ["secondPageCount"] = secondPage.Items.Count,
            ["stableContinuation"] = secondPage.Items[0].BookmarkId
        };
        var expectedObservations = scenario.CreateExpectedObservations();
        if (!ObservationsMatch(actualObservations, expectedObservations))
            throw new InvalidOperationException("The bookmark lookup observable results no longer match the catalog contract.");

        var resultDigest = ReproducibleWorkloadScenarioCatalog.Hash(ReproducibleWorkloadScenarioCatalog.Serialize(new
        {
            WorkloadId,
            scenario.ScenarioId,
            InputFingerprint = scenario.ComputeInputFingerprint(),
            Operations = operations,
            ObservableResults = actualObservations
        }));
        if (resultDigest != ExpectedResultDigest)
            throw new InvalidOperationException("The bookmark lookup observable result digest no longer matches its ratified value.");

        return new RuntimeBookmarkLookupResult(
            scenario.ComputeInputFingerprint(),
            resultDigest,
            operations,
            actualObservations);
    }

    /// <summary>
    /// Prepares the two bounded public lookup operations used by process measurement. The deterministic
    /// fixture is created here when a fresh adapter is supplied; when correctness has already seeded the
    /// same adapter, the existing rows are reused. In either case setup and the probe page are outside the
    /// measurement callback, while each returned operation calls the public stimulus-index route itself.
    /// </summary>
    public async ValueTask<IReadOnlyList<IRuntimeBookmarkLookupWorkloadOperation>> PrepareMeasuredOperationsAsync(
        IRuntimeBookmarkLookupWorkloadAdapter adapter,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(adapter);
        var scenario = ValidateScenario();
        if (!scenario.OperationSequence.SequenceEqual(
                ["seed-bookmarks", "lookup-by-stimulus-and-type", "read-next-bounded-page", "verify-cross-scope-isolation"],
                StringComparer.Ordinal))
            throw new InvalidOperationException("The bookmark scenario operation sequence no longer matches the measured lookup contract.");

        var scopes = await adapter.OpenIsolatedScopesAsync(cancellationToken);
        ArgumentNullException.ThrowIfNull(scopes);
        ArgumentNullException.ThrowIfNull(scopes.Primary);
        ArgumentNullException.ThrowIfNull(scopes.Secondary);

        var expectedMatches = Enumerable.Range(0, MatchingBookmarks)
            .Select(workflow => CreateBookmark(workflow, 0, match: true, isSecondaryScope: false))
            .ToArray();
        var probe = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
            new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize),
            cancellationToken);
        if (probe.Items.Count == 0)
        {
            await SeedAsync(scopes.Primary.BookmarkStateStore, isSecondaryScope: false, cancellationToken);
            await SeedAsync(scopes.Secondary.BookmarkStateStore, isSecondaryScope: true, cancellationToken);
            probe = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
                new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize),
                cancellationToken);
        }

        RequirePage(probe, expectedMatches.Take(PageSize), "measured bookmark lookup setup", continuationRequired: true);
        var continuation = probe.NextContinuationToken!;
        return
        [
            new RuntimeBookmarkLookupWorkloadOperation(
                "lookup-by-stimulus-and-type",
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
                        new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize), token);
                    RequirePage(page, expectedMatches.Take(PageSize), "measured first bookmark lookup page", continuationRequired: true);
                }),
            new RuntimeBookmarkLookupWorkloadOperation(
                "read-next-bounded-page",
                (_, _) => ValueTask.CompletedTask,
                async (_, token) =>
                {
                    var page = await scopes.Primary.BookmarkStimulusIndex.ListByStimulusPageAsync(
                        new BookmarkStimulusPageQuery(PrimaryStimulusType, PrimaryStimulusHash, PageSize, continuation), token);
                    RequirePage(page, expectedMatches.Skip(PageSize), "measured second bookmark lookup page", continuationRequired: false);
                })
        ];
    }

    private static ReproducibleWorkloadScenario ValidateScenario()
    {
        if (Scenario.Version != "1.1.0" ||
            Scenario.ScenarioId != "runtime-bookmark-state" ||
            Scenario.Seed != "spec094-bookmark-lookup-v1.1" ||
            Scenario.ComputeInputFingerprint() != ExpectedInputFingerprint ||
            Scenario.ComputeResultDigest() != ExpectedResultDigest ||
            !ReproducibleWorkloadScenarioCatalog.GoldenVectors.TryGetValue(WorkloadId, out var golden) ||
            golden.InputFingerprint != ExpectedInputFingerprint ||
            golden.ResultDigest != ExpectedResultDigest ||
            ScopeCount != 2 ||
            WorkflowCount != 128 ||
            BookmarksPerWorkflow != 64 ||
            MatchingBookmarks != 37 ||
            PageSize != 25 ||
            PayloadBytes != 256)
            throw new InvalidOperationException("The bookmark lookup scenario no longer matches its frozen v1.1 catalog contract.");

        return Scenario;
    }

    private static int Int(string name) => (int)Scenario.Parameters[name];

    private static async ValueTask<IReadOnlyList<BookmarkState>> SeedAsync(
        IBookmarkStateStore store,
        bool isSecondaryScope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(store);
        var matches = new List<BookmarkState>(MatchingBookmarks);
        for (var workflow = 0; workflow < WorkflowCount; workflow++)
        {
            for (var bookmark = 0; bookmark < BookmarksPerWorkflow; bookmark++)
            {
                var match = workflow < MatchingBookmarks && bookmark == 0;
                var state = CreateBookmark(workflow, bookmark, match, isSecondaryScope);
                await store.SaveAsync(state, cancellationToken);
                if (match && !isSecondaryScope)
                    matches.Add(state);
            }
        }

        return matches;
    }

    private static BookmarkState CreateBookmark(int workflow, int bookmark, bool match, bool isSecondaryScope)
    {
        var workflowExecutionId = $"workflow-{workflow:D4}";
        var bookmarkId = match
            ? $"bookmark-match-{workflow:D4}"
            : $"bookmark-noise-{workflow:D4}-{bookmark:D4}";
        var stimulusHash = match
            ? isSecondaryScope ? SecondaryStimulusHash : PrimaryStimulusHash
            : NoiseStimulusHash;
        return new BookmarkState(
            bookmarkId,
            workflowExecutionId,
            $"activity-{workflow:D4}-{bookmark:D4}",
            $"node-{workflow:D4}",
            $"resume-target-{bookmark:D4}",
            PrimaryStimulusType,
            stimulusHash,
            Payload,
            new Dictionary<string, string>(StringComparer.Ordinal) { ["seed"] = Seed },
            SeededAt.AddMinutes(workflow * BookmarksPerWorkflow + bookmark),
            null);
    }

    private static void RequirePage(
        RuntimeStorePage<BookmarkState> page,
        IEnumerable<BookmarkState> expected,
        string operation,
        bool continuationRequired)
    {
        ArgumentNullException.ThrowIfNull(page);
        var expectedItems = expected.ToArray();
        if (!page.Items.SequenceEqual(expectedItems, BookmarkStateValueComparer.Instance) ||
            (continuationRequired && page.NextContinuationToken is null) ||
            (!continuationRequired && page.NextContinuationToken is not null))
            throw new InvalidOperationException($"The {operation} does not match the exact bounded bookmark result contract.");
    }

    private static bool ObservationsMatch(
        IReadOnlyDictionary<string, object> actual,
        IReadOnlyDictionary<string, object> expected) =>
        actual.Count == expected.Count &&
        actual.All(pair => expected.TryGetValue(pair.Key, out var expectedValue) && Equals(pair.Value, expectedValue));

    private static void RequireEmptyPage(RuntimeStorePage<BookmarkState> page, string failure)
    {
        ArgumentNullException.ThrowIfNull(page);
        if (page.Items.Count != 0 || page.NextContinuationToken is not null)
            throw new InvalidOperationException($"The {failure}.");
    }

    private static string Hash(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    private sealed class BookmarkStateValueComparer : IEqualityComparer<BookmarkState>
    {
        public static BookmarkStateValueComparer Instance { get; } = new();

        public bool Equals(BookmarkState? left, BookmarkState? right)
        {
            if (ReferenceEquals(left, right))
                return true;
            if (left is null || right is null)
                return false;

            return left.BookmarkId == right.BookmarkId &&
                   left.WorkflowExecutionId == right.WorkflowExecutionId &&
                   left.ActivityExecutionId == right.ActivityExecutionId &&
                   left.ExecutableNodeId == right.ExecutableNodeId &&
                   left.ResumeTargetId == right.ResumeTargetId &&
                   left.StimulusType == right.StimulusType &&
                   left.StimulusHash == right.StimulusHash &&
                   PayloadEquals(left.Payload, right.Payload) &&
                   left.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                       .SequenceEqual(right.Metadata.OrderBy(pair => pair.Key, StringComparer.Ordinal)) &&
                   left.CreatedAt == right.CreatedAt &&
                   left.ExpiresAt == right.ExpiresAt;
        }

        public int GetHashCode(BookmarkState state) => StringComparer.Ordinal.GetHashCode(state.BookmarkId);

        private static bool PayloadEquals(JsonElement? left, JsonElement? right) =>
            left.HasValue == right.HasValue &&
            (!left.HasValue || JsonElement.DeepEquals(left.Value, right!.Value));
    }
}

/// <summary>Host-selected isolated logical storage scopes for the bookmark correctness baseline.</summary>
public interface IRuntimeBookmarkLookupWorkloadAdapter
{
    ValueTask<RuntimeBookmarkLookupScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default);
}

/// <summary>One workload-owned bounded public lookup phase for process measurement.</summary>
public interface IRuntimeBookmarkLookupWorkloadOperation
{
    string Id { get; }
    ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default);
    ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default);
}

internal sealed class RuntimeBookmarkLookupWorkloadOperation(
    string id,
    Func<long, CancellationToken, ValueTask> prepare,
    Func<long, CancellationToken, ValueTask> invoke) : IRuntimeBookmarkLookupWorkloadOperation
{
    public string Id { get; } = id;

    public ValueTask PrepareInvocationAsync(long invocation, CancellationToken cancellationToken = default) =>
        prepare(invocation, cancellationToken);

    public ValueTask InvokeAsync(long invocation, CancellationToken cancellationToken = default) =>
        invoke(invocation, cancellationToken);
}

/// <summary>Two scopes expose only the bookmark state and stimulus-index public contracts.</summary>
public sealed record RuntimeBookmarkLookupScopes(RuntimeBookmarkLookupScope Primary, RuntimeBookmarkLookupScope Secondary);

/// <summary>Public bookmark stores minted from one adapter-owned runtime DI scope.</summary>
public sealed record RuntimeBookmarkLookupClient(
    IBookmarkStateStore BookmarkStateStore,
    IBookmarkStimulusIndex BookmarkStimulusIndex);

/// <summary>
/// One host-selected logical storage scope. The stimulus index is required to be the same store
/// instance that owns bookmark state, matching the runtime composition contract.
/// </summary>
public sealed record RuntimeBookmarkLookupScope
{
    public RuntimeBookmarkLookupScope(IBookmarkStateStore bookmarkStateStore)
    {
        BookmarkStateStore = bookmarkStateStore ?? throw new ArgumentNullException(nameof(bookmarkStateStore));
        BookmarkStimulusIndex = bookmarkStateStore as IBookmarkStimulusIndex
            ?? throw new ArgumentException(
                "The bookmark state store must implement IBookmarkStimulusIndex on the same instance.",
                nameof(bookmarkStateStore));
    }

    public IBookmarkStateStore BookmarkStateStore { get; }
    public IBookmarkStimulusIndex BookmarkStimulusIndex { get; }
}

public sealed record RuntimeBookmarkLookupResult(
    string InputFingerprint,
    string ResultDigest,
    IReadOnlyList<string> ObservableOperations,
    IReadOnlyDictionary<string, object> ObservableResults);
