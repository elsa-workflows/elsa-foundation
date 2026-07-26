using System.Collections.Immutable;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

public sealed class OpenIddictGroundworkTokenDocumentCoreTests
{
    private static readonly Type CoreType =
        typeof(OpenIddictGroundworkStoreSessionFactory).Assembly.GetType(
            "Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores.OpenIddictGroundworkTokenDocumentCore",
            throwOnError: true)!;

    [Fact]
    public async Task Core_is_internal_non_interface_and_local_descriptor_operations_clone_properties()
    {
        var sessions = new RejectingSessionFactory();
        var core = CreateCore(CreateAdmittedFactory(sessions));
        var token = await CallAsync<OpenIddictGroundworkToken>(
            core,
            "InstantiateAsync",
            CancellationToken.None);
        var creation = DateTimeOffset.Parse("2026-07-25T01:02:03+00:00");
        var expiration = creation.AddHours(1);
        var redemption = creation.AddMinutes(30);

        using (var document = JsonDocument.Parse("""{"answer":42}"""))
        {
            var properties = ImmutableDictionary<string, JsonElement>.Empty.Add(
                "custom",
                document.RootElement);
            await CallAsync(
                core,
                "SetPropertiesAsync",
                token,
                properties,
                CancellationToken.None);
        }

        await CallAsync(core, "SetApplicationIdAsync", token, "application-a", CancellationToken.None);
        await CallAsync(core, "SetAuthorizationIdAsync", token, "authorization-a", CancellationToken.None);
        await CallAsync(core, "SetCreationDateAsync", token, creation, CancellationToken.None);
        await CallAsync(core, "SetExpirationDateAsync", token, expiration, CancellationToken.None);
        await CallAsync(core, "SetPayloadAsync", token, "payload", CancellationToken.None);
        await CallAsync(core, "SetRedemptionDateAsync", token, redemption, CancellationToken.None);
        await CallAsync(core, "SetReferenceIdAsync", token, "reference-a", CancellationToken.None);
        await CallAsync(core, "SetStatusAsync", token, OpenIddictConstants.Statuses.Valid, CancellationToken.None);
        await CallAsync(core, "SetSubjectAsync", token, "subject-a", CancellationToken.None);
        await CallAsync(
            core,
            "SetTypeAsync",
            token,
            OpenIddictConstants.TokenTypeHints.RefreshToken,
            CancellationToken.None);

        var firstProperties = await CallAsync<ImmutableDictionary<string, JsonElement>>(
            core,
            "GetPropertiesAsync",
            token,
            CancellationToken.None);
        token.Properties["custom"] = Json("""{"answer":7}""");

        Assert.False(CoreType.IsPublic);
        Assert.Empty(CoreType.GetInterfaces());
        Assert.Equal("application-a", await CallAsync<string?>(core, "GetApplicationIdAsync", token, CancellationToken.None));
        Assert.Equal("authorization-a", await CallAsync<string?>(core, "GetAuthorizationIdAsync", token, CancellationToken.None));
        Assert.Equal(creation, await CallAsync<DateTimeOffset?>(core, "GetCreationDateAsync", token, CancellationToken.None));
        Assert.Equal(expiration, await CallAsync<DateTimeOffset?>(core, "GetExpirationDateAsync", token, CancellationToken.None));
        Assert.Equal(token.Id, await CallAsync<string?>(core, "GetIdAsync", token, CancellationToken.None));
        Assert.Equal("payload", await CallAsync<string?>(core, "GetPayloadAsync", token, CancellationToken.None));
        Assert.Equal(42, firstProperties["custom"].GetProperty("answer").GetInt32());
        Assert.Equal(redemption, await CallAsync<DateTimeOffset?>(core, "GetRedemptionDateAsync", token, CancellationToken.None));
        Assert.Equal("reference-a", await CallAsync<string?>(core, "GetReferenceIdAsync", token, CancellationToken.None));
        Assert.Equal(OpenIddictConstants.Statuses.Valid, await CallAsync<string?>(core, "GetStatusAsync", token, CancellationToken.None));
        Assert.Equal("subject-a", await CallAsync<string?>(core, "GetSubjectAsync", token, CancellationToken.None));
        Assert.Equal(
            OpenIddictConstants.TokenTypeHints.RefreshToken,
            await CallAsync<string?>(core, "GetTypeAsync", token, CancellationToken.None));
        Assert.Equal(0, sessions.OpenCount);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CallAsync<OpenIddictGroundworkToken>(core, "InstantiateAsync", cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            CallAsync<string?>(core, "GetPayloadAsync", token, cancellation.Token));
        Assert.Equal(0, sessions.OpenCount);
    }

    [Fact]
    public async Task Zero_page_and_unpersisted_update_delete_fail_without_session_or_token_rotation()
    {
        var sessions = new RejectingSessionFactory();
        var core = CreateCore(CreateAdmittedFactory(sessions));
        var token = await CallAsync<OpenIddictGroundworkToken>(
            core,
            "InstantiateAsync",
            CancellationToken.None);
        var concurrencyToken = token.ConcurrencyToken;

        Assert.Empty(await ToListAsync(CallEnumerable(
            core,
            "ListAsync",
            0,
            null,
            CancellationToken.None)));
        Assert.Empty(await ToListAsync(CallEnumerable(
            core,
            "ListAsync",
            0,
            17,
            CancellationToken.None)));
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            CallAsync(core, "UpdateAsync", token, CancellationToken.None));
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            CallAsync(core, "DeleteAsync", token, CancellationToken.None));

        Assert.Equal(concurrencyToken, token.ConcurrencyToken);
        Assert.Equal(0, token.PersistenceVersion);
        Assert.Equal(0, sessions.OpenCount);
    }

    [Fact]
    public async Task Relationship_bearing_writes_fail_before_session_or_token_rotation()
    {
        var sessions = new RejectingSessionFactory();
        var core = CreateCore(CreateAdmittedFactory(sessions));
        var applicationToken = await CallAsync<OpenIddictGroundworkToken>(
            core,
            "InstantiateAsync",
            CancellationToken.None);
        var authorizationToken = await CallAsync<OpenIddictGroundworkToken>(
            core,
            "InstantiateAsync",
            CancellationToken.None);
        await CallAsync(
            core,
            "SetApplicationIdAsync",
            applicationToken,
            "application-a",
            CancellationToken.None);
        await CallAsync(
            core,
            "SetAuthorizationIdAsync",
            authorizationToken,
            "authorization-a",
            CancellationToken.None);
        var applicationConcurrencyToken = applicationToken.ConcurrencyToken;
        var authorizationConcurrencyToken = authorizationToken.ConcurrencyToken;

        var applicationFailure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            CallAsync(core, "CreateAsync", applicationToken, CancellationToken.None));
        var authorizationFailure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            CallAsync(core, "CreateAsync", authorizationToken, CancellationToken.None));
        SetPersistenceVersion(applicationToken, 1);
        var updateFailure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            CallAsync(core, "UpdateAsync", applicationToken, CancellationToken.None));
        var deleteFailure = await Assert.ThrowsAnyAsync<InvalidOperationException>(() =>
            CallAsync(core, "DeleteAsync", applicationToken, CancellationToken.None));

        Assert.Equal("OpenIddictGroundworkTokenRelationshipException", applicationFailure.GetType().Name);
        Assert.Equal("OpenIddictGroundworkTokenRelationshipException", authorizationFailure.GetType().Name);
        Assert.Equal("OpenIddictGroundworkTokenRelationshipException", updateFailure.GetType().Name);
        Assert.Equal("OpenIddictGroundworkTokenRelationshipException", deleteFailure.GetType().Name);
        Assert.Contains("relationship-free", applicationFailure.Message, StringComparison.Ordinal);
        Assert.Equal(applicationConcurrencyToken, applicationToken.ConcurrencyToken);
        Assert.Equal(authorizationConcurrencyToken, authorizationToken.ConcurrencyToken);
        Assert.Equal(0, sessions.OpenCount);
    }

    [Fact]
    public async Task Local_sqlite_supports_create_unique_reference_load_cas_count_and_ordered_pages()
    {
        await using var fixture = await TokenCoreSqliteFixture.CreateAsync();
        var core = CreateCore(fixture.SessionFactory);
        await CreateTokenAsync(core, "token-c", "reference-c", "payload-c");
        await CreateTokenAsync(core, "token-a", "reference-a", "payload-a");
        await CreateTokenAsync(core, "token-b", "reference-b", "payload-b");
        var duplicate = await NewTokenAsync(core, "token-duplicate", "reference-a", "duplicate");

        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            CallAsync(core, "CreateAsync", duplicate, CancellationToken.None));

        var byId = await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByIdAsync",
            "token-a",
            CancellationToken.None);
        var byReference = await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByReferenceIdAsync",
            "reference-b",
            CancellationToken.None);
        var all = await ToListAsync(CallEnumerable(core, "ListAsync", null, null, CancellationToken.None));
        var page = await ToListAsync(CallEnumerable(core, "ListAsync", 1, 1, CancellationToken.None));

        Assert.NotNull(byId);
        Assert.NotNull(byReference);
        Assert.Equal("payload-a", byId.Payload);
        Assert.Equal("token-b", byReference.Id);
        Assert.Equal(3, await CallAsync<long>(core, "CountAsync", CancellationToken.None));
        Assert.Equal(["token-a", "token-b", "token-c"], all.Select(token => token.Id));
        Assert.Equal(["token-b"], page.Select(token => token.Id));

        var current = await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByIdAsync",
            "token-a",
            CancellationToken.None);
        var stale = await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByIdAsync",
            "token-a",
            CancellationToken.None);
        Assert.NotNull(current);
        Assert.NotNull(stale);
        var currentConcurrencyToken = current.ConcurrencyToken;
        var staleConcurrencyToken = stale.ConcurrencyToken;
        await CallAsync(core, "SetPayloadAsync", current, "payload-a-v2", CancellationToken.None);
        await CallAsync(core, "UpdateAsync", current, CancellationToken.None);

        Assert.NotEqual(currentConcurrencyToken, current.ConcurrencyToken);
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            CallAsync(core, "UpdateAsync", stale, CancellationToken.None));
        Assert.Equal(staleConcurrencyToken, stale.ConcurrencyToken);
        await Assert.ThrowsAsync<OpenIddictExceptions.ConcurrencyException>(() =>
            CallAsync(core, "DeleteAsync", stale, CancellationToken.None));

        var persisted = await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByIdAsync",
            "token-a",
            CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal("payload-a-v2", persisted.Payload);
        await CallAsync(core, "DeleteAsync", persisted, CancellationToken.None);
        Assert.Null(await CallAsync<OpenIddictGroundworkToken?>(
            core,
            "FindByIdAsync",
            "token-a",
            CancellationToken.None));
    }

    [Fact]
    public async Task Scripted_query_and_deserialization_failures_use_the_adapter_boundary_and_valid_route_shape()
    {
        var documents = new InMemoryDocumentStore(
            OpenIddictGroundworkStorageManifest.Create(),
            DocumentStoreAccess.Global);
        var queryFailure = new ScriptedQueryException();
        var providerQueries = new ScriptedBoundedDocumentStore(
            count: query =>
            {
                Assert.Equal(BoundedQueryResultOperation.Count, query.ResultOperation);
                Assert.Equal("id", Assert.Single(query.Order).Path);
                throw queryFailure;
            });
        var providerCore = CreateCore(CreateAdmittedFactory(
            new ScriptedSessionFactory(documents, providerQueries)));

        var providerException = await Assert.ThrowsAsync<OpenIddictGroundworkProviderException>(() =>
            CallAsync<long>(providerCore, "CountAsync", CancellationToken.None));

        Assert.Equal("token.CountAsync", providerException.Operation);
        Assert.Same(queryFailure, providerException.InnerException);

        var routeQueries = new ScriptedBoundedDocumentStore(first: query =>
        {
            Assert.Equal(
                OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery,
                query.QueryIdentity);
            Assert.Equal(BoundedQueryResultOperation.First, query.ResultOperation);
            Assert.Empty(query.Order);
            return null;
        });
        var routeCore = CreateCore(CreateAdmittedFactory(
            new ScriptedSessionFactory(documents, routeQueries)));

        Assert.Null(await CallAsync<OpenIddictGroundworkToken?>(
            routeCore,
            "FindByReferenceIdAsync",
            "missing-reference",
            CancellationToken.None));

        var corrupt = new DocumentEnvelope(
            OpenIddictGroundworkJson.TokenDocumentKind,
            "token-corrupt",
            "v1",
            1,
            "{",
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch);
        var corruptQueries = new ScriptedBoundedDocumentStore(
            query: query =>
            {
                Assert.Equal(OpenIddictGroundworkStorageManifest.ListTokensQuery, query.QueryIdentity);
                Assert.Equal("id", Assert.Single(query.Order).Path);
                return new DocumentQueryResult([corrupt], 1);
            });
        var corruptCore = CreateCore(CreateAdmittedFactory(
            new ScriptedSessionFactory(documents, corruptQueries)));

        var serializationException = await Assert.ThrowsAsync<OpenIddictGroundworkSerializationException>(() =>
            ToListAsync(CallEnumerable(corruptCore, "ListAsync", 1, 0, CancellationToken.None)));

        Assert.NotNull(serializationException.InnerException);
    }

    private static async Task<OpenIddictGroundworkToken> CreateTokenAsync(
        object core,
        string id,
        string referenceId,
        string payload)
    {
        var token = await NewTokenAsync(core, id, referenceId, payload);
        await CallAsync(core, "CreateAsync", token, CancellationToken.None);
        return token;
    }

    private static async Task<OpenIddictGroundworkToken> NewTokenAsync(
        object core,
        string id,
        string referenceId,
        string payload)
    {
        var token = await CallAsync<OpenIddictGroundworkToken>(
            core,
            "InstantiateAsync",
            CancellationToken.None);
        token.Id = id;
        await CallAsync(core, "SetReferenceIdAsync", token, referenceId, CancellationToken.None);
        await CallAsync(core, "SetPayloadAsync", token, payload, CancellationToken.None);
        return token;
    }

    private static object CreateCore(OpenIddictGroundworkStoreSessionFactory sessions) =>
        Activator.CreateInstance(
            CoreType,
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [sessions],
            culture: null)!;

    private static OpenIddictGroundworkStoreSessionFactory CreateAdmittedFactory(
        IGroundworkStoreSessionFactory sessions)
    {
        var source = new GroundworkStoreSessionSource();
        Assert.True(source.TrySetAdmitted(
            (_, _) => throw new InvalidOperationException("The scripted factory owns session resources."),
            TransactionBoundary.CrossUnitAtomic));
        return new OpenIddictGroundworkStoreSessionFactory(source, sessions);
    }

    private static async Task<T> CallAsync<T>(object core, string method, params object?[] arguments) =>
        await (ValueTask<T>)Invoke(core, method, arguments);

    private static async Task CallAsync(object core, string method, params object?[] arguments) =>
        await (ValueTask)Invoke(core, method, arguments);

    private static IAsyncEnumerable<OpenIddictGroundworkToken> CallEnumerable(
        object core,
        string method,
        params object?[] arguments) =>
        (IAsyncEnumerable<OpenIddictGroundworkToken>)Invoke(core, method, arguments);

    private static object Invoke(object core, string method, object?[] arguments)
    {
        var methodInfo = CoreType.GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException($"Internal token core method '{method}' was not found.");
        try
        {
            return methodInfo.Invoke(core, arguments)!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static async Task<List<T>> ToListAsync<T>(IAsyncEnumerable<T> values)
    {
        var result = new List<T>();
        await foreach (var value in values)
            result.Add(value);

        return result;
    }

    private static JsonElement Json(string value) => JsonDocument.Parse(value).RootElement.Clone();

    private static void SetPersistenceVersion(OpenIddictGroundworkToken token, long version) =>
        typeof(OpenIddictGroundworkRecord)
            .GetProperty(nameof(OpenIddictGroundworkRecord.PersistenceVersion))!
            .SetValue(token, version);

    private sealed class RejectingSessionFactory : IGroundworkStoreSessionFactory
    {
        public int OpenCount { get; private set; }

        public ValueTask<GroundworkStoreSession> CreateAsync(
            PersistenceAccessPolicy requiredPolicy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<GroundworkStoreSession> CreateOrdinaryGlobalAsync(
            CancellationToken cancellationToken = default)
        {
            OpenCount++;
            throw new InvalidOperationException("Provider access occurred.");
        }

        public ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ScriptedSessionFactory(
        IDocumentStore documents,
        IBoundedDocumentStore queries) : IGroundworkStoreSessionFactory
    {
        public ValueTask<GroundworkStoreSession> CreateAsync(
            PersistenceAccessPolicy requiredPolicy,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<GroundworkStoreSession> CreateOrdinaryGlobalAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new GroundworkStoreSession(
                documents.Access,
                new GroundworkStoreSessionResources(documents, queries)));
        }

        public ValueTask<TResult> ExecutePrivilegedAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public ValueTask<TResult> ExecutePrivilegedAcrossScopesAsync<TResult>(
            Func<GroundworkStoreSession, CancellationToken, ValueTask<TResult>> operation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ScriptedBoundedDocumentStore(
        Func<DocumentQuery, DocumentQueryResult>? query = null,
        Func<DocumentQuery, long>? count = null,
        Func<DocumentQuery, DocumentEnvelope?>? first = null) : IBoundedDocumentStore
    {
        public Task<DocumentQueryResult> QueryAsync(
            DocumentQuery documentQuery,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(query?.Invoke(documentQuery) ?? new DocumentQueryResult([], 0));
        }

        public Task<long> CountAsync(
            DocumentQuery documentQuery,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(count?.Invoke(documentQuery) ?? 0L);
        }

        public Task<DocumentEnvelope?> FirstOrDefaultAsync(
            DocumentQuery documentQuery,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(first?.Invoke(documentQuery));
        }

        public Task<bool> AnyAsync(
            DocumentQuery documentQuery,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(first?.Invoke(documentQuery) is not null);
    }

    private sealed class ScriptedQueryException : Exception;

    private sealed class TokenCoreSqliteFixture : IAsyncDisposable
    {
        private readonly string _databasePath;
        private readonly GroundworkStoreSessionSource _sessionSource;

        private TokenCoreSqliteFixture(
            string databasePath,
            GroundworkStoreSessionSource sessionSource,
            OpenIddictGroundworkStoreSessionFactory sessionFactory)
        {
            _databasePath = databasePath;
            _sessionSource = sessionSource;
            SessionFactory = sessionFactory;
        }

        public OpenIddictGroundworkStoreSessionFactory SessionFactory { get; }

        public static async Task<TokenCoreSqliteFixture> CreateAsync()
        {
            var databasePath = Path.Combine(
                Path.GetTempPath(),
                $"elsa-openiddict-token-core-{Guid.NewGuid():N}.db");
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
                    Assert.True(result.Outcome is PhysicalSchemaApplicationOutcome.Applied
                        or PhysicalSchemaApplicationOutcome.NoChanges);
                }

                var manifest = physicalSource.CreateManifest();
                var routes = physicalSource.PhysicalTarget.Routes;
                var tokenRoute = routes.Single(route =>
                    route.StorageUnit.Value == OpenIddictGroundworkJson.TokenDocumentKind);
                var sessionSource = new GroundworkStoreSessionSource();
                var sessionFactory = new OpenIddictGroundworkStoreSessionFactory(
                    sessionSource,
                    new GroundworkStoreSessionFactory(
                        new FixedAccessContextAccessor(PersistenceAccessContext.Global),
                        sessionSource));
                var fixture = new TokenCoreSqliteFixture(
                    databasePath,
                    sessionSource,
                    sessionFactory);

                Assert.True(sessionSource.TrySetAdmitted(async (access, cancellationToken) =>
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (access != DocumentStoreAccess.Global)
                        throw new InvalidOperationException("OpenIddict token storage must use explicit global access.");

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
                                OpenIddictGroundworkJson.TokenDocumentKind,
                                SqlitePhysicalQueryRuntime.Create(
                                    documents,
                                    manifest,
                                    tokenRoute,
                                    physicalSource.PhysicalTarget.Provider))
                        ]);
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

    private sealed class FixedAccessContextAccessor(PersistenceAccessContext context) :
        IPersistenceAccessContextAccessor
    {
        public PersistenceAccessContext Current { get; } = context;
    }
}
