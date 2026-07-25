using System.Collections.Immutable;
using System.Globalization;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Composition;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;
using Elsa.Persistence.Core;
using Elsa.Persistence.Groundwork;
using Elsa.Persistence.Groundwork.Composition;
using Elsa.Persistence.Groundwork.Scoping;
using Elsa.Persistence.Groundwork.Testing;
using Elsa.Persistence.Groundwork.Unified.Composition;
using Groundwork.Core.Capabilities;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Core.SchemaEvolution;
using Groundwork.Core.Transactions;
using Groundwork.Documents.Scoping;
using Groundwork.Documents.Store;
using Groundwork.Sqlite;
using Groundwork.Sqlite.Documents;
using Groundwork.Sqlite.PhysicalStorage;
using Microsoft.Data.Sqlite;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Tests;

/// <summary>
/// Direct public-behavior contract for the OpenIddict 7.5 scope store. The fixture
/// uses the admitted SQLite physical store and never supplies an IQueryable fallback.
/// </summary>
public sealed class GroundworkOpenIddictScopeStoreTests
{
    [Fact]
    public async Task Instantiate_create_and_all_descriptor_members_round_trip()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        var created = await store.InstantiateAsync(CancellationToken.None);
        var descriptions = ImmutableDictionary<CultureInfo, string>.Empty
            .Add(CultureInfo.GetCultureInfo("en-US"), "Read payments.")
            .Add(CultureInfo.GetCultureInfo("nl-NL"), "Lees betalingen.");
        var displayNames = ImmutableDictionary<CultureInfo, string>.Empty
            .Add(CultureInfo.GetCultureInfo("en-US"), "Payments")
            .Add(CultureInfo.GetCultureInfo("nl-NL"), "Betalingen");
        var properties = ImmutableDictionary<string, JsonElement>.Empty
            .Add("tier", Json("\"privileged\""));
        var resources = ImmutableArray.Create("payments-api", "ledger-api");

        created.Id = "scope-payments";
        await store.SetDescriptionAsync(created, "Read payments.", CancellationToken.None);
        await store.SetDescriptionsAsync(created, descriptions, CancellationToken.None);
        await store.SetDisplayNameAsync(created, "Payments", CancellationToken.None);
        await store.SetDisplayNamesAsync(created, displayNames, CancellationToken.None);
        await store.SetNameAsync(created, "payments.read", CancellationToken.None);
        await store.SetPropertiesAsync(created, properties, CancellationToken.None);
        await store.SetResourcesAsync(created, resources, CancellationToken.None);
        await store.CreateAsync(created, CancellationToken.None);

        var found = await store.FindByIdAsync("scope-payments", CancellationToken.None);

        Assert.NotNull(found);
        Assert.Equal("scope-payments", await store.GetIdAsync(found, CancellationToken.None));
        Assert.Equal("Read payments.", await store.GetDescriptionAsync(found, CancellationToken.None));
        Assert.Equal("Read payments.", (await store.GetDescriptionsAsync(found, CancellationToken.None))[CultureInfo.GetCultureInfo("en-US")]);
        Assert.Equal("Lees betalingen.", (await store.GetDescriptionsAsync(found, CancellationToken.None))[CultureInfo.GetCultureInfo("nl-NL")]);
        Assert.Equal("Payments", await store.GetDisplayNameAsync(found, CancellationToken.None));
        Assert.Equal("Payments", (await store.GetDisplayNamesAsync(found, CancellationToken.None))[CultureInfo.GetCultureInfo("en-US")]);
        Assert.Equal("Betalingen", (await store.GetDisplayNamesAsync(found, CancellationToken.None))[CultureInfo.GetCultureInfo("nl-NL")]);
        Assert.Equal("payments.read", await store.GetNameAsync(found, CancellationToken.None));
        Assert.Equal("\"privileged\"", (await store.GetPropertiesAsync(found, CancellationToken.None))["tier"].GetRawText());
        Assert.Equal(resources.ToArray(), (await store.GetResourcesAsync(found, CancellationToken.None)).ToArray());
    }

    [Fact]
    public async Task Scope_name_is_unique_and_a_failed_duplicate_keeps_the_original_unchanged()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        await CreateScopeAsync(store, "scope-a", "payments.read", "payments-api");
        var duplicate = await store.InstantiateAsync(CancellationToken.None);
        duplicate.Id = "scope-duplicate";
        await store.SetNameAsync(duplicate, "payments.read", CancellationToken.None);
        await store.SetDisplayNameAsync(duplicate, "Must not replace the original", CancellationToken.None);

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            store.CreateAsync(duplicate, CancellationToken.None).AsTask());

        var original = await store.FindByNameAsync("payments.read", CancellationToken.None);
        Assert.NotNull(original);
        Assert.Equal("scope-a", await store.GetIdAsync(original, CancellationToken.None));
        Assert.Equal("payments.read", await store.GetNameAsync(original, CancellationToken.None));
        Assert.Equal(1, await store.CountAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Name_set_and_resource_membership_queries_are_deterministically_id_ordered()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        await CreateScopeAsync(store, "scope-c", "profile", "identity-api");
        await CreateScopeAsync(store, "scope-a", "payments.read", "payments-api", "ledger-api");
        await CreateScopeAsync(store, "scope-b", "payments.write", "payments-api");

        var byNames = await ToListAsync(store.FindByNamesAsync(
            ImmutableArray.Create("profile", "payments.write", "profile"),
            CancellationToken.None));
        var byResource = await ToListAsync(store.FindByResourceAsync("payments-api", CancellationToken.None));

        Assert.Equal(new[] { "scope-b", "scope-c" }, await GetIdsAsync(store, byNames));
        Assert.Equal(new[] { "scope-a", "scope-b" }, await GetIdsAsync(store, byResource));
    }

    [Fact]
    public async Task Empty_and_zero_count_queries_complete_without_opening_a_provider_session()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;

        Assert.Empty(await ToListAsync(store.FindByNamesAsync(default, CancellationToken.None)));
        Assert.Empty(await ToListAsync(store.FindByNamesAsync(ImmutableArray<string>.Empty, CancellationToken.None)));
        Assert.Empty(await ToListAsync(store.ListAsync(0, null, CancellationToken.None)));
        Assert.Equal(0, fixture.SessionOpenCount);
    }

    [Fact]
    public async Task Name_set_ceiling_and_invalid_names_fail_before_opening_a_provider_session()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        var oversized = Enumerable.Range(0, GroundworkOpenIddictScopeStore.MaximumNamesPerLookup + 1)
            .Select(index => $"scope-{index}")
            .ToImmutableArray();

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            store.FindByNamesAsync(oversized, CancellationToken.None));
        Assert.Throws<ArgumentException>(() =>
            store.FindByNamesAsync(ImmutableArray.Create("valid", ""), CancellationToken.None));
        Assert.Equal(0, fixture.SessionOpenCount);
    }

    [Fact]
    public async Task Count_and_offset_pages_are_exact_and_deterministically_id_ordered()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        await CreateScopeAsync(store, "scope-c", "profile", "identity-api");
        await CreateScopeAsync(store, "scope-a", "payments.read", "payments-api");
        await CreateScopeAsync(store, "scope-b", "payments.write", "payments-api");

        var all = await ToListAsync(store.ListAsync(null, null, CancellationToken.None));
        var page = await ToListAsync(store.ListAsync(1, 1, CancellationToken.None));

        Assert.Equal(3, await store.CountAsync(CancellationToken.None));
        Assert.Equal(new[] { "scope-a", "scope-b", "scope-c" }, await GetIdsAsync(store, all));
        Assert.Equal(new[] { "scope-b" }, await GetIdsAsync(store, page));
    }

    [Fact]
    public async Task Update_and_delete_require_the_expected_version_and_rotate_the_concurrency_token()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        await CreateScopeAsync(store, "scope-a", "payments.read", "payments-api");
        var current = await store.FindByIdAsync("scope-a", CancellationToken.None);
        var stale = await store.FindByIdAsync("scope-a", CancellationToken.None);

        Assert.NotNull(current);
        Assert.NotNull(stale);
        var originalConcurrencyToken = current.ConcurrencyToken;
        await store.SetDisplayNameAsync(current, "Payments read v2", CancellationToken.None);
        await store.UpdateAsync(current, CancellationToken.None);

        Assert.NotEqual(originalConcurrencyToken, current.ConcurrencyToken);
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() => store.UpdateAsync(stale, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() => store.DeleteAsync(stale, CancellationToken.None).AsTask());

        var persisted = await store.FindByIdAsync("scope-a", CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("Payments read v2", await store.GetDisplayNameAsync(persisted, CancellationToken.None));

        await store.DeleteAsync(persisted, CancellationToken.None);
        Assert.Null(await store.FindByIdAsync("scope-a", CancellationToken.None));
    }

    [Fact]
    public async Task Update_and_delete_of_an_unpersisted_scope_fail_closed_without_opening_a_provider_session()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        var scope = await fixture.Store.InstantiateAsync(CancellationToken.None);
        var originalConcurrencyToken = scope.ConcurrencyToken;

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            fixture.Store.UpdateAsync(scope, CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            fixture.Store.DeleteAsync(scope, CancellationToken.None).AsTask());

        Assert.Equal(originalConcurrencyToken, scope.ConcurrencyToken);
        Assert.Equal(0, fixture.SessionOpenCount);
    }

    [Fact]
    public async Task Generic_delegates_fail_closed_before_the_delegate_is_invoked()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        var countInvoked = false;
        var getInvoked = false;
        var listInvoked = false;

        var count = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            store.CountAsync<string>(records =>
            {
                countInvoked = true;
                return records.Select(record => record.Id);
            }, CancellationToken.None).AsTask());
        var get = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            store.GetAsync<string, string>((records, name) =>
            {
                getInvoked = true;
                return records.Where(record => record.Name == name).Select(record => record.Id);
            }, "payments.read", CancellationToken.None).AsTask());
        var list = await Assert.ThrowsAsync<OpenIddictGroundworkCapabilityException>(() =>
            ToListAsync(store.ListAsync<string, string>((records, resource) =>
            {
                listInvoked = true;
                return records.Where(record => record.Resources.Contains(resource)).Select(record => record.Id);
            }, "payments-api", CancellationToken.None)));

        Assert.False(countInvoked);
        Assert.False(getInvoked);
        Assert.False(listInvoked);
        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, count.Code);
        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, get.Code);
        Assert.Equal(OpenIddictGroundworkCapabilityException.UnsupportedGenericQueryCode, list.Code);
        Assert.Equal("scope.CountAsync", count.Operation);
        Assert.Equal("scope.GetAsync", get.Operation);
        Assert.Equal("scope.ListAsync", list.Operation);
    }

    [Fact]
    public async Task Invalid_persisted_culture_identity_uses_the_adapter_serialization_boundary()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        var scope = await fixture.Store.InstantiateAsync(CancellationToken.None);
        scope.Descriptions["not a valid culture identity"] = "invalid";

        await Assert.ThrowsAsync<OpenIddictGroundworkSerializationException>(() =>
            fixture.Store.GetDescriptionsAsync(scope, CancellationToken.None).AsTask());
        Assert.Equal(0, fixture.SessionOpenCount);
    }

    [Fact]
    public async Task Cancellation_is_propagated_unchanged()
    {
        await using var fixture = await ScopeStoreFixture.CreateAsync();
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store = fixture.Store;
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => store.CountAsync(cancellation.Token).AsTask());
    }

    private static async Task<OpenIddictGroundworkScope> CreateScopeAsync(
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store,
        string id,
        string name,
        params string[] resources)
    {
        var scope = await store.InstantiateAsync(CancellationToken.None);
        scope.Id = id;
        await store.SetNameAsync(scope, name, CancellationToken.None);
        await store.SetResourcesAsync(scope, resources.ToImmutableArray(), CancellationToken.None);
        await store.CreateAsync(scope, CancellationToken.None);
        return scope;
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
        {
            result.Add(value);
        }

        return result;
    }

    private static async Task<string[]> GetIdsAsync(
        IOpenIddictScopeStore<OpenIddictGroundworkScope> store,
        IEnumerable<OpenIddictGroundworkScope> scopes)
    {
        var ids = new List<string>();
        foreach (var scope in scopes)
        {
            ids.Add((await store.GetIdAsync(scope, CancellationToken.None))!);
        }

        return [.. ids];
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private sealed class ScopeStoreFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly GroundworkStoreSessionSource _sessionSource;

        private ScopeStoreFixture(
            string databasePath,
            GroundworkStoreSessionSource sessionSource,
            GroundworkOpenIddictScopeStore store)
        {
            _databasePath = databasePath;
            _sessionSource = sessionSource;
            Store = store;
        }

        public GroundworkOpenIddictScopeStore Store { get; }

        public int SessionOpenCount { get; private set; }

        public static async Task<ScopeStoreFixture> CreateAsync()
        {
            var databasePath = Path.Combine(Path.GetTempPath(), $"elsa-openiddict-scope-{Guid.NewGuid():N}.db");
            try
            {
                var physicalSource = await GroundworkStoreInitialization.CreatePhysicalSchemaSourceAsync(
                    SqliteGroundworkCapabilities.Runtime(),
                    new GroundworkProviderTopologySnapshot(
                        SqliteGroundworkCapabilities.Provider.Name,
                        "sqlite-file",
                        new HashSet<string>(StringComparer.Ordinal)
                        {
                            RuntimeGroundworkStorageManifestSource.MultiDocumentTransactionsTopologyIdentity
                        }),
                    SqliteGroundworkCapabilities.PhysicalNames,
                    [new OpenIddictGroundworkStorageManifestSource()]);
                await using (var schemaConnection = new SqliteConnection($"Data Source={databasePath}"))
                {
                    var result = await PhysicalSchemaApplication.ApplyAsync(
                        physicalSource.PhysicalTarget,
                        new SqlitePhysicalSchemaExecutor(schemaConnection));
                    Assert.True(result.Outcome is PhysicalSchemaApplicationOutcome.Applied or PhysicalSchemaApplicationOutcome.NoChanges);
                }

                var manifest = physicalSource.CreateManifest();
                var routes = physicalSource.PhysicalTarget.Routes;
                var scopeRoute = routes.Single(route =>
                    route.StorageUnit.Value == OpenIddictGroundworkJson.ScopeDocumentKind);
                var sessionSource = new GroundworkStoreSessionSource();
                var fixture = new ScopeStoreFixture(
                    databasePath,
                    sessionSource,
                    new GroundworkOpenIddictScopeStore(
                        new OpenIddictGroundworkStoreSessionFactory(
                            sessionSource,
                            new GroundworkStoreSessionFactory(
                                new FixedAccessContextAccessor(PersistenceAccessContext.Global),
                                sessionSource))));

                Assert.True(sessionSource.TrySetAdmitted(async (access, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (access != DocumentStoreAccess.Global)
                        throw new InvalidOperationException("OpenIddict scope storage must use explicit global access.");

                    var connection = new SqliteConnection($"Data Source={databasePath}");
                    try
                    {
                        await connection.OpenAsync(cancellationToken);
                        var documents = new SqlitePhysicalDocumentStore(
                            connection,
                            manifest,
                            routes,
                            DocumentStoreAccess.Global);
                        var bounded = new GroundworkBoundedDocumentStoreRouter(
                        [
                            KeyValuePair.Create<string, IBoundedDocumentStore>(
                                OpenIddictGroundworkJson.ScopeDocumentKind,
                                SqlitePhysicalQueryRuntime.Create(
                                    documents,
                                    manifest,
                                    scopeRoute,
                                    physicalSource.PhysicalTarget.Provider))
                        ]);
                        fixture.SessionOpenCount++;
                        return new GroundworkStoreSessionResources(documents, bounded, connection);
                    }
                    catch
                    {
                        await connection.DisposeAsync();
                        throw;
                    }
                }, TransactionBoundary.CrossUnitAtomic));

                return fixture;
            }
            catch
            {
                DeleteDatabase(databasePath);
                throw;
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _sessionSource.DisposeAsync();
            DeleteDatabase(_databasePath);
        }

        private static void DeleteDatabase(string databasePath)
        {
            File.Delete(databasePath);
            File.Delete($"{databasePath}-shm");
            File.Delete($"{databasePath}-wal");
        }
    }

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) : IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }
}
