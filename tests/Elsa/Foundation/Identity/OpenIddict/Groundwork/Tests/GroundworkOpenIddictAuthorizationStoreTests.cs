using System.Collections.Immutable;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork.Scoping;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Documents.UnitOfWork;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

/// <summary>
/// Covers <see cref="GroundworkOpenIddictAuthorizationStore"/>: CRUD/CAS, every scalar and collection
/// accessor, the two genuinely-servable lookup shapes (subject-only and scopes-only), the rejected shapes
/// (compound/application-id/count-all/list-all/prune), and the revoke family's exactly-once behaviour through
/// <see cref="OpenIddictGroundworkAtomicWrite"/>.
/// </summary>
public sealed class GroundworkOpenIddictAuthorizationStoreTests
{
    private readonly FakeGroundworkDocumentStore _documentStore = new();
    private readonly GroundworkOpenIddictAuthorizationStore _store;

    public GroundworkOpenIddictAuthorizationStoreTests()
    {
        _store = new GroundworkOpenIddictAuthorizationStore(_documentStore, AtomicWrite(_documentStore), _documentStore);
    }

    [Fact]
    public async Task Create_then_find_by_id_round_trips_the_authorization()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "bob" };

        await _store.CreateAsync(authorization, CancellationToken.None);
        var found = await _store.FindByIdAsync(authorization.Id, CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal(authorization.Id, found!.Id);
        Assert.Equal("bob", found.Subject);
    }

    [Fact]
    public async Task Scalar_accessors_round_trip_through_get_and_set()
    {
        var authorization = new OpenIddictGroundworkAuthorization();
        var creationDate = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        await _store.SetApplicationIdAsync(authorization, "app-1", CancellationToken.None);
        await _store.SetSubjectAsync(authorization, "bob", CancellationToken.None);
        await _store.SetStatusAsync(authorization, OpenIddictConstants.Statuses.Valid, CancellationToken.None);
        await _store.SetTypeAsync(authorization, OpenIddictConstants.AuthorizationTypes.Permanent, CancellationToken.None);
        await _store.SetCreationDateAsync(authorization, creationDate, CancellationToken.None);

        Assert.Equal(authorization.Id, await _store.GetIdAsync(authorization, CancellationToken.None));
        Assert.Equal("app-1", await _store.GetApplicationIdAsync(authorization, CancellationToken.None));
        Assert.Equal("bob", await _store.GetSubjectAsync(authorization, CancellationToken.None));
        Assert.Equal(OpenIddictConstants.Statuses.Valid, await _store.GetStatusAsync(authorization, CancellationToken.None));
        Assert.Equal(OpenIddictConstants.AuthorizationTypes.Permanent, await _store.GetTypeAsync(authorization, CancellationToken.None));
        Assert.Equal(creationDate, await _store.GetCreationDateAsync(authorization, CancellationToken.None));
    }

    [Fact]
    public async Task Scopes_and_properties_round_trip_through_get_and_set()
    {
        var authorization = new OpenIddictGroundworkAuthorization();
        var scopes = ImmutableArray.Create("openid", "profile");
        using var property = JsonDocument.Parse("{\"tier\":\"gold\"}");
        var properties = new Dictionary<string, JsonElement> { ["feature"] = property.RootElement.Clone() }.ToImmutableDictionary();

        await _store.SetScopesAsync(authorization, scopes, CancellationToken.None);
        await _store.SetPropertiesAsync(authorization, properties, CancellationToken.None);

        Assert.Equal(scopes.ToArray(), (await _store.GetScopesAsync(authorization, CancellationToken.None)).ToArray());
        var readProperties = await _store.GetPropertiesAsync(authorization, CancellationToken.None);
        Assert.Equal("gold", readProperties["feature"].GetProperty("tier").GetString());
    }

    [Fact]
    public async Task Empty_collections_and_maps_round_trip_to_empty_immutable_collections()
    {
        var authorization = new OpenIddictGroundworkAuthorization();

        Assert.Empty(await _store.GetScopesAsync(authorization, CancellationToken.None));
        Assert.Empty(await _store.GetPropertiesAsync(authorization, CancellationToken.None));
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
    public async Task FindBySubjectAsync_resolves_through_the_declared_subject_route()
    {
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "bob" }, CancellationToken.None);
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "bob" }, CancellationToken.None);
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "alice" }, CancellationToken.None);

        var found = new List<OpenIddictGroundworkAuthorization>();
        await foreach (var authorization in _store.FindBySubjectAsync("bob", CancellationToken.None))
            found.Add(authorization);

        Assert.Equal(2, found.Count);
        Assert.All(found, authorization => Assert.Equal("bob", authorization.Subject));
    }

    [Fact]
    public async Task FindAsync_subject_only_serves_the_same_result_as_FindBySubjectAsync()
    {
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "bob" }, CancellationToken.None);
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "alice" }, CancellationToken.None);

        var found = new List<OpenIddictGroundworkAuthorization>();
        await foreach (var authorization in _store.FindAsync("bob", null, null, null, null, CancellationToken.None))
            found.Add(authorization);

        Assert.Single(found);
        Assert.Equal("bob", found[0].Subject);
    }

    [Fact]
    public async Task FindAsync_scopes_only_serves_through_the_declared_scope_route_requiring_every_scope()
    {
        await _store.CreateAsync(
            new OpenIddictGroundworkAuthorization { Subject = "bob", Scopes = ["openid", "profile"] },
            CancellationToken.None);
        await _store.CreateAsync(
            new OpenIddictGroundworkAuthorization { Subject = "alice", Scopes = ["openid"] },
            CancellationToken.None);

        var found = new List<OpenIddictGroundworkAuthorization>();
        await foreach (var authorization in _store.FindAsync(null, null, null, null, ["openid", "profile"], CancellationToken.None))
            found.Add(authorization);

        Assert.Single(found);
        Assert.Equal("bob", found[0].Subject);
    }

    [Theory]
    [InlineData("client-a", null, null)]
    [InlineData(null, "valid", null)]
    [InlineData(null, null, "permanent")]
    public async Task FindAsync_rejects_a_subject_predicate_combined_with_client_status_or_type(
        string? client, string? status, string? type)
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var authorization in _store.FindAsync("bob", client, status, type, null, CancellationToken.None))
                _ = authorization;
        });

        Assert.Equal("authorization.FindAsync", exception.Operation);
    }

    [Fact]
    public async Task FindAsync_rejects_an_entirely_unfiltered_predicate()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var authorization in _store.FindAsync(null, null, null, null, null, CancellationToken.None))
                _ = authorization;
        });

        Assert.Equal("authorization.FindAsync", exception.Operation);
    }

    [Fact]
    public async Task FindAsync_rejects_combining_subject_and_scopes()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var authorization in _store.FindAsync("bob", null, null, null, ["openid"], CancellationToken.None))
                _ = authorization;
        });

        Assert.Equal("authorization.FindAsync", exception.Operation);
    }

    [Fact]
    public async Task FindByApplicationIdAsync_is_always_rejected_because_no_route_is_declared()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var authorization in _store.FindByApplicationIdAsync("app-1", CancellationToken.None))
                _ = authorization;
        });

        Assert.Equal("authorization.FindByApplicationIdAsync", exception.Operation);
    }

    [Fact]
    public async Task Update_is_a_successful_compare_and_swap_that_rotates_the_concurrency_token()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "bob" };
        await _store.CreateAsync(authorization, CancellationToken.None);
        var originalToken = authorization.ConcurrencyToken;

        await _store.SetStatusAsync(authorization, OpenIddictConstants.Statuses.Valid, CancellationToken.None);
        await _store.UpdateAsync(authorization, CancellationToken.None);

        Assert.NotEqual(originalToken, authorization.ConcurrencyToken);
        var reloaded = await _store.FindByIdAsync(authorization.Id, CancellationToken.None);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, reloaded!.Status);
        Assert.Equal(authorization.ConcurrencyToken, reloaded.ConcurrencyToken);
    }

    [Fact]
    public async Task Update_with_a_stale_persistence_version_raises_a_concurrency_exception()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "contested" };
        await _store.CreateAsync(authorization, CancellationToken.None);

        var staleCopy = await _store.FindByIdAsync(authorization.Id, CancellationToken.None);
        var freshCopy = await _store.FindByIdAsync(authorization.Id, CancellationToken.None);
        Assert.NotNull(staleCopy);
        Assert.NotNull(freshCopy);

        await _store.UpdateAsync(freshCopy!, CancellationToken.None);

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            _store.UpdateAsync(staleCopy!, CancellationToken.None).AsTask());
    }

    [Fact]
    public async Task DeleteAsync_removes_the_authorization()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "removable" };
        await _store.CreateAsync(authorization, CancellationToken.None);

        await _store.DeleteAsync(authorization, CancellationToken.None);

        Assert.Null(await _store.FindByIdAsync(authorization.Id, CancellationToken.None));
    }

    /// <summary>
    /// Count-all and list-all are rejected, not degraded, for the same reason as the application and scope
    /// stores: no bounded route is declared and the portable query surface is forbidden in production
    /// Groundwork reads by <c>ArchitectureGuardTests.Groundwork_production_reads_use_only_admitted_bounded_query_APIs</c>.
    /// </summary>
    [Fact]
    public async Task CountAsync_is_rejected_because_no_bounded_count_all_route_is_declared()
    {
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.CountAsync(CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal("authorization.CountAsync", exception.Operation);
    }

    [Fact]
    public async Task ListAsync_is_rejected_because_no_bounded_list_all_route_is_declared()
    {
        await _store.CreateAsync(new OpenIddictGroundworkAuthorization { Subject = "list-one" }, CancellationToken.None);

        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(async () =>
        {
            await foreach (var authorization in _store.ListAsync(null, null, CancellationToken.None))
                _ = authorization;
        });

        Assert.Equal("authorization.ListAsync", exception.Operation);
    }

    [Fact]
    public async Task CountAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.CountAsync(authorizations => authorizations.Select(authorization => authorization.Id), CancellationToken.None).AsTask());

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    [Fact]
    public async Task GetAsync_generic_overload_fails_before_any_provider_work()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            _store.GetAsync(
                    (authorizations, state) => authorizations.Where(authorization => authorization.Subject == state).Select(authorization => authorization.Id),
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
                (authorizations, state) => authorizations.Where(authorization => authorization.Subject == state).Select(authorization => authorization.Id),
                "irrelevant",
                CancellationToken.None));

        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, exception.Code);
        Assert.Equal(0, _documentStore.SaveCount + _documentStore.LoadCount + _documentStore.QueryCount);
    }

    /// <summary>
    /// No creation-date route is declared for authorizations, so prune has nothing to bound its selection
    /// with; this is rejected outright rather than scanning every document in memory.
    /// </summary>
    [Fact]
    public async Task PruneAsync_is_rejected_because_no_date_route_is_declared()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.PruneAsync(DateTimeOffset.UtcNow, CancellationToken.None));

        Assert.Equal("authorization.PruneAsync", exception.Operation);
    }

    [Fact]
    public async Task RevokeByApplicationIdAsync_is_rejected_because_no_route_is_declared()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.RevokeByApplicationIdAsync("app-1", CancellationToken.None));

        Assert.Equal("authorization.RevokeByApplicationIdAsync", exception.Operation);
    }

    [Theory]
    [InlineData("client-a", null, null)]
    [InlineData(null, "valid", null)]
    [InlineData(null, null, "permanent")]
    public async Task RevokeAsync_rejects_predicates_the_declared_route_cannot_honestly_serve(
        string? client, string? status, string? type)
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.RevokeAsync("bob", client, status, type, CancellationToken.None));

        Assert.Equal("authorization.RevokeAsync", exception.Operation);
    }

    [Fact]
    public async Task RevokeAsync_with_no_subject_is_rejected()
    {
        var exception = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(
            async () => await _store.RevokeAsync(null, null, null, null, CancellationToken.None));

        Assert.Equal("authorization.RevokeAsync", exception.Operation);
    }

    [Fact]
    public async Task RevokeBySubjectAsync_revokes_every_matching_authorization_and_returns_the_exact_count()
    {
        var first = new OpenIddictGroundworkAuthorization { Subject = "bob", Status = OpenIddictConstants.Statuses.Valid };
        var second = new OpenIddictGroundworkAuthorization { Subject = "bob", Status = OpenIddictConstants.Statuses.Valid };
        var other = new OpenIddictGroundworkAuthorization { Subject = "alice", Status = OpenIddictConstants.Statuses.Valid };
        await _store.CreateAsync(first, CancellationToken.None);
        await _store.CreateAsync(second, CancellationToken.None);
        await _store.CreateAsync(other, CancellationToken.None);

        var revoked = await _store.RevokeBySubjectAsync("bob", CancellationToken.None);

        Assert.Equal(2, revoked);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, (await _store.FindByIdAsync(first.Id, CancellationToken.None))!.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, (await _store.FindByIdAsync(second.Id, CancellationToken.None))!.Status);
        Assert.Equal(OpenIddictConstants.Statuses.Valid, (await _store.FindByIdAsync(other.Id, CancellationToken.None))!.Status);
    }

    [Fact]
    public async Task RevokeAsync_with_only_a_subject_delegates_to_the_same_subject_route_as_RevokeBySubjectAsync()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "bob", Status = OpenIddictConstants.Statuses.Valid };
        await _store.CreateAsync(authorization, CancellationToken.None);

        var revoked = await _store.RevokeAsync("bob", null, null, null, CancellationToken.None);

        Assert.Equal(1, revoked);
        Assert.Equal(OpenIddictConstants.Statuses.Revoked, (await _store.FindByIdAsync(authorization.Id, CancellationToken.None))!.Status);
    }

    /// <summary>
    /// Proves the exactly-once property end to end through the store's own surface (spec 106 T030): a second
    /// call for the SAME subject, after the one matching authorization is already revoked, must not perform a
    /// second underlying save for it - the atomic wrapper replays the first call's receipt for that
    /// authorization id instead of re-executing the load/save.
    /// </summary>
    [Fact]
    public async Task RevokeBySubjectAsync_replay_returns_the_same_outcome_without_a_second_underlying_write()
    {
        var authorization = new OpenIddictGroundworkAuthorization { Subject = "bob", Status = OpenIddictConstants.Statuses.Valid };
        await _store.CreateAsync(authorization, CancellationToken.None);
        var saveCountBeforeRevoke = _documentStore.AuthorizationSaveCount;

        var first = await _store.RevokeBySubjectAsync("bob", CancellationToken.None);
        var saveCountAfterFirstRevoke = _documentStore.AuthorizationSaveCount;
        var replay = await _store.RevokeBySubjectAsync("bob", CancellationToken.None);
        var saveCountAfterReplay = _documentStore.AuthorizationSaveCount;

        Assert.Equal(1, first);
        Assert.Equal(1, replay);
        Assert.Equal(1, saveCountAfterFirstRevoke - saveCountBeforeRevoke);
        Assert.Equal(0, saveCountAfterReplay - saveCountAfterFirstRevoke);
    }

    /// <summary>
    /// Builds a real <see cref="OpenIddictGroundworkAtomicWrite"/> over the fake document store, mirroring
    /// <c>OpenIddictGroundworkAtomicWriteTests.AtomicWrite</c> so this suite exercises the revoke family
    /// exactly as production wiring would.
    /// </summary>
    private static OpenIddictGroundworkAtomicWrite AtomicWrite(FakeGroundworkDocumentStore store)
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("Admission callback is not used by OpenIddictGroundworkStoreSessionFactory."),
            TransactionBoundary.CrossUnitAtomic));
        var sessionFactory = new OpenIddictGroundworkStoreSessionFactory(source, new StubGroundworkStoreSessionFactory(store));
        return new OpenIddictGroundworkAtomicWrite(sessionFactory);
    }

    private sealed class StubGroundworkStoreSessionFactory(FakeGroundworkDocumentStore store) : IGroundworkStoreSessionFactory
    {
        public ValueTask<GroundworkStoreSession> CreateAsync(
            PersistenceAccessPolicy requiredPolicy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("OpenIddict must not request a scoped or privileged session.");

        public ValueTask<GroundworkStoreSession> CreateOrdinaryGlobalAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new GroundworkStoreSession(
                DocumentStoreAccess.Global,
                new GroundworkStoreSessionResources(store, store)));
        }

        public ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the authorization-store suite.");

        public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the authorization-store suite.");
    }

    /// <summary>
    /// In-memory <see cref="IDocumentStore"/>/<see cref="IBoundedDocumentStore"/> test double with working
    /// unit-of-work commit/rollback semantics, combining what
    /// <c>GroundworkOpenIddictApplicationStoreTests.FakeGroundworkDocumentStore</c> supports (point load/save/
    /// delete, bounded query with <c>Equal</c>) and what <c>OpenIddictGroundworkAtomicWriteTests.FakeUnitOfWorkDocumentStore</c>
    /// supports (a working <c>BeginAsync</c>/commit-or-rollback unit of work), plus <c>CollectionContainsAll</c>
    /// for the scopes route - no sibling fake needed that operator. Authorizations declare no unique index,
    /// so unlike the application/scope fakes this one has no duplicate-key preflight.
    /// </summary>
    private sealed class FakeGroundworkDocumentStore : IDocumentStore, IBoundedDocumentStore
    {
        private readonly Dictionary<(string Kind, string Id), DocumentEnvelope> _documents = new();
        private readonly Lock _gate = new();
        private readonly SemaphoreSlim _unitOfWorkGate = new(1, 1);

        public int SaveCount { get; private set; }
        public int LoadCount { get; private set; }
        public int QueryCount { get; private set; }
        public int AuthorizationSaveCount { get; private set; }

        public DocumentStoreAccess Access => DocumentStoreAccess.Global;
        public TransactionBoundary TransactionBoundary => TransactionBoundary.CrossUnitAtomic;

        public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
        {
            lock (_gate)
                return Task.FromResult(SaveLocked(request));
        }

        private DocumentStoreWriteResult SaveLocked(SaveDocumentRequest request)
        {
            SaveCount++;
            if (request.DocumentKind == OpenIddictGroundworkJson.AuthorizationDocumentKind)
                AuthorizationSaveCount++;

            var key = (request.DocumentKind, request.Id);
            _documents.TryGetValue(key, out var existing);
            if (existing is null)
            {
                if (request.ExpectedVersion is { } expected && expected != 0)
                    return DocumentStoreWriteResult.NotFound;
            }
            else if (request.ExpectedVersion is { } expectedVersion && existing.Version != expectedVersion)
            {
                return DocumentStoreWriteResult.ConcurrencyConflict;
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
            return DocumentStoreWriteResult.Saved(envelope);
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
            throw new NotSupportedException("Not exercised by the authorization store.");

        public Task<DocumentQueryResult> QueryAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the authorization store.");

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the authorization store.");

        public Task<bool> AnyAsync(PortableDocumentQuery query, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException("Not exercised by the authorization store.");
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
                QueryComparisonOperator.CollectionContainsAll => property.ValueKind == JsonValueKind.Array &&
                    comparison.Values.All(value => property.EnumerateArray().Any(element =>
                        element.ValueKind == JsonValueKind.String && string.Equals(element.GetString(), value, StringComparison.Ordinal))),
                _ => throw new NotSupportedException($"Operator '{comparison.Operator}' is not supported by the test double.")
            };
        }

        public async Task<IDocumentUnitOfWork> BeginAsync(DocumentCommitScope scope, CancellationToken cancellationToken = default)
        {
            await _unitOfWorkGate.WaitAsync(cancellationToken);
            return new FakeUnitOfWork(this);
        }

        private sealed class FakeUnitOfWork(FakeGroundworkDocumentStore store) : IDocumentUnitOfWork
        {
            private readonly Dictionary<(string Kind, string Id), DocumentEnvelope?> _staged = new();
            private readonly List<Func<Task>> _pending = new();
            private bool _completed;

            public Task<DocumentStoreWriteResult> SaveAsync(SaveDocumentRequest request, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                var key = (request.DocumentKind, request.Id);
                var existing = ResolveCurrent(key);
                if (existing is null)
                {
                    if (request.ExpectedVersion is { } expected && expected != 0)
                        return Task.FromResult(DocumentStoreWriteResult.NotFound);
                }
                else if (request.ExpectedVersion is { } expectedVersion && existing.Version != expectedVersion)
                {
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);
                }

                var now = DateTimeOffset.UtcNow;
                var envelope = new DocumentEnvelope(
                    request.DocumentKind,
                    request.Id,
                    request.SchemaVersion,
                    (existing?.Version ?? 0) + 1,
                    request.ContentJson,
                    existing?.CreatedAt ?? now,
                    now);
                _staged[key] = envelope;
                _pending.Add(() => store.SaveAsync(request, cancellationToken));
                return Task.FromResult(DocumentStoreWriteResult.Saved(envelope));
            }

            public Task<DocumentStoreWriteResult> DeleteAsync(DeleteDocumentRequest request, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                var key = (request.DocumentKind, request.Id);
                var existing = ResolveCurrent(key);
                if (existing is null)
                    return Task.FromResult(DocumentStoreWriteResult.NotFound);
                if (request.ExpectedVersion is { } expected && existing.Version != expected)
                    return Task.FromResult(DocumentStoreWriteResult.ConcurrencyConflict);

                _staged[key] = null;
                _pending.Add(() => store.DeleteAsync(request, cancellationToken));
                return Task.FromResult(DocumentStoreWriteResult.Deleted(existing.Id));
            }

            public Task<DocumentEnvelope?> LoadAsync(string documentKind, string id, CancellationToken cancellationToken = default)
            {
                EnsureActive();
                return Task.FromResult(ResolveCurrent((documentKind, id)));
            }

            public async Task CommitAsync(CancellationToken cancellationToken = default)
            {
                EnsureActive();
                foreach (var operation in _pending)
                    await operation();
                _completed = true;
            }

            public Task RollbackAsync(CancellationToken cancellationToken = default)
            {
                EnsureActive();
                _pending.Clear();
                _staged.Clear();
                _completed = true;
                return Task.CompletedTask;
            }

            public ValueTask DisposeAsync()
            {
                _completed = true;
                store._unitOfWorkGate.Release();
                return ValueTask.CompletedTask;
            }

            private DocumentEnvelope? ResolveCurrent((string Kind, string Id) key) =>
                _staged.TryGetValue(key, out var staged) ? staged : store._documents.GetValueOrDefault(key);

            private void EnsureActive()
            {
                if (_completed)
                    throw new InvalidOperationException("The unit of work has already completed.");
            }
        }
    }
}
