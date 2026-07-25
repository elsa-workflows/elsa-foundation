using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Querying;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>Provider-neutral OpenIddict scope store backed only by admitted Groundwork routes.</summary>
public sealed class GroundworkOpenIddictScopeStore(
    OpenIddictGroundworkStoreSessionFactory sessions)
    : IOpenIddictScopeStore<OpenIddictGroundworkScope>
{
    private const int TraversalPageSize = 256;

    /// <summary>
    /// Maximum number of distinct names accepted by one lookup. The ceiling preserves headroom below
    /// SQLite's reviewed 999-parameter default, the narrowest mandatory-provider limit, for route metadata.
    /// </summary>
    public const int MaximumNamesPerLookup = 900;

    public async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        await using var session = await sessions.CreateAsync(cancellationToken);
        try
        {
            return await session.BoundedDocumentStore.CountAsync(
                ScopeQuery(OpenIddictGroundworkStorageManifest.ListScopesQuery)
                    .Select(BoundedQueryResultOperation.Count),
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "scope.CountAsync");
        }
    }

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute(query, "scope.CountAsync");
        throw new UnreachableException();
    }

    public async ValueTask CreateAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        await using var session = await sessions.CreateAsync(cancellationToken);
        try
        {
            var result = await session.DocumentStore.SaveAsync(
                OpenIddictGroundworkRecordSerializer.CreateSaveRequest(scope, expectedVersion: 0),
                cancellationToken);
            ApplySaved(scope, result, "scope.CreateAsync");
        }
        catch (Exception exception)
        {
            throw Translate(exception, "scope.CreateAsync");
        }
    }

    public async ValueTask DeleteAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.PersistenceVersion <= 0)
            throw Concurrency(DocumentStoreWriteStatus.ConcurrencyConflict);

        await using var session = await sessions.CreateAsync(cancellationToken);
        try
        {
            var result = await session.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(
                    OpenIddictGroundworkJson.ScopeDocumentKind,
                    scope.Id,
                    scope.PersistenceVersion),
                cancellationToken);
            if (result.Status != DocumentStoreWriteStatus.Deleted)
                throw Concurrency(result.Status);

            scope.PersistenceVersion = 0;
        }
        catch (Exception exception)
        {
            throw Translate(exception, "scope.DeleteAsync");
        }
    }

    public async ValueTask<OpenIddictGroundworkScope?> FindByIdAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        await using var session = await sessions.CreateAsync(cancellationToken);
        try
        {
            var document = await session.DocumentStore.LoadAsync(
                OpenIddictGroundworkJson.ScopeDocumentKind,
                identifier,
                cancellationToken);
            return document is null
                ? null
                : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(document);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "scope.FindByIdAsync");
        }
    }

    public async ValueTask<OpenIddictGroundworkScope?> FindByNameAsync(
        string name,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await using var session = await sessions.CreateAsync(cancellationToken);
        try
        {
            var document = await session.BoundedDocumentStore.FirstOrDefaultAsync(
                EqualQuery(OpenIddictGroundworkStorageManifest.FindScopeByNameQuery, "name", name)
                    .Select(BoundedQueryResultOperation.First),
                cancellationToken);
            return document is null
                ? null
                : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(document);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "scope.FindByNameAsync");
        }
    }

    public IAsyncEnumerable<OpenIddictGroundworkScope> FindByNamesAsync(
        ImmutableArray<string> names,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (names.IsDefaultOrEmpty)
            return EmptyAsync(cancellationToken);

        var distinctNames = names.Distinct(StringComparer.Ordinal).ToArray();
        foreach (var name in distinctNames)
            ArgumentException.ThrowIfNullOrEmpty(name);

        if (distinctNames.Length > MaximumNamesPerLookup)
        {
            throw new ArgumentOutOfRangeException(
                nameof(names),
                distinctNames.Length,
                $"At most {MaximumNamesPerLookup} distinct scope names can be looked up in one operation.");
        }

        return QueryAllAsync(
            ScopeQuery(
                OpenIddictGroundworkStorageManifest.FindScopesByNamesQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.In("name", distinctNames))]),
            "scope.FindByNamesAsync",
            cancellationToken);
    }

    public IAsyncEnumerable<OpenIddictGroundworkScope> FindByResourceAsync(
        string resource,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(resource);
        return QueryAllAsync(
            ScopeQuery(
                OpenIddictGroundworkStorageManifest.FindScopeByResourceQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContains("resources", resource))]),
            "scope.FindByResourceAsync",
            cancellationToken);
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute(query, "scope.GetAsync");
        throw new UnreachableException();
    }

    public ValueTask<string?> GetDescriptionAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        Scalar(scope, value => value.Description, cancellationToken);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDescriptionsAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        CultureMap(scope, value => value.Descriptions, cancellationToken);

    public ValueTask<string?> GetDisplayNameAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        Scalar(scope, value => value.DisplayName, cancellationToken);

    public ValueTask<ImmutableDictionary<CultureInfo, string>> GetDisplayNamesAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        CultureMap(scope, value => value.DisplayNames, cancellationToken);

    public ValueTask<string?> GetIdAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        Scalar(scope, value => (string?)value.Id, cancellationToken);

    public ValueTask<string?> GetNameAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        Scalar(scope, value => value.Name, cancellationToken);

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(scope.Properties.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal));
    }

    public ValueTask<ImmutableArray<string>> GetResourcesAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken) =>
        Scalar(scope, value => value.Resources.ToImmutableArray(), cancellationToken);

    public ValueTask<OpenIddictGroundworkScope> InstantiateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OpenIddictGroundworkScope());
    }

    public IAsyncEnumerable<OpenIddictGroundworkScope> ListAsync(
        int? count,
        int? offset,
        CancellationToken cancellationToken)
    {
        if (count is < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (offset is < 0)
            throw new ArgumentOutOfRangeException(nameof(offset));
        if (count is 0)
            return EmptyAsync(cancellationToken);

        return QueryAllAsync(
            ScopeQuery(OpenIddictGroundworkStorageManifest.ListScopesQuery)
                .Page(offset, count),
            "scope.ListAsync",
            cancellationToken,
            singlePage: count.HasValue);
    }

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkScope>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute(query, "scope.ListAsync");
        throw new UnreachableException();
    }

    public ValueTask SetDescriptionAsync(
        OpenIddictGroundworkScope scope,
        string? description,
        CancellationToken cancellationToken) =>
        Set(scope, value => value.Description = description, cancellationToken);

    public ValueTask SetDescriptionsAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<CultureInfo, string> descriptions,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(descriptions);
        return Set(scope, value => value.Descriptions = ToStoredCultureMap(descriptions), cancellationToken);
    }

    public ValueTask SetDisplayNameAsync(
        OpenIddictGroundworkScope scope,
        string? name,
        CancellationToken cancellationToken) =>
        Set(scope, value => value.DisplayName = name, cancellationToken);

    public ValueTask SetDisplayNamesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<CultureInfo, string> names,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(names);
        return Set(scope, value => value.DisplayNames = ToStoredCultureMap(names), cancellationToken);
    }

    public ValueTask SetNameAsync(
        OpenIddictGroundworkScope scope,
        string? name,
        CancellationToken cancellationToken) =>
        Set(scope, value => value.Name = name, cancellationToken);

    public ValueTask SetPropertiesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Set(scope, value => value.Properties = new SortedDictionary<string, JsonElement>(
            properties.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            StringComparer.Ordinal), cancellationToken);
    }

    public ValueTask SetResourcesAsync(
        OpenIddictGroundworkScope scope,
        ImmutableArray<string> resources,
        CancellationToken cancellationToken) =>
        Set(
            scope,
            value => value.Resources = resources.IsDefaultOrEmpty ? [] : resources.ToArray(),
            cancellationToken);

    public async ValueTask UpdateAsync(
        OpenIddictGroundworkScope scope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (scope.PersistenceVersion <= 0)
            throw Concurrency(DocumentStoreWriteStatus.ConcurrencyConflict);

        await using var session = await sessions.CreateAsync(cancellationToken);
        var previousConcurrencyToken = scope.ConcurrencyToken;
        scope.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try
        {
            var result = await session.DocumentStore.SaveAsync(
                OpenIddictGroundworkRecordSerializer.CreateSaveRequest(scope, scope.PersistenceVersion),
                cancellationToken);
            ApplySaved(scope, result, "scope.UpdateAsync");
        }
        catch (Exception exception)
        {
            scope.ConcurrencyToken = previousConcurrencyToken;
            throw Translate(exception, "scope.UpdateAsync");
        }
    }

    private async IAsyncEnumerable<OpenIddictGroundworkScope> QueryAllAsync(
        DocumentQuery query,
        string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool singlePage = false)
    {
        await using var session = await sessions.CreateAsync(cancellationToken);
        var skip = query.Skip ?? 0;
        var remaining = query.Take;
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var take = singlePage
                ? remaining
                : remaining is null
                    ? TraversalPageSize
                    : Math.Min(TraversalPageSize, remaining.Value);
            DocumentQueryResult page;
            try
            {
                page = await session.BoundedDocumentStore.QueryAsync(
                    query.Page(skip, take),
                    cancellationToken);
            }
            catch (Exception exception)
            {
                throw Translate(exception, operation);
            }

            foreach (var document in page.Documents)
            {
                OpenIddictGroundworkScope scope;
                try
                {
                    scope = OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkScope>(document);
                }
                catch (Exception exception)
                {
                    throw Translate(exception, operation);
                }

                yield return scope;
            }

            skip += page.Documents.Count;
            if (singlePage ||
                page.Documents.Count == 0 ||
                skip >= page.TotalCount ||
                remaining is not null && page.Documents.Count >= remaining.Value)
            {
                yield break;
            }

            if (remaining is not null)
                remaining -= page.Documents.Count;
        }
    }

    private static async IAsyncEnumerable<OpenIddictGroundworkScope> EmptyAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    private static DocumentQuery ScopeQuery(
        string identity,
        IReadOnlyList<DocumentQueryClause>? clauses = null) =>
        new(
            OpenIddictGroundworkJson.ScopeDocumentKind,
            identity,
            clauses,
            identity == OpenIddictGroundworkStorageManifest.ListScopesQuery
                ? [new DocumentQueryOrder("id")]
                : []);

    private static DocumentQuery EqualQuery(string identity, string path, string value) =>
        ScopeQuery(identity, [DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value))]);

    private static ValueTask<TResult> Scalar<TResult>(
        OpenIddictGroundworkScope scope,
        Func<OpenIddictGroundworkScope, TResult> selector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(selector(scope));
    }

    private static ValueTask<ImmutableDictionary<CultureInfo, string>> CultureMap(
        OpenIddictGroundworkScope scope,
        Func<OpenIddictGroundworkScope, SortedDictionary<string, string>> selector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            return ValueTask.FromResult(selector(scope).ToImmutableDictionary(
                pair => ToCultureInfo(pair.Key),
                pair => pair.Value));
        }
        catch (CultureNotFoundException exception)
        {
            throw new OpenIddictGroundworkSerializationException(
                "An OpenIddict scope contains an invalid culture identity.",
                exception);
        }
    }

    private static ValueTask Set(
        OpenIddictGroundworkScope scope,
        Action<OpenIddictGroundworkScope> setter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(scope);
        cancellationToken.ThrowIfCancellationRequested();
        setter(scope);
        return ValueTask.CompletedTask;
    }

    private static SortedDictionary<string, string> ToStoredCultureMap(
        ImmutableDictionary<CultureInfo, string> values)
    {
        var stored = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var pair in values)
            stored[pair.Key.Name] = pair.Value;

        return stored;
    }

    private static CultureInfo ToCultureInfo(string name) =>
        string.IsNullOrEmpty(name) ? CultureInfo.InvariantCulture : CultureInfo.GetCultureInfo(name);

    private static void ApplySaved(
        OpenIddictGroundworkScope scope,
        DocumentStoreWriteResult result,
        string operation)
    {
        if (result.Status != DocumentStoreWriteStatus.Saved || result.Document is null)
        {
            throw result.Status is DocumentStoreWriteStatus.ConcurrencyConflict or DocumentStoreWriteStatus.NotFound
                ? Concurrency(result.Status)
                : new OpenIddictGroundworkProviderException(
                    operation,
                    new InvalidOperationException($"Groundwork document write returned '{result.Status}'."));
        }

        scope.PersistenceVersion = result.Document.Version;
    }

    private static Exception Concurrency(DocumentStoreWriteStatus status) =>
        OpenIddictGroundworkFailureMapper.WriteFailure(
            status == DocumentStoreWriteStatus.NotFound
                ? DocumentStoreWriteStatus.ConcurrencyConflict
                : status);

    private static Exception Translate(Exception exception, string operation) =>
        exception is OperationCanceledException or OpenIddictExceptions.ConcurrencyException
            or OpenIddictGroundworkCapabilityException
            or OpenIddictGroundworkProviderException
            or OpenIddictGroundworkSerializationException
            ? exception
            : OpenIddictGroundworkFailureMapper.Translate(exception, operation);
}
