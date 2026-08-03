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
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

public sealed class GroundworkOpenIddictApplicationStoreTests
{
    private readonly FakeGroundworkDocumentStore _documentStore = new();
    private readonly GroundworkOpenIddictApplicationStore _store;

    public GroundworkOpenIddictApplicationStoreTests()
    {
        _store = new GroundworkOpenIddictApplicationStore(_documentStore, _documentStore);
    }

    [Fact]
    public async Task Create_then_find_by_id_round_trips_the_application()
    {
        var application = new OpenIddictGroundworkApplication { ClientId = "client-a" };

        await _store.CreateAsync(application, CancellationToken.None);
        var found = await _store.FindByIdAsync(application.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(application.Id, found!.Id);
        Assert.Equal("client-a", found.ClientId);
    }

    [Fact]
    public async Task Create_rejects_a_duplicate_client_id_via_the_declared_unique_index()
    {
        await _store.CreateAsync(new OpenIddictGroundworkApplication { ClientId = "duplicate" }, CancellationToken.None);
        var second = new OpenIddictGroundworkApplication { ClientId = "duplicate" };

        await Assert.ThrowsAsync<OpenIddictGroundworkProviderException>(() =>
            _store.CreateAsync(second, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task Scalar_accessors_round_trip_through_get_and_set()
    {
        var application = new OpenIddictGroundworkApplication();

        await _store.SetClientIdAsync(application, "client-id", CancellationToken.None);
        await _store.SetClientSecretAsync(application, "client-secret", CancellationToken.None);
        await _store.SetClientTypeAsync(application, "confidential", CancellationToken.None);
        await _store.SetConsentTypeAsync(application, "explicit", CancellationToken.None);
        await _store.SetApplicationTypeAsync(application, "web", CancellationToken.None);
        await _store.SetDisplayNameAsync(application, "Display Name", CancellationToken.None);

        Assert.Equal(application.Id, await _store.GetIdAsync(application, CancellationToken.None));
        Assert.Equal("client-id", await _store.GetClientIdAsync(application, CancellationToken.None));
        Assert.Equal("client-secret", await _store.GetClientSecretAsync(application, CancellationToken.None));
        Assert.Equal("confidential", await _store.GetClientTypeAsync(application, CancellationToken.None));
        Assert.Equal("explicit", await _store.GetConsentTypeAsync(application, CancellationToken.None));
        Assert.Equal("web", await _store.GetApplicationTypeAsync(application, CancellationToken.None));
        Assert.Equal("Display Name", await _store.GetDisplayNameAsync(application, CancellationToken.None));
    }

    [Fact]
    public async Task Localized_display_names_round_trip_through_get_and_set()
    {
        var application = new OpenIddictGroundworkApplication();
        var displayNames = new Dictionary<CultureInfo, string> { [CultureInfo.GetCultureInfo("fr-FR")] = "Nom Francais" }
            .ToImmutableDictionary();

        await _store.SetDisplayNamesAsync(application, displayNames, CancellationToken.None);
        var read = await _store.GetDisplayNamesAsync(application, CancellationToken.None);

        Assert.Equal("Nom Francais", read[CultureInfo.GetCultureInfo("fr-FR")]);
    }

    [Fact]
    public async Task Collection_accessors_round_trip_through_get_and_set()
    {
        var application = new OpenIddictGroundworkApplication();
        var permissions = ImmutableArray.Create("permission-a", "permission-b");
        var redirectUris = ImmutableArray.Create("https://a.example/callback", "https://b.example/callback");
        var postLogoutUris = ImmutableArray.Create("https://a.example/logout");
        var requirements = ImmutableArray.Create("requirement-a");

        await _store.SetPermissionsAsync(application, permissions, CancellationToken.None);
        await _store.SetRedirectUrisAsync(application, redirectUris, CancellationToken.None);
        await _store.SetPostLogoutRedirectUrisAsync(application, postLogoutUris, CancellationToken.None);
        await _store.SetRequirementsAsync(application, requirements, CancellationToken.None);

        Assert.Equal(permissions.ToArray(), (await _store.GetPermissionsAsync(application, CancellationToken.None)).ToArray());
        Assert.Equal(redirectUris.ToArray(), (await _store.GetRedirectUrisAsync(application, CancellationToken.None)).ToArray());
        Assert.Equal(postLogoutUris.ToArray(), (await _store.GetPostLogoutRedirectUrisAsync(application, CancellationToken.None)).ToArray());
        Assert.Equal(requirements.ToArray(), (await _store.GetRequirementsAsync(application, CancellationToken.None)).ToArray());
    }

    [Fact]
    public async Task Properties_and_settings_round_trip_through_get_and_set()
    {
        var application = new OpenIddictGroundworkApplication();
        using var property = JsonDocument.Parse("{\"tier\":\"gold\"}");
        var properties = new Dictionary<string, JsonElement> { ["feature"] = property.RootElement.Clone() }.ToImmutableDictionary();
        var settings = new Dictionary<string, string> { ["setting-a"] = "value-a" }.ToImmutableDictionary();

        await _store.SetPropertiesAsync(application, properties, CancellationToken.None);
        await _store.SetSettingsAsync(application, settings, CancellationToken.None);

        var readProperties = await _store.GetPropertiesAsync(application, CancellationToken.None);
        var readSettings = await _store.GetSettingsAsync(application, CancellationToken.None);

        Assert.True(readProperties["feature"].GetProperty("tier").GetString() == "gold");
        Assert.Equal("value-a", readSettings["setting-a"]);
    }

    [Fact]
    public async Task Json_web_key_set_round_trips_through_get_and_set()
    {
        var application = new OpenIddictGroundworkApplication();
        var keySet = JsonWebKeySet.Create("{\"keys\":[{\"kty\":\"RSA\",\"kid\":\"key-1\"}]}");

        await _store.SetJsonWebKeySetAsync(application, keySet, CancellationToken.None);
        var read = await _store.GetJsonWebKeySetAsync(application, CancellationToken.None);

        Assert.NotNull(read);
        Assert.Single(read!.Keys);
        Assert.Equal("key-1", read.Keys[0].Kid);
    }

    [Fact]
    public async Task Json_web_key_set_is_null_when_unset()
    {
        var application = new OpenIddictGroundworkApplication();

        Assert.Null(await _store.GetJsonWebKeySetAsync(application, CancellationToken.None));

        await _store.SetJsonWebKeySetAsync(application, null, CancellationToken.None);

        Assert.Null(await _store.GetJsonWebKeySetAsync(application, CancellationToken.None));
    }

    [Fact]
    public async Task Empty_collections_and_maps_round_trip_to_empty_immutable_collections()
    {
        var application = new OpenIddictGroundworkApplication();

        Assert.Empty(await _store.GetPermissionsAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetRedirectUrisAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetPostLogoutRedirectUrisAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetRequirementsAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetPropertiesAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetSettingsAsync(application, CancellationToken.None));
        Assert.Empty(await _store.GetDisplayNamesAsync(application, CancellationToken.None));
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
    public async Task FindByClientIdAsync_resolves_through_the_declared_named_route()
    {
        await _store.CreateAsync(new OpenIddictGroundworkApplication { ClientId = "resolvable" }, CancellationToken.None);

        var found = await _store.FindByClientIdAsync("resolvable", CancellationToken.None);
        var missing = await _store.FindByClientIdAsync("does-not-exist", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("resolvable", found!.ClientId);
        Assert.Null(missing);
    }

    [Fact]
    public async Task FindByRedirectUriAsync_resolves_through_the_declared_collection_route()
    {
        await _store.CreateAsync(
            new OpenIddictGroundworkApplication { ClientId = "with-uri", RedirectUris = ["https://shared.example/callback"] },
            CancellationToken.None);
        await _store.CreateAsync(
            new OpenIddictGroundworkApplication { ClientId = "without-uri", RedirectUris = ["https://other.example/callback"] },
            CancellationToken.None);

        var found = new List<OpenIddictGroundworkApplication>();
        await foreach (var application in _store.FindByRedirectUriAsync("https://shared.example/callback", CancellationToken.None))
            found.Add(application);

        Assert.Single(found);
        Assert.Equal("with-uri", found[0].ClientId);
    }

    [Fact]
    public async Task FindByPostLogoutRedirectUriAsync_resolves_through_the_declared_collection_route()
    {
        await _store.CreateAsync(
            new OpenIddictGroundworkApplication { ClientId = "with-logout-uri", PostLogoutRedirectUris = ["https://shared.example/logout"] },
            CancellationToken.None);
        await _store.CreateAsync(
            new OpenIddictGroundworkApplication { ClientId = "without-logout-uri", PostLogoutRedirectUris = ["https://other.example/logout"] },
            CancellationToken.None);

        var found = new List<OpenIddictGroundworkApplication>();
        await foreach (var application in _store.FindByPostLogoutRedirectUriAsync("https://shared.example/logout", CancellationToken.None))
            found.Add(application);

        Assert.Single(found);
        Assert.Equal("with-logout-uri", found[0].ClientId);
    }

    [Fact]
    public async Task Update_is_a_successful_compare_and_swap_that_rotates_the_concurrency_token()
    {
        var application = new OpenIddictGroundworkApplication { ClientId = "updatable" };
        await _store.CreateAsync(application, CancellationToken.None);
        var originalToken = application.ConcurrencyToken;

        await _store.SetDisplayNameAsync(application, "Updated", CancellationToken.None);
        await _store.UpdateAsync(application, CancellationToken.None);

        Assert.NotEqual(originalToken, application.ConcurrencyToken);
        var reloaded = await _store.FindByIdAsync(application.Id, CancellationToken.None);
        Assert.Equal("Updated", reloaded!.DisplayName);
        Assert.Equal(application.ConcurrencyToken, reloaded.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_with_a_stale_persistence_version_raises_a_concurrency_exception()
    {
        var application = new OpenIddictGroundworkApplication { ClientId = "contested" };
        await _store.CreateAsync(application, CancellationToken.None);

        var staleCopy = await _store.FindByIdAsync(application.Id, CancellationToken.None);
        var freshCopy = await _store.FindByIdAsync(application.Id, CancellationToken.None);
        Assert.NotNull(staleCopy);
        Assert.NotNull(freshCopy);

        await _store.UpdateAsync(freshCopy!, CancellationToken.None);

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            _store.UpdateAsync(staleCopy!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DeleteAsync_removes_the_application()
    {
        var application = new OpenIddictGroundworkApplication { ClientId = "removable" };
        await _store.CreateAsync(application, CancellationToken.None);

        await _store.DeleteAsync(application, CancellationToken.None);

        Assert.Null(await _store.FindByIdAsync(application.Id, CancellationToken.None));
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
        await _store.CreateAsync(new OpenIddictGroundworkApplication { ClientId = "one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.CountAsync(CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal("application.CountAsync", exception.Operation);
    }

    [Fact]
    public async Task ListAsync_is_rejected_because_no_bounded_list_all_route_is_declared()
    {
        await _store.CreateAsync(new OpenIddictGroundworkApplication { ClientId = "list-one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var application in _store.ListAsync(null, null, CancellationToken.None))
                _ = application;
        });

        Assert.Equal("application.ListAsync", exception.Operation);
    }

    [Fact]
    public async Task CountAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.CountAsync(applications => applications.Select(application => application.Id), CancellationToken.None).AsTask());

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    [Fact]
    public async Task GetAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.GetAsync(
                    (applications, state) => applications.Where(application => application.ClientId == state).Select(application => application.Id),
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
                (applications, state) => applications.Where(application => application.ClientId == state).Select(application => application.Id),
                "irrelevant",
                CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    /// <summary>
    /// Minimal in-memory <see cref="IDocumentStore"/>/<see cref="IBoundedDocumentStore"/> test double. It
    /// implements only the surface <see cref="GroundworkOpenIddictApplicationStore"/> exercises: point load/
    /// save/delete, the declared "clientId" unique index (enforced as a <see cref="DocumentStoreWriteStatus.IdentityConflict"/>
    /// on save, mirroring the physical unique index the manifest declares on <c>openiddict_applications</c>),
    /// and the declared "redirectUris"/"postLogoutRedirectUris" collection indexes. It deliberately avoids
    /// Groundwork.Sqlite/Testcontainers-backed fixtures so this suite stays hermetic and fast. Modelled on
    /// <see cref="GroundworkOpenIddictScopeStoreTests"/>'s fake of the same name; no new operators were needed
    /// beyond what that fake already supports (<c>Equal</c> and <c>CollectionContains</c>).
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
            throw new NotSupportedException("The application store never opens a unit of work.");

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

                    var conflict = FindByField(request.DocumentKind, "clientId", request.ContentJson, excludeId: request.Id);
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
            throw new NotSupportedException("Not exercised by the application store.");

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the application store.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the application store.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the application store.");
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
                QueryComparisonOperator.In => property.ValueKind == JsonValueKind.String &&
                    comparison.Values.Contains(property.GetString(), StringComparer.Ordinal),
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
