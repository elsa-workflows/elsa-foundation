using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class GroundworkOpenIddictScopeStoreTests
{
    private readonly FakeGroundworkDocumentStore _documentStore = new();
    private readonly GroundworkOpenIddictScopeStore _store;

    public GroundworkOpenIddictScopeStoreTests()
    {
        _store = new GroundworkOpenIddictScopeStore(_documentStore, _documentStore);
    }

    [Fact]
    public async Task Create_then_find_by_id_round_trips_the_scope()
    {
        var scope = new OpenIddictGroundworkScope { Name = "api" };

        await _store.CreateAsync(scope, CancellationToken.None);
        var found = await _store.FindByIdAsync(scope.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(scope.Id, found!.Id);
        Assert.Equal("api", found.Name);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_name_via_the_declared_unique_index()
    {
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "duplicate" }, CancellationToken.None);
        var second = new OpenIddictGroundworkScope { Name = "duplicate" };

        await Assert.ThrowsAsync<OpenIddictGroundworkProviderException>(() =>
            _store.CreateAsync(second, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Scalar_accessors_round_trip_through_get_and_set()
    {
        var scope = new OpenIddictGroundworkScope();

        await _store.SetNameAsync(scope, "scope-name", CancellationToken.None);
        await _store.SetDisplayNameAsync(scope, "Scope Display", CancellationToken.None);
        await _store.SetDescriptionAsync(scope, "Scope description", CancellationToken.None);

        Assert.Equal(scope.Id, await _store.GetIdAsync(scope, CancellationToken.None));
        Assert.Equal("scope-name", await _store.GetNameAsync(scope, CancellationToken.None));
        Assert.Equal("Scope Display", await _store.GetDisplayNameAsync(scope, CancellationToken.None));
        Assert.Equal("Scope description", await _store.GetDescriptionAsync(scope, CancellationToken.None));
    }

    [Fact]
    public async Task Localized_collection_accessors_round_trip_through_get_and_set()
    {
        var scope = new OpenIddictGroundworkScope();
        var descriptions = new Dictionary<CultureInfo, string> { [CultureInfo.GetCultureInfo("en-US")] = "English description" }
            .ToImmutableDictionary();
        var displayNames = new Dictionary<CultureInfo, string> { [CultureInfo.GetCultureInfo("fr-FR")] = "Nom Francais" }
            .ToImmutableDictionary();

        await _store.SetDescriptionsAsync(scope, descriptions, CancellationToken.None);
        await _store.SetDisplayNamesAsync(scope, displayNames, CancellationToken.None);

        var readDescriptions = await _store.GetDescriptionsAsync(scope, CancellationToken.None);
        var readDisplayNames = await _store.GetDisplayNamesAsync(scope, CancellationToken.None);

        Assert.Equal("English description", readDescriptions[CultureInfo.GetCultureInfo("en-US")]);
        Assert.Equal("Nom Francais", readDisplayNames[CultureInfo.GetCultureInfo("fr-FR")]);
    }

    [Fact]
    public async Task Properties_and_resources_round_trip_through_get_and_set()
    {
        var scope = new OpenIddictGroundworkScope();
        using var property = JsonDocument.Parse("{\"tier\":\"gold\"}");
        var properties = new Dictionary<string, JsonElement> { ["feature"] = property.RootElement.Clone() }.ToImmutableDictionary();
        var resources = ImmutableArray.Create("resource-a", "resource-b");

        await _store.SetPropertiesAsync(scope, properties, CancellationToken.None);
        await _store.SetResourcesAsync(scope, resources, CancellationToken.None);

        var readProperties = await _store.GetPropertiesAsync(scope, CancellationToken.None);
        var readResources = await _store.GetResourcesAsync(scope, CancellationToken.None);

        Assert.True(readProperties["feature"].GetProperty("tier").GetString() == "gold");
        Assert.Equal(new[] { "resource-a", "resource-b" }, readResources.ToArray());
    }

    [Fact]
    public async Task Empty_resources_and_properties_round_trip_to_empty_immutable_collections()
    {
        var scope = new OpenIddictGroundworkScope();

        Assert.Empty(await _store.GetResourcesAsync(scope, CancellationToken.None));
        Assert.Empty(await _store.GetPropertiesAsync(scope, CancellationToken.None));
        Assert.Empty(await _store.GetDescriptionsAsync(scope, CancellationToken.None));
        Assert.Empty(await _store.GetDisplayNamesAsync(scope, CancellationToken.None));
    }

    [Fact]
    public async Task InstantiateAsync_returns_a_fresh_unpersisted_descriptor()
    {
        var first = await _store.InstantiateAsync(CancellationToken.None);
        var second = await _store.InstantiateAsync(CancellationToken.None);

        Assert.NotEqual(first.Id, second.Id);
        Assert.NotEqual(first.ConcurrencyToken, second.ConcurrencyToken);
        Assert.Equal(0, first.PersistenceVersion);
    }

    [Fact]
    public async Task FindByNameAsync_resolves_through_the_declared_named_route()
    {
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "resolvable" }, CancellationToken.None);

        var found = await _store.FindByNameAsync("resolvable", CancellationToken.None);
        var missing = await _store.FindByNameAsync("does-not-exist", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("resolvable", found!.Name);
        Assert.Null(missing);
    }

    [Fact]
    public async Task FindByNamesAsync_resolves_a_finite_name_set_via_repeated_point_lookups()
    {
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "alpha" }, CancellationToken.None);
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "beta" }, CancellationToken.None);

        var found = new List<OpenIddictGroundworkScope>();
        await foreach (var scope in _store.FindByNamesAsync(["alpha", "beta", "missing"], CancellationToken.None))
            found.Add(scope);

        Assert.Equal(2, found.Count);
        Assert.Contains(found, scope => scope.Name == "alpha");
        Assert.Contains(found, scope => scope.Name == "beta");
    }

    [Fact]
    public async Task FindByResourceAsync_resolves_through_the_declared_collection_route()
    {
        await _store.CreateAsync(
            new OpenIddictGroundworkScope { Name = "with-resource", Resources = ["shared-resource"] },
            CancellationToken.None);
        await _store.CreateAsync(
            new OpenIddictGroundworkScope { Name = "without-resource", Resources = ["other-resource"] },
            CancellationToken.None);

        var found = new List<OpenIddictGroundworkScope>();
        await foreach (var scope in _store.FindByResourceAsync("shared-resource", CancellationToken.None))
            found.Add(scope);

        Assert.Single(found);
        Assert.Equal("with-resource", found[0].Name);
    }

    [Fact]
    public async Task Update_is_a_successful_compare_and_swap_that_rotates_the_concurrency_token()
    {
        var scope = new OpenIddictGroundworkScope { Name = "updatable" };
        await _store.CreateAsync(scope, CancellationToken.None);
        var originalToken = scope.ConcurrencyToken;

        await _store.SetDisplayNameAsync(scope, "Updated", CancellationToken.None);
        await _store.UpdateAsync(scope, CancellationToken.None);

        Assert.NotEqual(originalToken, scope.ConcurrencyToken);
        var reloaded = await _store.FindByIdAsync(scope.Id, CancellationToken.None);
        Assert.Equal("Updated", reloaded!.DisplayName);
        Assert.Equal(scope.ConcurrencyToken, reloaded.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_with_a_stale_persistence_version_raises_a_concurrency_exception()
    {
        var scope = new OpenIddictGroundworkScope { Name = "contested" };
        await _store.CreateAsync(scope, CancellationToken.None);

        var staleCopy = await _store.FindByIdAsync(scope.Id, CancellationToken.None);
        var freshCopy = await _store.FindByIdAsync(scope.Id, CancellationToken.None);
        Assert.NotNull(staleCopy);
        Assert.NotNull(freshCopy);

        await _store.UpdateAsync(freshCopy!, CancellationToken.None);

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            _store.UpdateAsync(staleCopy!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DeleteAsync_removes_the_scope()
    {
        var scope = new OpenIddictGroundworkScope { Name = "removable" };
        await _store.CreateAsync(scope, CancellationToken.None);

        await _store.DeleteAsync(scope, CancellationToken.None);

        Assert.Null(await _store.FindByIdAsync(scope.Id, CancellationToken.None));
    }

    /// <summary>
    /// Count-all and list-all are rejected, not degraded. The manifest declares no bounded route for
    /// either, and the portable query surface that could answer them is forbidden in production Groundwork
    /// reads by <c>ArchitectureGuardTests.Groundwork_production_reads_use_only_admitted_bounded_query_APIs</c>
    /// — with no declared id index it would also page in unguaranteed order. These assertions exist so the
    /// missing routes stay visible; when the manifest declares them, replace these with real behaviour
    /// tests rather than deleting them.
    /// </summary>
    [Fact]
    public async Task CountAsync_is_rejected_because_no_bounded_count_all_route_is_declared()
    {
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.CountAsync(CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal("scope.CountAsync", exception.Operation);
    }

    [Fact]
    public async Task ListAsync_is_rejected_because_no_bounded_list_all_route_is_declared()
    {
        await _store.CreateAsync(new OpenIddictGroundworkScope { Name = "list-one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var scope in _store.ListAsync(null, null, CancellationToken.None))
                _ = scope;
        });

        Assert.Equal("scope.ListAsync", exception.Operation);
    }

    [Fact]
    public async Task CountAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.CountAsync(scopes => scopes.Select(scope => scope.Id), CancellationToken.None).AsTask());

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    [Fact]
    public async Task GetAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.GetAsync(
                    (scopes, state) => scopes.Where(scope => scope.Name == state).Select(scope => scope.Id),
                    "irrelevant",
                    CancellationToken.None)
                .AsTask());

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    [Fact]
    public void ListAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = Assert.Throws<OpenIddictGroundworkCapabilityException>(() =>
            _store.ListAsync(
                (scopes, state) => scopes.Where(scope => scope.Name == state).Select(scope => scope.Id),
                "irrelevant",
                CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IDocumentStore"/>/<see cref="IBoundedDocumentStore"/> test double. It
    /// implements only the surface <see cref="GroundworkOpenIddictScopeStore"/> exercises: point load/save/
    /// delete, the declared "name" unique index (enforced as an <see cref="DocumentStoreWriteStatus.IdentityConflict"/>
    /// on save, mirroring the physical unique index the manifest declares on <c>openiddict_scopes</c>), the
    /// declared "resources" collection index, and the provider-executed unfiltered portable-query surface used
    /// for the plain Count/List members. It deliberately avoids Groundwork.Sqlite/Testcontainers-backed
    /// fixtures so this suite stays hermetic and fast.
    /// </summary>
    private sealed class FakeGroundworkDocumentStore : IDocumentStore, IBoundedDocumentStore
    {
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope> _documents = new();
        private readonly object _gate = new();

        public int SaveCount { get; private set; }
        public int LoadCount { get; private set; }
        public int QueryCount { get; private set; }

        public DocumentStoreAccess Access => DocumentStoreAccess.Global;

        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("The scope store never opens a unit of work.");

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                SaveCount++;
                var key = (request.DocumentKind, request.Id);
                _documents.TryGetValue(key, out var existing);

                if (existing is null)
                {
                    if (request.ExpectedVersion is { } expected && expected != 0)
                        return Task.FromResult(DocumentStoreWriteResult.NotFound);

                    var conflict = FindByField(request.DocumentKind, "name", request.ContentJson, excludeId: request.Id);
                    if (conflict is not null)
                        return Task.FromResult(DocumentStoreWriteResult.IdentityConflict(conflict.Id));
                }
                else if (request.ExpectedVersion is { } expectedVersion && existing.Version != expectedVersion)
                {
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
                }

                var version = (existing?.Version ?? 0) + 1;
                var now = DateTimeOffset.UtcNow;
                var envelope = new DocumentEnvelope(
                    request.DocumentKind,
                    request.Id,
                    request.SchemaVersion,
                    version,
                    request.ContentJson,
                    existing?.CreatedAt ?? now,
                    now);
                _documents[key] = envelope;
                return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
            }
        }

        public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                LoadCount++;
                return Task.FromResult(_documents.TryGetValue((documentKind, id), out var envelope) ? envelope : null);
            }
        }

        public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                var key = (request.DocumentKind, request.Id);
                if (!_documents.TryGetValue(key, out var existing))
                    return Task.FromResult(DocumentStoreWriteResult.NotFound);
                if (request.ExpectedVersion is { } expected && existing.Version != expected)
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

                _documents.Remove(key);
                return Task.FromResult(DocumentStoreWriteResult.Deleted(request.Id));
            }
        }

#pragma warning disable GW0004
        public Task<IReadOnlyList<DocumentEnvelope>> QueryAsync(DocumentStoreQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the scope store.");

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                QueryCount++;
                var matches = _documents.Values.Where(d => d.DocumentKind == query.DocumentKind).ToArray();
                var skip = query.Skip ?? 0;
                IEnumerable<DocumentEnvelope> page = matches.Skip(skip);
                if (query.Take is { } take)
                    page = page.Take(take);
                return Task.FromResult(new DocumentQueryResult(page.ToArray(), matches.Length));
            }
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the scope store.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the scope store.");
#pragma warning restore GW0004

        public Task<DocumentQueryResult> QueryAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
            {
                QueryCount++;
                var matches = Filter(query).ToArray();
                var skip = query.Skip ?? 0;
                IEnumerable<DocumentEnvelope> page = matches.Skip(skip);
                if (query.Take is { } take)
                    page = page.Take(take);
                return Task.FromResult(new DocumentQueryResult(page.ToArray(), matches.Length));
            }
        }

        public Task<long> CountAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return Task.FromResult((long)Filter(query).Count());
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return Task.FromResult(Filter(query).FirstOrDefault());
        }

        public Task<bool> AnyAsync(DocumentQuery query, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return Task.FromResult(Filter(query).Any());
        }

        private IEnumerable<DocumentEnvelope> Filter(DocumentQuery query)
        {
            IEnumerable<DocumentEnvelope> candidates = _documents.Values.Where(d => d.DocumentKind == query.DocumentKind);
            foreach (var clause in query.Clauses)
                candidates = candidates.Where(document => clause.Comparisons.Any(comparison => Matches(document, comparison)));
            return candidates;
        }

        private static bool Matches(DocumentEnvelope envelope, DocumentQueryComparison comparison)
        {
            using var document = JsonDocument.Parse(envelope.ContentJson);
            if (!document.RootElement.TryGetProperty(comparison.Path, out var property))
                return false;

            return comparison.Operator switch
            {
                QueryComparisonOperator.Equal => property.ValueKind == JsonValueKind.String &&
                    string.Equals(property.GetString(), comparison.Values[0], StringComparison.Ordinal),
                QueryComparisonOperator.CollectionContains => property.ValueKind == JsonValueKind.Array &&
                    property.EnumerateArray().Any(element =>
                        element.ValueKind == JsonValueKind.String &&
                        string.Equals(element.GetString(), comparison.Values[0], StringComparison.Ordinal)),
                _ => throw new NotSupportedException($"Operator '{comparison.Operator}' is not supported by the test double.")
            };
        }

        private DocumentEnvelope? FindByField(string documentKind, string fieldPath, string contentJson, string excludeId)
        {
            using var document = JsonDocument.Parse(contentJson);
            if (!document.RootElement.TryGetProperty(fieldPath, out var property) || property.ValueKind != JsonValueKind.String)
                return null;

            var value = property.GetString();
            return _documents.Values.FirstOrDefault(candidate =>
                candidate.DocumentKind == documentKind &&
                candidate.Id != excludeId &&
                MatchesField(candidate.ContentJson, fieldPath, value));
        }

        private static bool MatchesField(string contentJson, string fieldPath, string? value)
        {
            using var document = JsonDocument.Parse(contentJson);
            return document.RootElement.TryGetProperty(fieldPath, out var property) &&
                property.ValueKind == JsonValueKind.String &&
                string.Equals(property.GetString(), value, StringComparison.Ordinal);
        }
    }
}
