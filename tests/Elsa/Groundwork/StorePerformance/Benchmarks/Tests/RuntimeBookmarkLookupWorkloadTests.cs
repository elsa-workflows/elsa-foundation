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
        Assert.Equal(RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow, adapter.Primary.Store.States.Count);
        Assert.Equal(RuntimeBookmarkLookupWorkload.WorkflowCount * RuntimeBookmarkLookupWorkload.BookmarksPerWorkflow, adapter.Secondary.Store.States.Count);
        Assert.Equal(RuntimeBookmarkLookupWorkload.MatchingBookmarks, adapter.Primary.Store.States.Count(state => state.BookmarkId.StartsWith("bookmark-match-", StringComparison.Ordinal)));
        Assert.Equal(RuntimeBookmarkLookupWorkload.MatchingBookmarks, adapter.Secondary.Store.States.Count(state => state.BookmarkId.StartsWith("bookmark-match-", StringComparison.Ordinal)));
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

        Assert.Equal(2, adapter.Primary.Index.Requests.Count);
        Assert.Null(adapter.Primary.Index.Requests[0].ContinuationToken);
        Assert.NotNull(adapter.Primary.Index.Requests[1].ContinuationToken);
        Assert.Equal(RuntimeBookmarkLookupWorkload.PageSize, adapter.Primary.Index.Requests[0].Limit);
        Assert.Equal(RuntimeBookmarkLookupWorkload.PageSize, adapter.Primary.Index.Requests[1].Limit);
    }

    [Fact]
    public async Task Rejects_a_scope_adapter_that_leaks_the_primary_results()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new RuntimeBookmarkLookupWorkload().ExecuteAsync(new DictionaryBookmarkLookupAdapter(leakSecondScope: true)).AsTask());
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
        private readonly bool _leakSecondScope;
        private readonly DictionaryBookmarkLookupFault _fault;

        public DictionaryBookmarkLookupAdapter(
            bool leakSecondScope = false,
            DictionaryBookmarkLookupFault fault = DictionaryBookmarkLookupFault.None)
        {
            _leakSecondScope = leakSecondScope;
            _fault = fault;
        }

        public DictionaryBookmarkScope Primary { get; } = new();
        public DictionaryBookmarkScope Secondary { get; } = new();

        public ValueTask<RuntimeBookmarkLookupScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Primary.Index.Fault = _fault;
            Secondary.Index.Fault = _fault;
            return new(new RuntimeBookmarkLookupScopes(
                new(Primary.Store, Primary.Index),
                new(Secondary.Store, _leakSecondScope ? Primary.Index : Secondary.Index)));
        }
    }

    private sealed class DictionaryBookmarkScope
    {
        public DictionaryBookmarkScope()
        {
            Store = new DictionaryBookmarkStore();
            Index = new DictionaryBookmarkIndex(Store);
        }

        public DictionaryBookmarkStore Store { get; }
        public DictionaryBookmarkIndex Index { get; }
    }

    private sealed class DictionaryBookmarkStore : IBookmarkStateStore
    {
        private readonly Dictionary<(string WorkflowExecutionId, string BookmarkId), BookmarkState> _states = new();

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

        public IReadOnlyCollection<BookmarkState> States => _states.Values;
    }

    private sealed class DictionaryBookmarkIndex : IBookmarkStimulusIndex
    {
        private const string Continuation = "next";

        public DictionaryBookmarkIndex(DictionaryBookmarkStore store) => Store = store;

        public DictionaryBookmarkStore Store { get; }
        public List<BookmarkStimulusPageQuery> Requests { get; } = [];
        public DictionaryBookmarkLookupFault Fault { get; set; }

        public ValueTask<RuntimeStorePage<BookmarkState>> ListByStimulusPageAsync(
            BookmarkStimulusPageQuery query,
            CancellationToken cancellationToken = default)
        {
            Requests.Add(query);

            var matches = Store.States
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
