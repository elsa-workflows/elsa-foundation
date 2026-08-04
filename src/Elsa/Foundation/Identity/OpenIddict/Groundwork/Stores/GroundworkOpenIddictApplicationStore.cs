using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Querying;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Documents.Store;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using Elsa.Persistence.Groundwork.Stores;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IOpenIddictApplicationStore{TApplication}"/>. Applications are global (not
/// tenant-scoped): the document id is an opaque identity and the OpenIddict-visible unique key is
/// <c>clientId</c>, which is enforced by the manifest's declared unique index rather than a pre-flight
/// duplicate read.
/// </summary>
public sealed class GroundworkOpenIddictApplicationStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : IOpenIddictApplicationStore<OpenIddictGroundworkApplication>
{
    // Groundwork admits no cursor paging on collection-membership routes (GW-QUERY-008), so each uri
    // lookup is one bounded page; this is the fail-closed ceiling on it.
    private const int MaxUriMaterialization = 10_000;

    private const string ClientIdField = "clientId";
    private const string RedirectUrisField = "redirectUris";
    private const string PostLogoutRedirectUrisField = "postLogoutRedirectUris";

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    /// <summary>
    /// Rejected: the manifest declares no bounded count-all route for applications. Its only named routes
    /// are the client-id, redirect-uri, and post-logout-redirect-uri lookups.
    /// </summary>
    /// <remarks>
    /// The portable query surface would answer this at the provider, but it is forbidden in production
    /// Groundwork reads by <c>ArchitectureGuardTests.Groundwork_production_reads_use_only_admitted_bounded_query_APIs</c>,
    /// and with no declared id index it has no guaranteed order anyway. Rejecting keeps the missing route
    /// visible instead of shipping an unordered fallback, matching how
    /// <see cref="OpenIddictGroundworkGenericQueryTranslator"/> already refuses unsupported query shapes.
    /// Declare a count-all route in the manifest to admit this member.
    /// </remarks>
    public ValueTask<long> CountAsync(CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("application.CountAsync");

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<OpenIddictGroundworkApplication>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute<OpenIddictGroundworkApplication, TResult>(query, "application.CountAsync");
        throw new UnreachableException("RequireCountRoute always throws before returning.");
    }

    public async ValueTask CreateAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(application, expectedVersion: 0);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.create");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        application.PersistenceVersion = result.Document!.Version;
    }

    public async ValueTask DeleteAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var request = new DeleteDocumentRequest(
            OpenIddictGroundworkJson.ApplicationDocumentKind,
            application.Id,
            application.PersistenceVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.DeleteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.delete");
        }

        if (result.Status != DocumentStoreWriteStatus.Deleted)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);
    }

    public async ValueTask<OpenIddictGroundworkApplication?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        DocumentEnvelope? envelope;
        try
        {
            envelope = await store.LoadAsync(OpenIddictGroundworkJson.ApplicationDocumentKind, identifier, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.findById");
        }

        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkApplication>(envelope);
    }

    public async ValueTask<OpenIddictGroundworkApplication?> FindByClientIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var envelope = await FindByClientIdEnvelopeAsync(identifier, cancellationToken);
        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkApplication>(envelope);
    }

    private async ValueTask<DocumentEnvelope?> FindByClientIdEnvelopeAsync(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedStore.FirstOrDefaultAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.ApplicationDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindApplicationByClientIdQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ClientIdField, identifier))]),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.findByClientId");
        }
    }

    public async IAsyncEnumerable<OpenIddictGroundworkApplication> FindByPostLogoutRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        var documents = await QueryByPostLogoutRedirectUriAsync(uri, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkApplication>(document);
    }

    /// <summary>
    /// The declared route admits <c>CollectionContains</c> with offset paging and no sort support, a
    /// combination neither <see cref="BoundedDocumentQueryPager"/> helper accepts (<c>QueryAllAsync</c>
    /// requires cursor paging; <c>QueryAllOffsetAsync</c> requires a non-empty declared order). So this is
    /// one bounded page rather than real pagination: the provider-reported total count is compared against
    /// the page actually returned, and a match set that would exceed the page throws instead of silently
    /// truncating. Declare cursor paging for this route (as the scope resource route now has) to admit
    /// genuine exhaustive iteration.
    /// </summary>
    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryByPostLogoutRedirectUriAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.ApplicationDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindApplicationByPostLogoutRedirectUriQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContains(PostLogoutRedirectUrisField, uri))],
                    take: MaxUriMaterialization),
                cancellationToken);

            if (result.TotalCount > result.Documents.Count)
            {
                throw new InvalidOperationException(
                    $"Document query '{OpenIddictGroundworkStorageManifest.FindApplicationByPostLogoutRedirectUriQuery}' matched " +
                    $"{result.TotalCount} applications, exceeding the bounded materialization limit of {MaxUriMaterialization}.");
            }

            return result.Documents;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.findByPostLogoutRedirectUri");
        }
    }

    public async IAsyncEnumerable<OpenIddictGroundworkApplication> FindByRedirectUriAsync(
        string uri,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(uri);

        var documents = await QueryByRedirectUriAsync(uri, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkApplication>(document);
    }

    /// <summary>
    /// Same route-shape gap as <see cref="QueryByPostLogoutRedirectUriAsync"/>: one bounded, total-count
    /// validated page rather than real pagination.
    /// </summary>
    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryByRedirectUriAsync(string uri, CancellationToken cancellationToken)
    {
        try
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.ApplicationDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindApplicationByRedirectUriQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContains(RedirectUrisField, uri))],
                    take: MaxUriMaterialization),
                cancellationToken);

            if (result.TotalCount > result.Documents.Count)
            {
                throw new InvalidOperationException(
                    $"Document query '{OpenIddictGroundworkStorageManifest.FindApplicationByRedirectUriQuery}' matched " +
                    $"{result.TotalCount} applications, exceeding the bounded materialization limit of {MaxUriMaterialization}.");
            }

            return result.Documents;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.findByRedirectUri");
        }
    }

    public ValueTask<string?> GetApplicationTypeAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.ApplicationType);
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkApplication, TState, TResult>(query, "application.GetAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask<string?> GetClientIdAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.ClientId);
    }

    public ValueTask<string?> GetClientSecretAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.ClientSecret);
    }

    public ValueTask<string?> GetClientTypeAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.ClientType);
    }

    public ValueTask<string?> GetConsentTypeAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.ConsentType);
    }

    public ValueTask<string?> GetDisplayNameAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.DisplayName);
    }

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(ToCultureMap(application.DisplayNames));
    }

    public ValueTask<string?> GetIdAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult<string?>(application.Id);
    }

    public ValueTask<JsonWebKeySet?> GetJsonWebKeySetAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(string.IsNullOrEmpty(application.JsonWebKeySet)
            ? null
            : JsonWebKeySet.Create(application.JsonWebKeySet));
    }

    public ValueTask<ImmutableArray<string>> GetPermissionsAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.Permissions.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(application.Permissions));
    }

    public ValueTask<ImmutableArray<string>> GetPostLogoutRedirectUrisAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.PostLogoutRedirectUris.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(application.PostLogoutRedirectUris));
    }

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.Properties.Count == 0
            ? ImmutableDictionary<string, JsonElement>.Empty
            : application.Properties.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public ValueTask<ImmutableArray<string>> GetRedirectUrisAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.RedirectUris.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(application.RedirectUris));
    }

    public ValueTask<ImmutableArray<string>> GetRequirementsAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.Requirements.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(application.Requirements));
    }

    public ValueTask<ImmutableDictionary<string, string>> GetSettingsAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        return ValueTask.FromResult(application.Settings.Count == 0
            ? ImmutableDictionary<string, string>.Empty
            : application.Settings.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public ValueTask<OpenIddictGroundworkApplication> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OpenIddictGroundworkApplication());

    /// <summary>
    /// Rejected for the same reason as <see cref="CountAsync(CancellationToken)"/>: no bounded list-all
    /// route is declared, and paging without a declared id index would have no guaranteed order.
    /// </summary>
    public async IAsyncEnumerable<OpenIddictGroundworkApplication> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryAllApplicationsAsync(count, offset, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkApplication>(document);
    }

    private static ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllApplicationsAsync(int? count, int? offset, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("application.ListAsync");

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkApplication>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkApplication, TState, TResult>(query, "application.ListAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask SetApplicationTypeAsync(OpenIddictGroundworkApplication application, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ApplicationType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientIdAsync(OpenIddictGroundworkApplication application, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ClientId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientSecretAsync(OpenIddictGroundworkApplication application, string? secret, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ClientSecret = secret;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetClientTypeAsync(OpenIddictGroundworkApplication application, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ClientType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetConsentTypeAsync(OpenIddictGroundworkApplication application, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.ConsentType = type;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNameAsync(OpenIddictGroundworkApplication application, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.DisplayName = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNamesAsync(
        OpenIddictGroundworkApplication application,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(names);
        application.DisplayNames = FromCultureMap(names);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetJsonWebKeySetAsync(OpenIddictGroundworkApplication application, JsonWebKeySet? set, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.JsonWebKeySet = set is null ? null : JsonSerializer.Serialize(set);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPermissionsAsync(OpenIddictGroundworkApplication application, ImmutableArray<string> permissions, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Permissions = permissions.IsDefaultOrEmpty ? [] : permissions.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPostLogoutRedirectUrisAsync(OpenIddictGroundworkApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.PostLogoutRedirectUris = uris.IsDefaultOrEmpty ? [] : uris.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(
        OpenIddictGroundworkApplication application,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(properties);
        application.Properties = new SortedDictionary<string, JsonElement>(properties, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRedirectUrisAsync(OpenIddictGroundworkApplication application, ImmutableArray<string> uris, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.RedirectUris = uris.IsDefaultOrEmpty ? [] : uris.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRequirementsAsync(OpenIddictGroundworkApplication application, ImmutableArray<string> requirements, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        application.Requirements = requirements.IsDefaultOrEmpty ? [] : requirements.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSettingsAsync(
        OpenIddictGroundworkApplication application,
        ImmutableDictionary<string, string> settings,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);
        ArgumentNullException.ThrowIfNull(settings);
        application.Settings = new SortedDictionary<string, string>(settings, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(OpenIddictGroundworkApplication application, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(application);

        var expectedVersion = application.PersistenceVersion;
        application.ConcurrencyToken = Guid.NewGuid().ToString("N");

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(application, expectedVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "application.update");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        application.PersistenceVersion = result.Document!.Version;
    }

    private static ImmutableDictionary<CultureInfo, string> ToCultureMap(IReadOnlyDictionary<string, string> source)
    {
        if (source.Count == 0)
            return ImmutableDictionary<CultureInfo, string>.Empty;

        var builder = ImmutableDictionary.CreateBuilder<CultureInfo, string>();
        foreach (var pair in source)
            builder[CultureInfo.GetCultureInfo(pair.Key)] = pair.Value;

        return builder.ToImmutable();
    }

    private static SortedDictionary<string, string> FromCultureMap(ImmutableDictionary<CultureInfo, string> source)
    {
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in source)
            result[pair.Key.Name] = pair.Value;

        return result;
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("OpenIddict application queries require an admitted bounded document-store runtime.");
}
