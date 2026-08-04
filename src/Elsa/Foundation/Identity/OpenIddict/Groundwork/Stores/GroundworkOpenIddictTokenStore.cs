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
/// Groundwork-backed <see cref="IOpenIddictTokenStore{TToken}"/>. Tokens are global (not tenant-scoped),
/// like the other three OpenIddict stores. The manifest declares exactly two named routes for tokens:
/// <c>FindTokenByReferenceIdQuery</c> (a unique point lookup on <c>referenceId</c>) and
/// <c>FindTokenBySubjectQuery</c> (a cursor-paged "subject" route, served through
/// <c>TokenSubjectV2Index</c>). The manifest also declares <c>TokenSubjectIndex</c>
/// (<c>subject</c>, <c>status</c>, <c>expirationDate</c>) as a physical compound index, but it backs no
/// bounded query route of its own: <c>expirationDate</c> is only the third column of a subject-led
/// compound, not a standalone date route, and <c>status</c>/<c>type</c>/<c>client</c> are never
/// independently queryable through it. Neither <c>applicationId</c> nor <c>authorizationId</c> has any
/// declared route at all, even though both are physical columns on the token table.
///
/// Because of that gap, <see cref="FindAsync"/> and <see cref="RevokeAsync"/> serve only the single-field
/// predicate shape the declared subject route genuinely supports (subject-only, everything else
/// null/default) and reject every other argument combination up front, before any provider work, rather
/// than silently narrowing the predicate. <see cref="FindByApplicationIdAsync"/>,
/// <see cref="RevokeByApplicationIdAsync"/>, <see cref="FindByAuthorizationIdAsync"/>,
/// <see cref="RevokeByAuthorizationIdAsync"/>, and <see cref="PruneAsync"/> have no declared route at all
/// and are always rejected. The revoke-by-subject family that needs exactly-once semantics goes through
/// <see cref="OpenIddictGroundworkAtomicWrite"/> (spec 106 T030), one token at a time, fingerprinted by
/// token id so a retried call replays the original per-token outcome instead of re-saving.
/// </summary>
public sealed class GroundworkOpenIddictTokenStore(
    IDocumentStore store,
    OpenIddictGroundworkAtomicWrite atomicWrite,
    IBoundedDocumentStore? boundedStore = null) : IOpenIddictTokenStore<OpenIddictGroundworkToken>
{
    private const string SubjectField = "subject";
    private const string ReferenceIdField = "referenceId";

    private readonly IBoundedDocumentStore? _boundedStore = boundedStore ?? store as IBoundedDocumentStore;

    /// <summary>
    /// Rejected: the manifest declares no bounded count-all route for tokens. Its only named routes are
    /// the reference-id and subject lookups.
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
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.CountAsync");

    public ValueTask<long> CountAsync<TResult>(
        Func<IQueryable<OpenIddictGroundworkToken>, IQueryable<TResult>> query,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireCountRoute<OpenIddictGroundworkToken, TResult>(query, "token.CountAsync");
        throw new UnreachableException("RequireCountRoute always throws before returning.");
    }

    public async ValueTask CreateAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(token, expectedVersion: 0);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.create");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        token.PersistenceVersion = result.Document!.Version;
    }

    public async ValueTask DeleteAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var request = new DeleteDocumentRequest(
            OpenIddictGroundworkJson.TokenDocumentKind,
            token.Id,
            token.PersistenceVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.DeleteAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.delete");
        }

        if (result.Status != DocumentStoreWriteStatus.Deleted)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);
    }

    /// <summary>
    /// Serves only the one predicate shape the declared route genuinely supports: subject-only (<paramref
    /// name="subject"/> set, everything else null) resolved through the subject route. <paramref
    /// name="client"/>, <paramref name="status"/>, and <paramref name="type"/> are never honoured - there
    /// is no declared route that carries them - so any call that supplies one of them, or supplies no
    /// subject at all, is rejected before any provider work rather than silently evaluated against a
    /// narrower predicate than requested. See the class remarks for why no compound route exists.
    /// </summary>
    public IAsyncEnumerable<OpenIddictGroundworkToken> FindAsync(
        string? subject,
        string? client,
        string? status,
        string? type,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(subject) || client is not null || status is not null || type is not null)
            throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.FindAsync");

        return FindBySubjectCoreAsync(subject, cancellationToken);
    }

    /// <summary>
    /// Rejected: the manifest declares no application-relationship route for tokens - only "referenceId"
    /// and "subject" are indexed (see <see cref="OpenIddictGroundworkStorageManifest.CreateTokenDefinition"/>).
    /// Declare a bounded "applicationId" route to admit this member.
    /// </summary>
    public IAsyncEnumerable<OpenIddictGroundworkToken> FindByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.FindByApplicationIdAsync");
    }

    /// <summary>
    /// Rejected: the manifest declares no authorization-relationship route for tokens either, the same gap
    /// as <see cref="FindByApplicationIdAsync"/>. Declare a bounded "authorizationId" route to admit this
    /// member.
    /// </summary>
    public IAsyncEnumerable<OpenIddictGroundworkToken> FindByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.FindByAuthorizationIdAsync");
    }

    public async ValueTask<OpenIddictGroundworkToken?> FindByIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        DocumentEnvelope? envelope;
        try
        {
            envelope = await store.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, identifier, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.findById");
        }

        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(envelope);
    }

    /// <summary>
    /// Despite the interface's plural documentation ("Retrieves the list of tokens..."), the return type is
    /// a single nullable token: <c>referenceId</c> is declared as a unique point route
    /// (<c>FindTokenByReferenceIdQuery</c> over <c>TokenReferenceIndex</c>), so at most one token can ever
    /// match.
    /// </summary>
    public async ValueTask<OpenIddictGroundworkToken?> FindByReferenceIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);

        var envelope = await FindByReferenceIdEnvelopeAsync(identifier, cancellationToken);
        return envelope is null ? null : OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(envelope);
    }

    private async ValueTask<DocumentEnvelope?> FindByReferenceIdEnvelopeAsync(string identifier, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedStore.FirstOrDefaultAsync(
                new DocumentQuery(
                    OpenIddictGroundworkJson.TokenDocumentKind,
                    OpenIddictGroundworkStorageManifest.FindTokenByReferenceIdQuery,
                    [DocumentQueryClause.Of(DocumentQueryComparison.Equal(ReferenceIdField, identifier))]),
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.findByReferenceId");
        }
    }

    public IAsyncEnumerable<OpenIddictGroundworkToken> FindBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(subject);
        return FindBySubjectCoreAsync(subject, cancellationToken);
    }

    private async IAsyncEnumerable<OpenIddictGroundworkToken> FindBySubjectCoreAsync(
        string subject,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryBySubjectAsync(subject, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(document);
    }

    /// <summary>
    /// The subject route is declared with genuine cursor paging, so this exhausts every page rather than
    /// reading one bounded page and failing closed.
    /// </summary>
    private async ValueTask<IReadOnlyList<DocumentEnvelope>> QueryBySubjectAsync(string subject, CancellationToken cancellationToken)
    {
        try
        {
            return await BoundedDocumentQueryPager.QueryAllAsync(
                BoundedStore,
                OpenIddictGroundworkJson.TokenDocumentKind,
                OpenIddictGroundworkStorageManifest.FindTokenBySubjectQuery,
                [DocumentQueryClause.Of(DocumentQueryComparison.Equal(SubjectField, subject))],
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.findBySubject");
        }
    }

    public ValueTask<string?> GetApplicationIdAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.ApplicationId);
    }

    public ValueTask<TResult?> GetAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkToken, TState, TResult>(query, "token.GetAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    public ValueTask<string?> GetAuthorizationIdAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.AuthorizationId);
    }

    public ValueTask<DateTimeOffset?> GetCreationDateAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.CreationDate);
    }

    public ValueTask<DateTimeOffset?> GetExpirationDateAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.ExpirationDate);
    }

    public ValueTask<string?> GetIdAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult<string?>(token.Id);
    }

    public ValueTask<string?> GetPayloadAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.Payload);
    }

    public ValueTask<ImmutableDictionary<string, JsonElement>> GetPropertiesAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.Properties.Count == 0
            ? ImmutableDictionary<string, JsonElement>.Empty
            : token.Properties.ToImmutableDictionary(StringComparer.Ordinal));
    }

    public ValueTask<DateTimeOffset?> GetRedemptionDateAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.RedemptionDate);
    }

    public ValueTask<string?> GetReferenceIdAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.ReferenceId);
    }

    public ValueTask<string?> GetStatusAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.Status);
    }

    public ValueTask<string?> GetSubjectAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.Subject);
    }

    public ValueTask<string?> GetTypeAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        return ValueTask.FromResult(token.Type);
    }

    public ValueTask<OpenIddictGroundworkToken> InstantiateAsync(CancellationToken cancellationToken) =>
        ValueTask.FromResult(new OpenIddictGroundworkToken());

    /// <summary>
    /// Rejected for the same reason as <see cref="CountAsync(CancellationToken)"/>: no bounded list-all
    /// route is declared, and paging without a declared id index would have no guaranteed order.
    /// </summary>
    public async IAsyncEnumerable<OpenIddictGroundworkToken> ListAsync(
        int? count,
        int? offset,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var documents = await QueryAllTokensAsync(count, offset, cancellationToken);
        foreach (var document in documents)
            yield return OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(document);
    }

    private static ValueTask<IReadOnlyList<DocumentEnvelope>> QueryAllTokensAsync(int? count, int? offset, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.ListAsync");

    public IAsyncEnumerable<TResult> ListAsync<TState, TResult>(
        Func<IQueryable<OpenIddictGroundworkToken>, TState, IQueryable<TResult>> query,
        TState state,
        CancellationToken cancellationToken)
    {
        OpenIddictGroundworkGenericQueryTranslator.RequireStatefulRoute<OpenIddictGroundworkToken, TState, TResult>(query, "token.ListAsync");
        throw new UnreachableException("RequireStatefulRoute always throws before returning.");
    }

    /// <summary>
    /// Rejected: the manifest declares no standalone creation-date or expiration-date route for tokens, so
    /// there is no bounded way to select prune candidates. <c>TokenSubjectIndex</c> carries
    /// <c>expirationDate</c>, but only as the third column of a subject-led compound index, which does not
    /// make it a standalone date route - a prune scan has no subject to lead with. Evaluating the threshold
    /// against every token document would require an in-memory scan, which the no-client-side-filtering
    /// contract forbids. Declare a bounded "creationDate" or "expirationDate" route to admit this member.
    /// </summary>
    public ValueTask<long> PruneAsync(DateTimeOffset threshold, CancellationToken cancellationToken) =>
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.PruneAsync");

    /// <summary>
    /// Same route gap as <see cref="FindAsync"/>: the manifest has no compound subject/client/status/type
    /// route, so this only serves the subject-only shape (<paramref name="client"/>, <paramref
    /// name="status"/>, and <paramref name="type"/> all null). Every matching token is resolved through the
    /// declared subject route and then revoked individually through
    /// <see cref="OpenIddictGroundworkAtomicWrite"/> (see <see cref="RevokeSingleAsync"/>). Every other
    /// combination, including a null/empty subject, is rejected before any provider work.
    /// </summary>
    public ValueTask<long> RevokeAsync(string? subject, string? client, string? status, string? type, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(subject) || client is not null || status is not null || type is not null)
            throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.RevokeAsync");

        return RevokeBySubjectCoreAsync(subject, cancellationToken);
    }

    /// <summary>
    /// Rejected: the manifest declares no application-relationship route for tokens, the same gap as
    /// <see cref="FindByApplicationIdAsync"/>.
    /// </summary>
    public ValueTask<long> RevokeByApplicationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.RevokeByApplicationIdAsync");
    }

    /// <summary>
    /// Rejected: the manifest declares no authorization-relationship route for tokens, the same gap as
    /// <see cref="FindByAuthorizationIdAsync"/>.
    /// </summary>
    public ValueTask<long> RevokeByAuthorizationIdAsync(string identifier, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(identifier);
        throw OpenIddictGroundworkFailureMapper.UnsupportedGenericQuery("token.RevokeByAuthorizationIdAsync");
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
    /// Revokes exactly one token through <see cref="OpenIddictGroundworkAtomicWrite"/>. The mutation
    /// fingerprint is the token id alone (mirroring the authorization store's per-authorization
    /// fingerprint), so a retried or replayed call for the SAME token returns the original outcome instead
    /// of re-running the load/save - that is the exactly-once property T030 exists for. The token is
    /// re-loaded inside the unit of work rather than reused from the outer selection read, so a concurrent
    /// delete or edit is caught by CAS instead of blindly overwritten. The token is unconditionally
    /// (re-)marked revoked and counted, even if it was already revoked: the exact count this returns is
    /// "how many matching tokens are revoked as of this call", not "how many actually changed state".
    /// </summary>
    private async ValueTask<bool> RevokeSingleAsync(string tokenId, CancellationToken cancellationToken)
    {
        var mutation = OpenIddictGroundworkAtomicMutation.Create(
            "token.revoke",
            [tokenId],
            OpenIddictGroundworkJson.TokenDocumentKind);

        DocumentStoreWriteResult result;
        try
        {
            result = await atomicWrite.ExecuteAsync(
                mutation,
                async (unitOfWork, ct) =>
                {
                    var envelope = await unitOfWork.LoadAsync(OpenIddictGroundworkJson.TokenDocumentKind, tokenId, ct);
                    if (envelope is null)
                        return DocumentStoreWriteResult.NotFound;

                    var token = OpenIddictGroundworkRecordSerializer.Deserialize<OpenIddictGroundworkToken>(envelope);
                    token.Status = OpenIddictConstants.Statuses.Revoked;
                    token.ConcurrencyToken = Guid.NewGuid().ToString("N");
                    var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(token, envelope.Version);
                    return await unitOfWork.SaveAsync(request, ct);
                },
                cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not OpenIddictGroundworkUncertainCommitException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.revoke");
        }

        return result.Status == DocumentStoreWriteStatus.Saved;
    }

    public ValueTask SetApplicationIdAsync(OpenIddictGroundworkToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.ApplicationId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetAuthorizationIdAsync(OpenIddictGroundworkToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.AuthorizationId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetCreationDateAsync(OpenIddictGroundworkToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.CreationDate = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetExpirationDateAsync(OpenIddictGroundworkToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.ExpirationDate = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPayloadAsync(OpenIddictGroundworkToken token, string? payload, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Payload = payload;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetPropertiesAsync(
        OpenIddictGroundworkToken token,
        ImmutableDictionary<string, JsonElement> properties,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        ArgumentNullException.ThrowIfNull(properties);
        token.Properties = new SortedDictionary<string, JsonElement>(properties, StringComparer.Ordinal);
        return ValueTask.CompletedTask;
    }

    public ValueTask SetRedemptionDateAsync(OpenIddictGroundworkToken token, DateTimeOffset? date, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.RedemptionDate = date;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetReferenceIdAsync(OpenIddictGroundworkToken token, string? identifier, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.ReferenceId = identifier;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetStatusAsync(OpenIddictGroundworkToken token, string? status, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Status = status;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetSubjectAsync(OpenIddictGroundworkToken token, string? subject, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Subject = subject;
        return ValueTask.CompletedTask;
    }

    public ValueTask SetTypeAsync(OpenIddictGroundworkToken token, string? type, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);
        token.Type = type;
        return ValueTask.CompletedTask;
    }

    public async ValueTask UpdateAsync(OpenIddictGroundworkToken token, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(token);

        var expectedVersion = token.PersistenceVersion;
        token.ConcurrencyToken = Guid.NewGuid().ToString("N");

        var request = OpenIddictGroundworkRecordSerializer.CreateSaveRequest(token, expectedVersion);
        DocumentStoreWriteResult result;
        try
        {
            result = await store.SaveAsync(request, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw OpenIddictGroundworkFailureMapper.Translate(exception, "token.update");
        }

        if (result.Status != DocumentStoreWriteStatus.Saved)
            throw OpenIddictGroundworkFailureMapper.WriteFailure(result.Status);

        token.PersistenceVersion = result.Document!.Version;
    }

    private IBoundedDocumentStore BoundedStore => _boundedStore
        ?? throw new InvalidOperationException("OpenIddict token queries require an admitted bounded document-store runtime.");
}
