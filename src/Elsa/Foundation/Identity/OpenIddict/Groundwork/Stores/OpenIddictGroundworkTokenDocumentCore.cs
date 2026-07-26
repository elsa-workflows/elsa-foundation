using System.Collections.Immutable;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Core.PhysicalStorage;
using Groundwork.Documents.Store;
using OpenIddict.Abstractions;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Internal relationship-free token document mechanics. This is deliberately not an
/// OpenIddict store implementation and is not registered in dependency injection.
/// </summary>
internal sealed class OpenIddictGroundworkTokenDocumentCore(
    OpenIddictGroundworkStoreSessionFactory sessions)
{
    private const int TraversalPageSize = 256;

    internal async ValueTask<long> CountAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var session = await sessions.CreateAsync(cancellationToken);
            return await session.BoundedDocumentStore.CountAsync(
                TokenQuery(OpenIddictGroundworkStorageManifest.ListTokensQuery)
                    .Select(BoundedQueryResultOperation.Count),
                cancellationToken);
        }
        catch (Exception exception)
        {
            throw Translate(exception, "token.CountAsync");
        }
    }

    internal async ValueTask CreateAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        RequireRelationshipFree(token, "token.CreateAsync");

        try
        {
            var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(token, expectedVersion: 0);
            await using var session = await sessions.CreateAsync(cancellationToken);
            var result = await session.DocumentStore.SaveAsync(request, cancellationToken);
            ApplySaved(token, result, "token.CreateAsync");
        }
        catch (Exception exception)
        {
            throw Translate(exception, "token.CreateAsync");
        }
    }

    internal async ValueTask DeleteAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        RequirePersisted(token);
        RequireRelationshipFree(token, "token.DeleteAsync");

        try
        {
            await using var session = await sessions.CreateAsync(cancellationToken);
            var result = await session.DocumentStore.DeleteAsync(
                new DeleteDocumentRequest(
                    OpenIddictGroundworkJson.TokenDocumentKind,
                    token.Id,
                    token.PersistenceVersion),
                cancellationToken);
            if (result.Status != DocumentStoreWriteStatus.Deleted)
                throw Concurrency(result.Status);

            token.PersistenceVersion = 0;
        }
        catch (Exception exception)
        {
            throw Translate(exception, "token.DeleteAsync");
        }
    }

    internal async ValueTask<OpenIddictGroundworkToken?> FindByIdAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        try
        {
            await using var session = await sessions.CreateAsync(cancellationToken);
            var document = await session.DocumentStore.LoadAsync(
                OpenIddictGroundworkJson.TokenDocumentKind,
                identifier,
                cancellationToken);
            return document is null
                ? null
                : DeserializeRelationshipFree(document, "token.FindByIdAsync");
        }
        catch (Exception exception)
        {
            throw Translate(exception, "token.FindByIdAsync");
        }
    }

    internal async ValueTask<OpenIddictGroundworkToken?> FindByReferenceIdAsync(
        string identifier,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        try
        {
            await using var session = await sessions.CreateAsync(cancellationToken);
            var document = await session.BoundedDocumentStore.FirstOrDefaultAsync(
                EqualQuery(
                    OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery,
                    "referenceId",
                    identifier)
                    .Select(BoundedQueryResultOperation.First),
                cancellationToken);
            return document is null
                ? null
                : DeserializeRelationshipFree(document, "token.FindByReferenceIdAsync");
        }
        catch (Exception exception)
        {
            throw Translate(exception, "token.FindByReferenceIdAsync");
        }
    }

    internal ValueTask<string?> GetApplicationIdAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.ApplicationId, cancellationToken);

    internal ValueTask<string?> GetAuthorizationIdAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.AuthorizationId, cancellationToken);

    internal ValueTask<DateTimeOffset?> GetCreationDateAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.CreationDate, cancellationToken);

    internal ValueTask<DateTimeOffset?> GetExpirationDateAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.ExpirationDate, cancellationToken);

    internal ValueTask<string?> GetIdAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => (string?)value.Id, cancellationToken);

    internal ValueTask<string?> GetPayloadAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.Payload, cancellationToken);

    internal ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(token.Properties.ToImmutableDictionary(
            pair => pair.Key,
            pair => pair.Value.Clone(),
            StringComparer.Ordinal));
    }

    internal ValueTask<DateTimeOffset?> GetRedemptionDateAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.RedemptionDate, cancellationToken);

    internal ValueTask<string?> GetReferenceIdAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.ReferenceId, cancellationToken);

    internal ValueTask<string?> GetStatusAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.Status, cancellationToken);

    internal ValueTask<string?> GetSubjectAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.Subject, cancellationToken);

    internal ValueTask<string?> GetTypeAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken) =>
        Scalar(token, value => value.Type, cancellationToken);

    internal ValueTask<OpenIddictGroundworkToken> InstantiateAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new OpenIddictGroundworkToken());
    }

    internal IAsyncEnumerable<OpenIddictGroundworkToken> ListAsync(
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
            TokenQuery(OpenIddictGroundworkStorageManifest.ListTokensQuery)
                .Page(offset, count),
            "token.ListAsync",
            cancellationToken,
            singlePage: count.HasValue);
    }

    internal ValueTask SetApplicationIdAsync(
        OpenIddictGroundworkToken token,
        string? identifier,
        CancellationToken cancellationToken) =>
        Set(token, value => value.ApplicationId = identifier, cancellationToken);

    internal ValueTask SetAuthorizationIdAsync(
        OpenIddictGroundworkToken token,
        string? identifier,
        CancellationToken cancellationToken) =>
        Set(token, value => value.AuthorizationId = identifier, cancellationToken);

    internal ValueTask SetCreationDateAsync(
        OpenIddictGroundworkToken token,
        DateTimeOffset? date,
        CancellationToken cancellationToken) =>
        Set(token, value => value.CreationDate = date, cancellationToken);

    internal ValueTask SetExpirationDateAsync(
        OpenIddictGroundworkToken token,
        DateTimeOffset? date,
        CancellationToken cancellationToken) =>
        Set(token, value => value.ExpirationDate = date, cancellationToken);

    internal ValueTask SetPayloadAsync(
        OpenIddictGroundworkToken token,
        string? payload,
        CancellationToken cancellationToken) =>
        Set(token, value => value.Payload = payload, cancellationToken);

    internal ValueTask SetPropertiesAsync(
        OpenIddictGroundworkToken token,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return Set(token, value => value.Properties = new SortedDictionary<string, JsonElement>(
            properties.ToDictionary(pair => pair.Key, pair => pair.Value.Clone(), StringComparer.Ordinal),
            StringComparer.Ordinal), cancellationToken);
    }

    internal ValueTask SetRedemptionDateAsync(
        OpenIddictGroundworkToken token,
        DateTimeOffset? date,
        CancellationToken cancellationToken) =>
        Set(token, value => value.RedemptionDate = date, cancellationToken);

    internal ValueTask SetReferenceIdAsync(
        OpenIddictGroundworkToken token,
        string? identifier,
        CancellationToken cancellationToken) =>
        Set(token, value => value.ReferenceId = identifier, cancellationToken);

    internal ValueTask SetStatusAsync(
        OpenIddictGroundworkToken token,
        string? status,
        CancellationToken cancellationToken) =>
        Set(token, value => value.Status = status, cancellationToken);

    internal ValueTask SetSubjectAsync(
        OpenIddictGroundworkToken token,
        string? subject,
        CancellationToken cancellationToken) =>
        Set(token, value => value.Subject = subject, cancellationToken);

    internal ValueTask SetTypeAsync(
        OpenIddictGroundworkToken token,
        string? type,
        CancellationToken cancellationToken) =>
        Set(token, value => value.Type = type, cancellationToken);

    internal async ValueTask UpdateAsync(
        OpenIddictGroundworkToken token,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        RequirePersisted(token);
        RequireRelationshipFree(token, "token.UpdateAsync");

        var previousConcurrencyToken = token.ConcurrencyToken;
        token.ConcurrencyToken = Guid.NewGuid().ToString("N");
        try
        {
            var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(
                token,
                token.PersistenceVersion);
            await using var session = await sessions.CreateAsync(cancellationToken);
            var result = await session.DocumentStore.SaveAsync(request, cancellationToken);
            ApplySaved(token, result, "token.UpdateAsync");
        }
        catch (Exception exception)
        {
            token.ConcurrencyToken = previousConcurrencyToken;
            throw Translate(exception, "token.UpdateAsync");
        }
    }

    private async IAsyncEnumerable<OpenIddictGroundworkToken> QueryAllAsync(
        DocumentQuery query,
        string operation,
        [EnumeratorCancellation] CancellationToken cancellationToken,
        bool singlePage)
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
                OpenIddictGroundworkToken token;
                try
                {
                    token = DeserializeRelationshipFree(document, operation);
                }
                catch (Exception exception)
                {
                    throw Translate(exception, operation);
                }

                yield return token;
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

    private static async IAsyncEnumerable<OpenIddictGroundworkToken> EmptyAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        yield break;
    }

    private static DocumentQuery TokenQuery(
        string identity,
        IReadOnlyList<DocumentQueryClause>? clauses = null) =>
        new(
            OpenIddictGroundworkJson.TokenDocumentKind,
            identity,
            clauses,
            identity == OpenIddictGroundworkStorageManifest.ListTokensQuery
                ? [new DocumentQueryOrder("id")]
                : []);

    private static DocumentQuery EqualQuery(string identity, string path, string value) =>
        TokenQuery(identity, [DocumentQueryClause.Of(DocumentQueryComparison.Equal(path, value))]);

    private static ValueTask<TResult> Scalar<TResult>(
        OpenIddictGroundworkToken token,
        Func<OpenIddictGroundworkToken, TResult> selector,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(selector(token));
    }

    private static ValueTask Set(
        OpenIddictGroundworkToken token,
        Action<OpenIddictGroundworkToken> setter,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        cancellationToken.ThrowIfCancellationRequested();
        setter(token);
        return ValueTask.CompletedTask;
    }

    private static OpenIddictGroundworkToken DeserializeRelationshipFree(
        DocumentEnvelope document,
        string operation)
    {
        var token = OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(document);
        RequireRelationshipFree(token, operation);
        return token;
    }

    private static void RequirePersisted(OpenIddictGroundworkToken token)
    {
        if (token.PersistenceVersion <= 0)
            throw Concurrency(DocumentStoreWriteStatus.ConcurrencyConflict);
    }

    private static void RequireRelationshipFree(OpenIddictGroundworkToken token, string operation)
    {
        if (token.ApplicationId is not null || token.AuthorizationId is not null)
            throw new OpenIddictGroundworkTokenRelationshipException(operation);
    }

    private static void ApplySaved(
        OpenIddictGroundworkToken token,
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

        token.PersistenceVersion = result.Document.Version;
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
            or OpenIddictGroundworkReadinessException
            or OpenIddictGroundworkSerializationException
            or OpenIddictGroundworkTokenRelationshipException
            ? exception
            : OpenIddictGroundworkFailureMapper.Translate(exception, operation);
}

internal sealed class OpenIddictGroundworkTokenRelationshipException(string operation) :
    InvalidOperationException(
        $"OpenIddict Groundwork token operation '{operation}' cannot persist an application or authorization relationship in the relationship-free document core.")
{
    internal string Operation { get; } = operation;
}
