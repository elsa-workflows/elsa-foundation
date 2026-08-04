using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Exceptions;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Models;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Querying;
using Elsa.Foundation.Identity.OpenIddict.Groundwork.Serialization;
using Groundwork.Documents.Store;
using OpenIddict.Abstractions;
using Elsa.Persistence.Groundwork.Stores;

namespace Elsa.Foundation.Identity.OpenIddict.Groundwork.Stores;

/// <summary>
/// Groundwork-backed <see cref="IOpenIddictAuthorizationStore{TAuthorization}"/>. Authorizations are global
/// (not tenant-scoped), like the application and scope stores, but unlike them the manifest declares no
/// compound lookup route for authorizations: the only two named routes are <c>FindAuthorizationBySubjectQuery</c>
/// (a point/cursor-paged "subject" route) and <c>FindAuthorizationByScopeQuery</c> (an offset-paged "scopes"
/// collection-membership route). The earlier four-field subject/application/status/type index was dropped
/// because it exceeded SQL Server's 1,700-byte key limit (see the manifest's remarks on
/// <c>AuthorizationSubjectV2Index</c>) and was never replaced with a narrower compound shape.
///
/// Because of that gap, <see cref="FindAsync"/> and <see cref="RevokeAsync"/> serve only the single-field
/// predicate shapes the two declared routes genuinely support (subject-only, or - for <see cref="FindAsync"/>
/// only - scopes-only) and reject every other argument combination up front, before any provider work, rather
/// than silently narrowing the predicate. <see cref="FindByApplicationIdAsync"/>,
/// <see cref="RevokeByApplicationIdAsync"/> and <see cref="PruneAsync"/> have no declared route at all and are
/// always rejected. The revoke-by-subject family that needs exactly-once semantics goes through
/// <see cref="OpenIddictGroundworkAtomicWrite"/> (spec 106 T030), one authorization at a time, fingerprinted
/// by authorization id so a retried call replays the original per-authorization outcome instead of re-saving.
/// </summary>
public sealed class GroundworkOpenIddictAuthorizationStore(
    IDocumentStore store,
    OpenIddictGroundworkAtomicWrite atomicWrite,
    IBoundedDocumentStore? boundedStore = null) : IOpenIddictAuthorizationStore<OpenIddictGroundworkAuthorization>
{
    // Groundwork admits no cursor paging on collection-membership routes (GW-QUERY-008), so the scope
    // lookup is one bounded page; this is the fail-closed ceiling on it. The subject route, by contrast, is
    // declared with genuine cursor paging, so subject lookups are exhaustive rather than a bounded page.
    private const int MaxScopeMaterialization = 10_000;

    private const string SubjectField = "subject";
    private const string ScopesField = "scopes";

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    /// <summary>
    /// Rejected: the manifest declares no bounded count-all route for authorizations. Its only named routes
    /// are the subject and scopes lookups.
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
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.CountAsync");

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<OpenIddictGroundworkAuthorization>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute<OpenIddictGroundworkAuthorization, TResult>(query, "authorization.CountAsync");
        throw new UnreachableException("RequireCountRoute always throws before returning.");
    }

    public async ValueTask CreateAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(authorization, expectedVersion: 0);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.create");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        authorization.PersistenceVersion = result.Document!.Version;
    }

    public async ValueTask DeleteAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var request = new DeleteDocumentRequest(
            OpenIddictGroundworkJson.AuthorizationDocumentKind,
            authorization.Id,
            authorization.PersistenceVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.DeleteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.delete");
        }

        if (result.Status != DocumentStoreWriteStatus.Deleted)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);
    }

    /// <summary>
    /// Serves only the two predicate shapes the declared routes genuinely support: subject-only (<paramref
    /// name="subject"/> set, everything else null/default) resolved through the subject route, or
    /// scopes-only (<paramref name="scopes"/> set to a non-empty array, everything else null/default)
    /// resolved through the scopes collection-membership route. <paramref name="client"/>, <paramref
    /// name="status"/>, and <paramref name="type"/> are never honoured - there is no declared route that
    /// carries them - so any call that supplies one of them, supplies both <paramref name="subject"/> and
    /// <paramref name="scopes"/> together, or supplies neither, is rejected before any provider work rather
    /// than silently evaluated against a narrower predicate than requested. See the class remarks for why no
    /// compound route exists.
    /// </summary>
    public IAsyncEnumerable<OpenIddictGroundworkAuthorization> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        ImmutableArray<string>? scopes,
        CancellationToken cancellationToken)
    {
        if (client is not null || status is not null || type is not null)
            throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.FindAsync");

        var hasSubject = !string.IsNullOrEmpty(subject);
        var hasScopes = scopes is { IsDefaultOrEmpty: false };

        if (hasSubject && !hasScopes)
            return FindBySubjectCoreAsync(subject!, cancellationToken);

        if (hasScopes && !hasSubject)
            return FindByScopesCoreAsync(scopes!.Value, cancellationToken);

        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.FindAsync");
    }

    /// <summary>
    /// Rejected: the manifest declares no application-relationship route for authorizations - only "subject"
    /// and "scopes" are indexed (see <see cref="OpenIddictGroundworkStorageManifest.CreateAuthorizationDefinition"/>).
    /// Declare a bounded "applicationId" route to admit this member.
    /// </summary>
    public IAsyncEnumerable<OpenIddictGroundworkAuthorization> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.FindByApplicationIdAsync");
    }

    public async ValueTask<OpenIddictGroundworkAuthorization?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        DocumentEnvelope? envelope;
        try
        {
            envelope = await store.LoadAsync(OpenIddictGroundworkJson.AuthorizationDocumentKind, identifier, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.findById");
        }

        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkAuthorization>(envelope);
    }

    public IAsyncEnumerable<OpenIddictGroundworkAuthorization> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        return FindBySubjectCoreAsync(subject, cancellationToken);
    }

    private async IAsyncEnumerable<OpenIddictGroundworkAuthorization> FindBySubjectCoreAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryBySubjectAsync(subject, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkAuthorization>(document);
    }

    /// <summary>
    /// The subject route is declared with genuine cursor paging (unlike the offset-only collection-membership
    /// routes), so this exhausts every page rather than reading one bounded page and failing closed.
    /// </summary>
    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedDocumentQueryPager.QueryAllAsync(
                BoundedStore,
                OpenIddictGroundworkJson.AuthorizationDocumentKind,
                OpenIddictGroundworkStorageManifest.FindAuthorizationBySubjectQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(SubjectField, subject))],
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.findBySubject");
        }
    }

    private async IAsyncEnumerable<OpenIddictGroundworkAuthorization> FindByScopesCoreAsync(
        ImmutableArray<string> scopes,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryByScopesAsync(scopes, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkAuthorization>(document);
    }

    /// <summary>
    /// One bounded page, failing closed above it rather than truncating silently - the same shape-gap as the
    /// application/scope stores' URI and resource lookups: Groundwork rejects cursor paging on a
    /// collection-membership route at plan compilation with <c>GW-QUERY-008</c>.
    /// </summary>
    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryByScopesAsync(ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        try
        {
            var result = await BoundedStore.QueryAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.AuthorizationDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindAuthorizationByScopeQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.CollectionContainsAll(ScopesField, scopes))],
                    take: MaxScopeMaterialization),
                cancellationToken);

            if (result.TotalCount > result.Documents.Count)
            {
                throw new InvalidOperationException(
                    $"Document query '{OpenIddictGroundworkStorageManifest.FindAuthorizationByScopeQuery}' matched " +
                    $"{result.TotalCount} authorizations, exceeding the bounded materialization limit of {MaxScopeMaterialization}.");
            }

            return result.Documents;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.findByScopes");
        }
    }

    public ValueTask<string?> GetApplicationIdAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.ApplicationId);
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkAuthorization, TState, TResult>(query, "authorization.GetAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.CreationDate);
    }

    public ValueTask<string?> GetIdAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult<string?>(authorization.Id);
    }

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.Properties.Count == 0
            ? ImmutableDictionary<string, JsonElement>.Empty
            : authorization.Properties.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public ValueTask<ImmutableArray<string>> GetScopesAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.Scopes.Length == 0
            ? ImmutableArray<string>.Empty
            : ImmutableArray.Create(authorization.Scopes));
    }

    public ValueTask<string?> GetStatusAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.Status);
    }

    public ValueTask<string?> GetSubjectAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.Subject);
    }

    public ValueTask<string?> GetTypeAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        return ValueTask.FromResult(authorization.Type);
    }

    public ValueTask<OpenIddictGroundworkAuthorization> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OpenIddictGroundworkAuthorization());

    /// <summary>
    /// Rejected for the same reason as <see cref="CountAsync(CancellationToken)"/>: no bounded list-all
    /// route is declared, and paging without a declared id index would have no guaranteed order.
    /// </summary>
    public async IAsyncEnumerable<OpenIddictGroundworkAuthorization> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryAllAuthorizationsAsync(count, offset, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkAuthorization>(document);
    }

    private static ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllAuthorizationsAsync(int? count, int? offset, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.ListAsync");

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkAuthorization>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkAuthorization, TState, TResult>(query, "authorization.ListAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    /// <summary>
    /// Rejected: the manifest declares no creation-date index/route for authorizations, so there is no
    /// bounded way to select prune candidates. Evaluating the threshold against every authorization document
    /// would require an in-memory scan, which the no-client-side-filtering contract forbids. Declare a
    /// bounded "creationDate" route to admit this member.
    /// </summary>
    public ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.PruneAsync");

    /// <summary>
    /// Same route gap as <see cref="FindAsync"/>: the manifest has no compound subject/client/status/type
    /// route, so this only serves the subject-only shape (<paramref name="client"/>, <paramref
    /// name="status"/>, and <paramref name="type"/> all null). Every matching authorization is resolved
    /// through the declared subject route and then revoked individually through
    /// <see cref="OpenIddictGroundworkAtomicWrite"/> (see <see cref="RevokeSingleAsync"/>). Every other
    /// combination, including a null/empty subject, is rejected before any provider work.
    /// </summary>
    public ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(subject) || client is not null || status is not null || type is not null)
            throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.RevokeAsync");

        return RevokeBySubjectCoreAsync(subject, cancellationToken);
    }

    /// <summary>
    /// Rejected: the manifest declares no application-relationship route for authorizations, the same gap as
    /// <see cref="FindByApplicationIdAsync"/>.
    /// </summary>
    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("authorization.RevokeByApplicationIdAsync");
    }

    public ValueTask<long> RevokeBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        return RevokeBySubjectCoreAsync(subject, cancellationToken);
    }

    private async ValueTask<long> RevokeBySubjectCoreAsync(string subject, CancellationToken cancellationToken)
    {
        var documents = await QueryBySubjectAsync(subject, cancellationToken);
        var revoked = 0L;
        foreach (var document in documents)
        {
            if (await RevokeSingleAsync(document.Id, cancellationToken))
                revoked++;
        }

        return revoked;
    }

    /// <summary>
    /// Revokes exactly one authorization through <see cref="OpenIddictGroundworkAtomicWrite"/>. The mutation
    /// fingerprint is the authorization id alone (mirroring the "redeem-token"/token-id fingerprint the
    /// wrapper's own tests use), so a retried or replayed call for the SAME authorization returns the
    /// original outcome instead of re-running the load/save - that is the exactly-once property T030 exists
    /// for. The authorization is re-loaded inside the unit of work rather than reused from the outer
    /// selection read, so a concurrent delete or edit is caught by CAS instead of blindly overwritten. The
    /// authorization is unconditionally (re-)marked revoked and counted, even if it was already revoked: the
    /// exact count this returns is "how many matching authorizations are revoked as of this call", not "how
    /// many actually changed state".
    /// </summary>
    private async ValueTask<bool> RevokeSingleAsync(string authorizationId, CancellationToken cancellationToken)
    {
        var mutation = OpenIddictGroundworkAtomicMutation.Create(
            "authorization.revoke",
            [authorizationId],
            OpenIddictGroundworkJson.AuthorizationDocumentKind);

        DocumentStoreWriteResult result;
        try
        {
            result = await atomicWrite.ExecuteAsync(
                mutation,
                async (unitOfWork, ct) =>
                {
                    var envelope = await unitOfWork.LoadAsync(OpenIddictGroundworkJson.AuthorizationDocumentKind, authorizationId, ct);
                    if (envelope is null)
                        return DocumentStoreWriteResult.NotFound;

                    var authorization = OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkAuthorization>(envelope);
                    authorization.Status = OpenIddictConstants.Statuses.Revoked;
                    authorization.ConcurrencyToken = Guid.NewGuid().ToString("N");
                    var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(authorization, envelope.Version);
                    return await unitOfWork.SaveAsync(request, ct);
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OpenIddictGroundworkUncertainCommitException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.revoke");
        }

        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public ValueTask SetApplicationIdAsync(OpenIddictGroundworkAuthorization authorization, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.ApplicationId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetCreationDateAsync(OpenIddictGroundworkAuthorization authorization, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.CreationDate = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(
        OpenIddictGroundworkAuthorization authorization,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        ArgumentNullException.ThrowIfNull(properties);
        authorization.Properties = new SortedDictionary<string, JsonElement>(properties, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetScopesAsync(OpenIddictGroundworkAuthorization authorization, ImmutableArray<string> scopes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.Scopes = scopes.IsDefaultOrEmpty ? [] : scopes.ToArray();
        return ValueTask.CompletedTask;
    }

    public ValueTask SetStatusAsync(OpenIddictGroundworkAuthorization authorization, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.Status = status;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSubjectAsync(OpenIddictGroundworkAuthorization authorization, string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.Subject = subject;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetTypeAsync(OpenIddictGroundworkAuthorization authorization, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);
        authorization.Type = type;
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(OpenIddictGroundworkAuthorization authorization, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(authorization);

        var expectedVersion = authorization.PersistenceVersion;
        authorization.ConcurrencyToken = Guid.NewGuid().ToString("N");

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(authorization, expectedVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "authorization.update");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        authorization.PersistenceVersion = result.Document!.Version;
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("OpenIddict authorization queries require an admitted bounded document-store runtime.");
}
