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
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IOpenIddictScopeStore{TScope}"/>. Scopes are global (not tenant-scoped): the
/// document id is an opaque identity and the OpenIddict-visible unique key is <c>name</c>, which is enforced
/// by the manifest's declared unique index rather than a pre-flight duplicate read.
/// </summary>
public sealed class GroundworkOpenIddictScopeStore(
    IDocumentStore store,
    IBoundedDocumentStore? boundedStore = null) : IOpenIddictScopeStore<OpenIddictGroundworkScope>
{
    // Resource-membership matches are expected to be a small, admin-managed set. The declared collection
    // route (see below) has no admitted cursor or sort capability, so it is exhausted as one bounded page
    // rather than paged; this constant is the fail-closed bound on that single page.
    private const int MaxResourceMaterialization = 10_000;

    private const string NameField = "name";
    private const string ResourcesField = "resources";

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    /// <summary>
    /// Rejected: the manifest declares no bounded count-all route for scopes. Its only named routes are the
    /// name and resource lookups.
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
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("scope.CountAsync");

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute<OpenIddictGroundworkScope, TResult>(query, "scope.CountAsync");
        throw new UnreachableException("RequireCountRoute always throws before returning.");
    }

    public async ValueTask CreateAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(scope, expectedVersion: 0);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.create");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        scope.PersistenceVersion = result.Document!.Version;
    }

    public async ValueTask DeleteAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var request = new DeleteDocumentRequest(
            OpenIddictGroundworkJson.ScopeDocumentKind,
            scope.Id,
            scope.PersistenceVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.DeleteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.delete");
        }

        if (result.Status != DocumentStoreWriteStatus.Deleted)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);
    }

    public async ValueTask<OpenIddictGroundworkScope?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        DocumentEnvelope? envelope;
        try
        {
            envelope = await store.LoadAsync(OpenIddictGroundworkJson.ScopeDocumentKind, identifier, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.findById");
        }

        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(envelope);
    }

    public async ValueTask<OpenIddictGroundworkScope?> FindByNameAsync(string name, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var envelope = await FindByNameEnvelopeAsync(name, cancellationToken);
        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(envelope);
    }

    public async IAsyncEnumerable<OpenIddictGroundworkScope> FindByNamesAsync(
        ImmutableArray<string> names,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        if (names.IsDefault)
            throw new ArgumentNullException(nameof(names));
        if (names.Any(string.IsNullOrEmpty))
            throw new ArgumentException("Scope names cannot be null or empty.", nameof(names));

        // The declared name route only admits Equal (see OpenIddictGroundworkStorageManifest.ScopeNameQuery),
        // not an "In" membership test, so a finite name set is resolved as one Equal lookup per name against
        // the same declared route, rather than a single multi-value query or an in-memory filter.
        foreach (var name in names)
        {
            var envelope = await FindByNameEnvelopeAsync(name, cancellationToken);
            if (envelope is not null)
                yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(envelope);
        }
    }

    public async IAsyncEnumerable<OpenIddictGroundworkScope> FindByResourceAsync(
        string resource,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);

        var documents = await QueryByResourceAsync(resource, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(document);
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkScope, TState, TResult>(query, "scope.GetAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask<string?> GetDescriptionAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(scope.Description);
    }

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(ToCultureMap(scope.Descriptions));
    }

    public ValueTask<string?> GetDisplayNameAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(scope.DisplayName);
    }

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(ToCultureMap(scope.DisplayNames));
    }

    public ValueTask<string?> GetIdAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult<string?>(scope.Id);
    }

    public ValueTask<string?> GetNameAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(scope.Name);
    }

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(scope.Properties.Count == 0
            ? ImmutableDictionary<string, JsonElement>.Empty
            : scope.Properties.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public ValueTask<ImmutableArray<string>> GetResourcesAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        return ValueTask.FromResult(scope.Resources.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(scope.Resources));
    }

    public ValueTask<OpenIddictGroundworkScope> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OpenIddictGroundworkScope());

    public async IAsyncEnumerable<OpenIddictGroundworkScope> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryAllScopesAsync(count, offset, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(document);
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkScope, TState, TResult>(query, "scope.ListAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask SetDescriptionAsync(OpenIddictGroundworkScope scope, string? description, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Description = description;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDescriptionsAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<CultureInfo, string> descriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(descriptions);
        scope.Descriptions = FromCultureMap(descriptions);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNameAsync(OpenIddictGroundworkScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.DisplayName = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetDisplayNamesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(names);
        scope.DisplayNames = FromCultureMap(names);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetNameAsync(OpenIddictGroundworkScope scope, string? name, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Name = name;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentNullException.ThrowIfNull(properties);
        scope.Properties = new SortedDictionary<string, JsonElement>(properties, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetResourcesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableArray<string> resources,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        scope.Resources = resources.IsDefaultOrEmpty ? [] : resources.ToArray();
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(OpenIddictGroundworkScope scope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);

        var expectedVersion = scope.PersistenceVersion;
        scope.ConcurrencyToken = Guid.NewGuid().ToString("N");

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(scope, expectedVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.update");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        scope.PersistenceVersion = result.Document!.Version;
    }

    private async ValueTask<DocumentEnvelope?> FindByNameEnvelopeAsync(string name, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedStore.FirstOrDefaultAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.ScopeDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindScopeByNameQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(NameField, name))]),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.findByName");
        }
    }

    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryByResourceAsync(string resource, CancellationToken cancellationToken)
    {
        try
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.ScopeDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindScopeByResourceQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContains(ResourcesField, resource))],
                    take: MaxResourceMaterialization),
                cancellationToken);

            if (result.TotalCount > result.Documents.Count)
            {
                throw new InvalidOperationException(
                    $"Document query '{OpenIddictGroundworkStorageManifest.FindScopeByResourceQuery}' matched " +
                    $"{result.TotalCount} scopes, exceeding the bounded materialization limit of {MaxResourceMaterialization}.");
            }

            return result.Documents;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "scope.findByResource");
        }
    }

    /// <summary>
    /// Rejected for the same reason as <see cref="CountAsync(CancellationToken)"/>: no bounded list-all
    /// route is declared, and paging without a declared id index would have no guaranteed order.
    /// </summary>
    private static ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllScopesAsync(int? count, int? offset, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("scope.ListAsync");

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
        ?? throw new InvalidOperationException("OpenIddict scope queries require an admitted bounded document-store runtime.");
}
