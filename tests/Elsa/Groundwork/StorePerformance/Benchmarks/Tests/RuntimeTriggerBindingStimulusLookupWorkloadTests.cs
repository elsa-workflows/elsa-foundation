using Elsa.Groundwork.StorePerformance.Benchmarks.Workloads;
using Elsa.Workflows.Runtime.Core.Contracts;
using Elsa.Workflows.Runtime.Core.Models;
using Xunit;

namespace Elsa.Groundwork.StorePerformance.Benchmarks.Tests;

public sealed class RuntimeTriggerBindingStimulusLookupWorkloadTests
{
    [Fact]
    public async Task Reproduces_the_exact_catalog_golden_and_traverses_every_required_projection()
    {
        var adapter = new TriggerBindingLookupAdapter();
        var result = await new RuntimeTriggerBindingStimulusLookupWorkload().ExecuteAsync(adapter);

        Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.ExpectedInputFingerprint, result.InputFingerprint);
        Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.ExpectedResultDigest, result.ResultDigest);
        Assert.Equal(ReproducibleWorkloadScenarioCatalog.Get(RuntimeTriggerBindingStimulusLookupWorkload.WorkloadId).OperationSequence, result.ObservableOperations);
        Assert.Equal(
            RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount * RuntimeTriggerBindingStimulusLookupWorkload.BindingsPerPublication,
            adapter.Primary.Bindings.Bindings.Count);
        Assert.Equal(
            RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount * RuntimeTriggerBindingStimulusLookupWorkload.BindingsPerPublication,
            adapter.Secondary.Bindings.Bindings.Count);
        Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount, adapter.Primary.ActivationQueries.Select(query => query.ActivationId).Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.PublicationCount, adapter.Secondary.ActivationQueries.Select(query => query.ActivationId).Distinct(StringComparer.Ordinal).Count());
        Assert.All(adapter.Primary.ActivationQueries, query => Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.PageSize, query.Limit));
        Assert.All(adapter.Secondary.ActivationQueries, query => Assert.Equal(RuntimeTriggerBindingStimulusLookupWorkload.PageSize, query.Limit));
        Assert.True(adapter.Primary.SourcePageQueries.Count >= 5);
        Assert.True(adapter.Secondary.SourcePageQueries.Count >= 5);
    }

    [Fact]
    public async Task Performs_the_frozen_public_operation_order()
    {
        var result = await new RuntimeTriggerBindingStimulusLookupWorkload().ExecuteAsync(new TriggerBindingLookupAdapter());

        Assert.Equal(
            [
                "seed-publications-and-bindings",
                "lookup-active-bindings-by-stimulus-type",
                "load-executable-source-references",
                "verify-publication-and-scope-isolation"
            ],
            result.ObservableOperations);
    }

    [Theory]
    [InlineData(TriggerBindingLookupFault.AliasScopes)]
    [InlineData(TriggerBindingLookupFault.LeakBindingsIntoSecondary)]
    [InlineData(TriggerBindingLookupFault.LeakBindingsIntoPrimary)]
    [InlineData(TriggerBindingLookupFault.LeakSourcesIntoSecondary)]
    [InlineData(TriggerBindingLookupFault.LeakSourcesIntoPrimary)]
    [InlineData(TriggerBindingLookupFault.SwapBindingSourcePairs)]
    [InlineData(TriggerBindingLookupFault.DiscardPreparation)]
    [InlineData(TriggerBindingLookupFault.DropPreparedBinding)]
    [InlineData(TriggerBindingLookupFault.DiscardSourceSave)]
    [InlineData(TriggerBindingLookupFault.IgnoreActivation)]
    [InlineData(TriggerBindingLookupFault.IgnoreReplacement)]
    [InlineData(TriggerBindingLookupFault.IgnoreActivePredicate)]
    [InlineData(TriggerBindingLookupFault.WrongPublicationProjection)]
    [InlineData(TriggerBindingLookupFault.IgnoreStimulusType)]
    [InlineData(TriggerBindingLookupFault.IgnoreStimulusHash)]
    [InlineData(TriggerBindingLookupFault.UseTypeOnlyPredicate)]
    [InlineData(TriggerBindingLookupFault.IgnoreLimit)]
    [InlineData(TriggerBindingLookupFault.MissingContinuation)]
    [InlineData(TriggerBindingLookupFault.RepeatedContinuation)]
    [InlineData(TriggerBindingLookupFault.IgnoreContinuation)]
    [InlineData(TriggerBindingLookupFault.WrongOrder)]
    [InlineData(TriggerBindingLookupFault.AlterBinding)]
    [InlineData(TriggerBindingLookupFault.FabricateTotalCount)]
    [InlineData(TriggerBindingLookupFault.IgnoreSourceLiveOnly)]
    [InlineData(TriggerBindingLookupFault.IgnoreSourceScope)]
    [InlineData(TriggerBindingLookupFault.IgnoreSourceNow)]
    [InlineData(TriggerBindingLookupFault.WrongSourceArtifact)]
    [InlineData(TriggerBindingLookupFault.WrongSourcePublication)]
    [InlineData(TriggerBindingLookupFault.WrongSourceTenant)]
    public async Task Fails_closed_when_public_store_outcomes_drift(TriggerBindingLookupFault fault)
    {
        await Assert.ThrowsAnyAsync<Exception>(() =>
            new RuntimeTriggerBindingStimulusLookupWorkload().ExecuteAsync(new TriggerBindingLookupAdapter(fault)).AsTask());
    }

    [Fact]
    public void Public_adapter_surface_contains_no_provider_or_timing_inputs()
    {
        var types = new[]
        {
            typeof(IRuntimeTriggerBindingStimulusLookupWorkloadAdapter),
            typeof(RuntimeTriggerBindingStimulusLookupScopes),
            typeof(RuntimeTriggerBindingStimulusLookupScope)
        };
        var publicSurface = types
            .SelectMany(type => type.GetMembers(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.DeclaredOnly))
            .Select(member => member.ToString()!)
            .Append(typeof(IRuntimeTriggerBindingStimulusLookupWorkloadAdapter).ToString());

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

    private sealed class TriggerBindingLookupAdapter(TriggerBindingLookupFault fault = TriggerBindingLookupFault.None) : IRuntimeTriggerBindingStimulusLookupWorkloadAdapter
    {
        public MemoryScope Primary { get; } = new(fault, isSecondary: false);
        public MemoryScope Secondary { get; } = new(fault, isSecondary: true);

        public ValueTask<RuntimeTriggerBindingStimulusLookupScopes> OpenIsolatedScopesAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Primary.Bindings.Foreign = Secondary.Bindings;
            Secondary.Bindings.Foreign = Primary.Bindings;
            Primary.Sources.Foreign = Secondary.Sources;
            Secondary.Sources.Foreign = Primary.Sources;
            if (fault == TriggerBindingLookupFault.AliasScopes)
                return new(new RuntimeTriggerBindingStimulusLookupScopes(new(Primary.Bindings, Primary.Sources), new(Primary.Bindings, Primary.Sources)));
            return new(new RuntimeTriggerBindingStimulusLookupScopes(new(Primary.Bindings, Primary.Sources), new(Secondary.Bindings, Secondary.Sources)));
        }
    }

    private sealed class MemoryScope(TriggerBindingLookupFault fault, bool isSecondary)
    {
        public MemoryBindingStore Bindings { get; } = new(fault, isSecondary);
        public MemorySourceStore Sources { get; } = new(fault, isSecondary);
        public IReadOnlyList<WorkflowTriggerBindingActivationPageQuery> ActivationQueries => Bindings.ActivationQueries;
        public IReadOnlyList<WorkflowExecutableSourceReferencePageQuery> SourcePageQueries => Sources.PageQueries;
    }

    private sealed class MemoryBindingStore(TriggerBindingLookupFault fault, bool isSecondary) : IWorkflowTriggerBindingStore
    {
        private readonly Dictionary<string, WorkflowTriggerBinding> _bindings = new(StringComparer.Ordinal);
        private readonly HashSet<string> _prepared = new(StringComparer.Ordinal);
        public MemoryBindingStore? Foreign { get; set; }
        public IReadOnlyCollection<WorkflowTriggerBinding> Bindings => _bindings.Values;
        public List<WorkflowTriggerBindingActivationPageQuery> ActivationQueries { get; } = [];

        public ValueTask<WorkflowTriggerBinding> SaveAsync(WorkflowTriggerBinding binding, CancellationToken cancellationToken = default)
        {
            _bindings[binding.TriggerBindingId] = binding;
            return new(binding);
        }

        public ValueTask PrepareActivationAsync(string activationId, IReadOnlyCollection<WorkflowTriggerBinding> bindings, CancellationToken cancellationToken = default)
        {
            if (fault == TriggerBindingLookupFault.DiscardPreparation)
                return ValueTask.CompletedTask;
            foreach (var existing in _bindings.Values.Where(binding => binding.ActivationId == activationId).Select(binding => binding.TriggerBindingId).ToArray())
                _bindings.Remove(existing);
            var prepared = fault == TriggerBindingLookupFault.DropPreparedBinding ? bindings.SkipLast(1) : bindings;
            foreach (var binding in prepared)
                _bindings[binding.TriggerBindingId] = binding with { IsActive = false };
            _prepared.Add(activationId);
            return ValueTask.CompletedTask;
        }

        public ValueTask ActivateAsync(string activationId, string? replacedActivationId, CancellationToken cancellationToken = default)
        {
            if (!_prepared.Contains(activationId))
                throw new InvalidOperationException("Publication was not prepared.");
            if (fault == TriggerBindingLookupFault.IgnoreActivation)
                return ValueTask.CompletedTask;
            SetActive(activationId, true);
            if (replacedActivationId is not null && fault != TriggerBindingLookupFault.IgnoreReplacement)
                SetActive(replacedActivationId, false);
            return ValueTask.CompletedTask;
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusAsync(WorkflowTriggerBindingPageQuery query, CancellationToken cancellationToken = default) =>
            new(Page(query, SelectForExact(query)));

        public ValueTask<WorkflowTriggerBindingPage> ListByStimulusTypeAsync(WorkflowTriggerBindingTypePageQuery query, CancellationToken cancellationToken = default) =>
            new(Page(query, SelectForType(query)));

        public ValueTask<WorkflowTriggerBindingPage> ListByActivationAsync(WorkflowTriggerBindingActivationPageQuery query, CancellationToken cancellationToken = default)
        {
            ActivationQueries.Add(query);
            var source = fault == TriggerBindingLookupFault.WrongPublicationProjection
                ? _bindings.Values
                : _bindings.Values.Where(binding => binding.ActivationId == query.ActivationId);
            return new(Page(query, source));
        }

        public ValueTask<WorkflowTriggerBindingPage> ListByArtifactAsync(WorkflowTriggerBindingArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            new(Page(query, _bindings.Values.Where(binding => binding.ArtifactId == query.ArtifactId)));

        public ValueTask<int> DeleteByArtifactAsync(string artifactId, CancellationToken cancellationToken = default)
        {
            var ids = _bindings.Values.Where(binding => binding.ArtifactId == artifactId).Select(binding => binding.TriggerBindingId).ToArray();
            foreach (var id in ids)
                _bindings.Remove(id);
            return new(ids.Length);
        }

        public ValueTask DeleteByActivationAsync(string activationId, CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

        private IEnumerable<WorkflowTriggerBinding> SelectForExact(WorkflowTriggerBindingPageQuery query)
        {
            var leaksFromForeign =
                fault == TriggerBindingLookupFault.LeakBindingsIntoSecondary && isSecondary ||
                fault == TriggerBindingLookupFault.LeakBindingsIntoPrimary && !isSecondary;
            var source = leaksFromForeign && Foreign is not null
                ? Foreign._bindings.Values
                : _bindings.Values;
            return source.Where(binding =>
                (fault == TriggerBindingLookupFault.IgnoreStimulusType || binding.StimulusType == query.StimulusType) &&
                (fault is TriggerBindingLookupFault.IgnoreStimulusHash or TriggerBindingLookupFault.UseTypeOnlyPredicate || binding.StimulusHash == query.StimulusHash) &&
                (fault == TriggerBindingLookupFault.IgnoreActivePredicate || binding.IsActive));
        }

        private IEnumerable<WorkflowTriggerBinding> SelectForType(WorkflowTriggerBindingTypePageQuery query) =>
            _bindings.Values.Where(binding => binding.StimulusType == query.StimulusType && (fault == TriggerBindingLookupFault.IgnoreActivePredicate || binding.IsActive));

        private WorkflowTriggerBindingPage Page(WorkflowTriggerBindingPageRequest query, IEnumerable<WorkflowTriggerBinding> candidate)
        {
            var ordered = candidate.OrderBy(binding => binding.TriggerBindingId, StringComparer.Ordinal).ToList();
            if (fault == TriggerBindingLookupFault.WrongOrder)
                ordered.Reverse();
            var offset = fault == TriggerBindingLookupFault.IgnoreContinuation ? 0 : ParseOffset(query.ContinuationToken);
            var limit = fault == TriggerBindingLookupFault.IgnoreLimit ? int.MaxValue : query.Limit;
            var items = ordered.Skip(offset).Take(limit).Select(binding => binding with { }).ToArray();
            if (fault == TriggerBindingLookupFault.AlterBinding && items.Length > 0)
                items[0] = items[0] with { ArtifactHash = "sha256:altered" };
            var nextOffset = offset + items.Length;
            var next = nextOffset < ordered.Count && fault != TriggerBindingLookupFault.MissingContinuation ? $"offset:{nextOffset}" : null;
            if (fault == TriggerBindingLookupFault.RepeatedContinuation && next is not null)
                next = query.ContinuationToken is null ? "repeat:0" : "repeat:0";
            return new WorkflowTriggerBindingPage(query, items, fault == TriggerBindingLookupFault.FabricateTotalCount ? ordered.Count + 1 : ordered.Count, next);
        }

        private void SetActive(string activationId, bool active)
        {
            foreach (var binding in _bindings.Values.Where(binding => binding.ActivationId == activationId).ToArray())
                _bindings[binding.TriggerBindingId] = binding with { IsActive = active };
        }

        private static int ParseOffset(string? token)
        {
            if (token is null)
                return 0;
            return token.StartsWith("offset:", StringComparison.Ordinal) && int.TryParse(token.AsSpan("offset:".Length), out var offset)
                ? offset
                : 0;
        }
    }

    private sealed class MemorySourceStore(TriggerBindingLookupFault fault, bool isSecondary) : IWorkflowExecutableSourceReferenceStore
    {
        private readonly Dictionary<string, WorkflowExecutableSourceReference> _references = new(StringComparer.Ordinal);
        public MemorySourceStore? Foreign { get; set; }
        public List<WorkflowExecutableSourceReferencePageQuery> PageQueries { get; } = [];

        public ValueTask SaveAsync(WorkflowExecutableSourceReference reference, CancellationToken cancellationToken = default)
        {
            if (fault != TriggerBindingLookupFault.DiscardSourceSave)
                _references[reference.SourceReferenceId] = reference;
            return ValueTask.CompletedTask;
        }

        public ValueTask<WorkflowExecutableSourceReference?> FindAsync(string sourceReferenceId, CancellationToken cancellationToken = default) =>
            new(_references.GetValueOrDefault(sourceReferenceId));

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListByArtifactPageAsync(WorkflowExecutableSourceReferenceArtifactPageQuery query, CancellationToken cancellationToken = default) =>
            new(new RuntimeStorePage<WorkflowExecutableSourceReference>(query, _references.Values.Where(reference => reference.ArtifactId == query.ArtifactId).Take(query.Limit).ToArray()));

        public ValueTask<RuntimeStorePage<WorkflowExecutableSourceReference>> ListPageAsync(WorkflowExecutableSourceReferencePageQuery query, CancellationToken cancellationToken = default)
        {
            PageQueries.Add(query);
            var leaksFromForeign =
                fault == TriggerBindingLookupFault.LeakSourcesIntoSecondary && isSecondary ||
                fault == TriggerBindingLookupFault.LeakSourcesIntoPrimary && !isSecondary;
            var source = leaksFromForeign && Foreign is not null ? Foreign._references.Values : _references.Values;
            IEnumerable<WorkflowExecutableSourceReference> selected = source;
            if (query.Scope is not null && fault != TriggerBindingLookupFault.IgnoreSourceScope)
                selected = selected.Where(reference => reference.Scope == query.Scope);
            if (query.LiveOnly && fault != TriggerBindingLookupFault.IgnoreSourceLiveOnly)
                selected = selected.Where(reference => reference.IsLive(fault == TriggerBindingLookupFault.IgnoreSourceNow ? query.Now!.Value.AddDays(-2) : query.Now!.Value));
            var ordered = selected.OrderBy(reference => reference.SourceReferenceId, StringComparer.Ordinal).ToArray();
            var offset = ParseOffset(query.ContinuationToken);
            var items = ordered.Skip(offset).Take(query.Limit).Select(reference => reference with { }).ToArray();
            if (items.Length > 0)
            {
                if (fault == TriggerBindingLookupFault.SwapBindingSourcePairs || fault == TriggerBindingLookupFault.WrongSourceArtifact)
                    items[0] = items[0] with { ArtifactId = "artifact-wrong" };
                if (fault == TriggerBindingLookupFault.WrongSourcePublication)
                    items[0] = items[0] with { ActivationId = "publication-wrong" };
                if (fault == TriggerBindingLookupFault.WrongSourceTenant)
                    items[0] = items[0] with { TenantId = "tenant-wrong" };
            }
            var next = offset + items.Length < ordered.Length ? $"offset:{offset + items.Length}" : null;
            return new(new RuntimeStorePage<WorkflowExecutableSourceReference>(query, items, next));
        }

        public ValueTask<IReadOnlyCollection<string>> ListUnreferencedArtifactIdsAsync(WorkflowExecutableArtifactCandidateBatch candidates, DateTimeOffset now, CancellationToken cancellationToken = default) =>
            new((IReadOnlyCollection<string>)[]);

        public ValueTask<bool> RetireAsync(string sourceReferenceId, DateTimeOffset deletedAt, string? reason = null, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<bool> DeleteAsync(string sourceReferenceId, CancellationToken cancellationToken = default) => new(false);
        public ValueTask<IReadOnlyCollection<string>> DeleteExpiredOrRetiredAsync(WorkflowExecutableSourceReferenceCleanupBatch batch, DateTimeOffset now, CancellationToken cancellationToken = default) => new((IReadOnlyCollection<string>)[]);

        private static int ParseOffset(string? token) =>
            token is not null && token.StartsWith("offset:", StringComparison.Ordinal) && int.TryParse(token.AsSpan("offset:".Length), out var offset) ? offset : 0;
    }
}

public enum TriggerBindingLookupFault
{
    None,
    AliasScopes,
    LeakBindingsIntoSecondary,
    LeakBindingsIntoPrimary,
    LeakSourcesIntoSecondary,
    LeakSourcesIntoPrimary,
    SwapBindingSourcePairs,
    DiscardPreparation,
    DropPreparedBinding,
    DiscardSourceSave,
    IgnoreActivation,
    IgnoreReplacement,
    IgnoreActivePredicate,
    WrongPublicationProjection,
    IgnoreStimulusType,
    IgnoreStimulusHash,
    UseTypeOnlyPredicate,
    IgnoreLimit,
    MissingContinuation,
    RepeatedContinuation,
    IgnoreContinuation,
    WrongOrder,
    AlterBinding,
    FabricateTotalCount,
    IgnoreSourceLiveOnly,
    IgnoreSourceScope,
    IgnoreSourceNow,
    WrongSourceArtifact,
    WrongSourcePublication,
    WrongSourceTenant
}
