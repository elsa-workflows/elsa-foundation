using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using System.Text.Json;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeBookmarkLookupWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_exact_catalog_golden()
    {
        var adapter = new DictionaryBookmarkLookupAdapter();
        var result = await new RuntimeBookmarkLookupWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeBookmarkLookupWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeBookmarkLookupWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeBookmarkLookupWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal(RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow, adapter.Primary.States.Count);
        Assert.Equal(RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow, adapter.Secondary.States.Count);
        Assert.Equal(RuntimeBookmarkLookupWorkload.MatchingBookmarks, adapter.Primary.States.Count(state => state.BookmarkId.StartsWith("bookmark-match-", StringComparison.Ordinal)));
        Assert.Equal(RuntimeBookmarkLookupWorkload.MatchingBookmarks, adapter.Secondary.States.Count(state => state.BookmarkId.StartsWith("bookmark-match-", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task Performs_the_catalog_operations_in_deterministic_order()
    {
        var adapter = new DictionaryBookmarkLookupAdapter();
        var result = await new RuntimeBookmarkLookupWorkload().ExecuteAsync(adapter);

        Assert.Equal(
            ["seed-bookmarks", "lookup-by-stimulus-and-type", "read-next-bounded-page", "verify-cross-scope-isolation"],
            result.ObservableOperations);
    }

    [Fact]
    public async Task Consumes_the_second_page_at_the_expected_boundary()
    {
        var adapter = new DictionaryBookmarkLookupAdapter();
        await new RuntimeBookmarkLookupWorkload().ExecuteAsync(adapter);

        Assert.Equal(3, adapter.Primary.Requests.Count);
        Assert.Null(adapter.Primary.Requests[0].ContinuationToken);
        Assert.NotNull(adapter.Primary.Requests[1].ContinuationToken);
        Assert.Null(adapter.Primary.Requests[2].ContinuationToken);
        Assert.Equal(RuntimeBookmarkLookupWorkload.PageSize, adapter.Primary.Requests[0].Limit);
        Assert.Equal(RuntimeBookmarkLookupWorkload.PageSize, adapter.Primary.Requests[1].Limit);
        Assert.Equal(RuntimeBookmarkLookupWorkload.PageSize, adapter.Primary.Requests[2].Limit);
    }

    [Fact]
    public async Task Exposes_bounded_public_lookup_phases_with_setup_outside_timing()
    {
        var adapter = new DictionaryBookmarkLookupAdapter();
        var operations = await new RuntimeBookmarkLookupWorkload().PrepareMeasuredOperationsAsync(adapter);

        Assert.Equal(["lookup-by-stimulus-and-type", "read-next-bounded-page"], operations.Select(operation => operation.Id));
        var setupRequestCount = adapter.Primary.Requests.Count;
        Assert.True(setupRequestCount > 0);

        foreach (var operation in operations)
        {
            await operation.PrepareInvocationAsync(0);
            var beforeInvoke = adapter.Primary.Requests.Count;
            await operation.InvokeAsync(0);
            Assert.Equal(beforeInvoke + 1, adapter.Primary.Requests.Count);
        }

        Assert.Equal(setupRequestCount + operations.Count, adapter.Primary.Requests.Count);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task Rejects_scope_adapters_that_leak_results_in_either_direction(
        bool leakPrimaryIntoSecondary,
        bool leakSecondaryIntoPrimary)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeBookmarkLookupWorkload().ExecuteAsync(
                new DictionaryBookmarkLookupAdapter(
                    leakPrimaryIntoSecondary: leakPrimaryIntoSecondary,
                    leakSecondaryIntoPrimary: leakSecondaryIntoPrimary)).AsTask());
    }

    [Fact]
    public async Task Rejects_a_store_that_discards_seeded_state()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeBookmarkLookupWorkload().ExecuteAsync(new DictionaryBookmarkLookupAdapter(discardPrimarySaves: true)).AsTask());
    }

    [Fact]
    public void Rejects_a_scope_whose_state_store_does_not_own_the_stimulus_index()
    {
        var exception = Assert.Throws<ArgumentException>(() => new RuntimeBookmarkLookupScope(new StateOnlyBookmarkStore()));

        Assert.Contains("same instance", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(DictionaryBookmarkLookupFault.WrongContinuation)]
    [InlineData(DictionaryBookmarkLookupFault.WrongOrder)]
    [InlineData(DictionaryBookmarkLookupFault.MissingMatch)]
    [InlineData(DictionaryBookmarkLookupFault.AlterObservableResult)]
    public async Task Fails_closed_when_an_observable_result_drifts(DictionaryBookmarkLookupFault fault)
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeBookmarkLookupWorkload().ExecuteAsync(new DictionaryBookmarkLookupAdapter(fault: fault)).AsTask());
    }

    [Fact]
    public void Public_adapter_surface_contains_no_provider_or_timing_inputs()
    {
        var types = new[]
        {
            typeof(IRuntimeBookmarkLookupWorkloadAdapter),
            typeof(RuntimeBookmarkLookupScopes),
            typeof(RuntimeBookmarkLookupScope)
        };
        var publicSurface = types
            .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IRuntimeBookmarkLookupWorkloadAdapter).ToString());

        Assert.DoesNotContain(publicSurface, value =>
            value.Contains("Provider", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Connection", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("DocumentStore", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Raw", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Timing", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Matrix", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Ledger", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Manifest", StringComparison.OrdinalIgnoreCase) ||
            value.Contains("Physical", StringComparison.OrdinalIgnoreCase));
    }

    private sealed class DictionaryBookmarkLookupAdapter : IRuntimeBookmarkLookupWorkloadAdapter
    {
        private readonly bool _leakPrimaryIntoSecondary;
        private readonly bool _leakSecondaryIntoPrimary;
        private readonly bool _discardPrimarySaves;
        private readonly DictionaryBookmarkLookupFault _fault;

        public DictionaryBookmarkLookupAdapter(
            bool leakPrimaryIntoSecondary = false,
            bool leakSecondaryIntoPrimary = false,
            bool discardPrimarySaves = false,
            DictionaryBookmarkLookupFault fault = DictionaryBookmarkLookupFault.None)
        {
            _leakPrimaryIntoSecondary = leakPrimaryIntoSecondary;
            _leakSecondaryIntoPrimary = leakSecondaryIntoPrimary;
            _discardPrimarySaves = discardPrimarySaves;
            _fault = fault;
        }

        public DictionaryBookmarkStore Primary { get; } = new();
        public DictionaryBookmarkStore Secondary { get; } = new();

        public ValueTask<RuntimeBookmarkLookupScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Primary.Fault = _fault;
            Secondary.Fault = _fault;
            IBookmarkStateStore primary = _discardPrimarySaves
                ? new DelegatingBookmarkStore(new StateOnlyBookmarkStore(), Primary)
                : _leakSecondaryIntoPrimary
                    ? new SelectiveLeakBookmarkStore(Primary, Secondary)
                    : Primary;
            IBookmarkStateStore secondary = _leakPrimaryIntoSecondary
                ? new SelectiveLeakBookmarkStore(Secondary, Primary)
                : Secondary;
            return new(new RuntimeBookmarkLookupScopes(
                new(primary),
                new(secondary)));
        }
    }

    private sealed class StateOnlyBookmarkStore : IBookmarkStateStore
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
            => new(state);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            new(false);

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            new((BookmarkState?)null);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The bookmark lookup workload only uses the stimulus index.");
    }

    private sealed class DelegatingBookmarkStore(
        IBookmarkStateStore stateStore,
        IBookmarkStimulusIndex stimulusIndex) : IBookmarkStateStore, IBookmarkStimulusIndex
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) =>
            stateStore.SaveAsync(state, cancellationToken);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            stateStore.DeleteAsync(workflowExecutionId, bookmarkId, cancellationToken);

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            stateStore.FindAsync(workflowExecutionId, bookmarkId, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) =>
            stateStore.ListPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
            BookmarkStimulusPageQuery query,
            CancellationToken cancellationToken = default) =>
            stimulusIndex.ListByStimulusPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
            BookmarkStimulusTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            stimulusIndex.ListByStimulusTypePageAsync(query, cancellationToken);
    }

    private sealed class SelectiveLeakBookmarkStore(
        DictionaryBookmarkStore localStore,
        IBookmarkStimulusIndex foreignIndex) : IBookmarkStateStore, IBookmarkStimulusIndex
    {
        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default) =>
            localStore.SaveAsync(state, cancellationToken);

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            localStore.DeleteAsync(workflowExecutionId, bookmarkId, cancellationToken);

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            localStore.FindAsync(workflowExecutionId, bookmarkId, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) =>
            localStore.ListPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
            BookmarkStimulusPageQuery query,
            CancellationToken cancellationToken = default) =>
            localStore.States.Any(state => state.StimulusType == query.StimulusType && state.StimulusHash == query.StimulusHash)
                ? localStore.ListByStimulusPageAsync(query, cancellationToken)
                : foreignIndex.ListByStimulusPageAsync(query, cancellationToken);

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
            BookmarkStimulusTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            localStore.ListByStimulusTypePageAsync(query, cancellationToken);
    }

    private sealed class DictionaryBookmarkStore : IBookmarkStateStore, IBookmarkStimulusIndex
    {
        private const string Continuation = "next";
        private readonly Dictionary<(string WorkflowExecutionId, string BookmarkId), BookmarkState> _states = new();

        public DictionaryBookmarkLookupFault Fault { get; set; }
        public IReadOnlyCollection<BookmarkState> States => _states.Values;
        public List<BookmarkStimulusPageQuery> Requests { get; } = [];

        public ValueTask<BookmarkState> SaveAsync(BookmarkState state, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _states[(state.WorkflowExecutionId, state.BookmarkId)] = state;
            return new(state);
        }

        public ValueTask<bool> DeleteAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            new(_states.Remove((workflowExecutionId, bookmarkId)));

        public ValueTask<BookmarkState?> FindAsync(string workflowExecutionId, string bookmarkId, CancellationToken cancellationToken = default) =>
            new(_states.GetValueOrDefault((workflowExecutionId, bookmarkId)));

        public ValueTask<RuntimeStorePage<BookmarkState>> ListPageAsync(BookmarkStatePageQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The bookmark lookup workload only uses the stimulus index.");

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
            BookmarkStimulusPageQuery query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(query);

            var matches = States
                .Where(state => state.StimulusType == query.StimulusType && state.StimulusHash == query.StimulusHash)
                .OrderBy(state => state.WorkflowExecutionId, StringComparer.Ordinal)
                .ThenBy(state => state.BookmarkId, StringComparer.Ordinal)
                .ToList();

            if (Fault == DictionaryBookmarkLookupFault.MissingMatch)
                matches.RemoveAt(0);
            if (query.ContinuationToken is not null &&
                !(Fault == DictionaryBookmarkLookupFault.WrongContinuation && query.ContinuationToken != Continuation))
                matches = matches.Skip(query.Limit).ToList();
            if (Fault == DictionaryBookmarkLookupFault.WrongOrder)
                matches.Reverse();
            if (Fault == DictionaryBookmarkLookupFault.AlterObservableResult && matches.Count > 0)
                matches[0] = matches[0] with { ResumeTargetId = "altered" };

            var items = matches.Take(query.Limit).Select(RoundTrip).ToArray();
            var next = matches.Count > items.Length ? Continuation : null;
            if (Fault == DictionaryBookmarkLookupFault.WrongContinuation && query.ContinuationToken is null)
                next = "wrong";
            return new(new RuntimeStorePage<BookmarkState>(query, items, next));
        }

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusTypePageAsync(
            BookmarkStimulusTypePageQuery query,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The bookmark lookup workload only uses exact stimulus lookups.");

        private static BookmarkState RoundTrip(BookmarkState state) => state with
        {
            Payload = state.Payload?.Clone(),
            Metadata = new Dictionary<string, string>(state.Metadata, StringComparer.Ordinal)
        };
    }
}

public enum DictionaryBookmarkLookupFault
{
    None,
    WrongContinuation,
    WrongOrder,
    MissingMatch,
    AlterObservableResult
}
