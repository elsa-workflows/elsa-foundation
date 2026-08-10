using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Groundwork.Documents.Store;

namespace Elsa.Persistence.Groundwork.Querying;

/// <summary>
/// Durable storage for the design lane's post-commit intents: the write that rides inside a design commit, and
/// the claim, release and delete a redrive needs to finish one.
/// <para>
/// The intent document deliberately carries <c>claimableAt</c>, <c>recordedAt</c> and <c>intentId</c> as bare
/// top-level properties. Design entities are stored nested under <c>content.entity.*</c> by
/// <see cref="GroundworkDocumentWriter"/>, and the claim index declared in
/// <see cref="GroundworkDesignAtomicWriteStorageManifest"/> names those three paths at the top level — so
/// writing an intent through the entity envelope would project the fields one level too deep and the index
/// would match nothing at all. That failure is silent: every write and every unindexed read still works.
/// </para>
/// </summary>
public sealed class GroundworkDesignPostCommitOutbox(IDocumentStore store, IBoundedDocumentStore boundedStore)
{
    /// <summary>The largest page one sweep may claim, which is also the bound on a sweep's blast radius.</summary>
    public const int MaximumClaimLimit = 64;

    private const string DocumentKind = GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentDocumentKind;
    private const string ProjectionDigestPrefix = "design-intent:projection:sha256:v1:";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// The physical document id, which is also the projected <c>intentId</c>. The projection column is bounded
    /// at <see cref="GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdProjectionLength"/>
    /// because SQL Server refuses an unbounded string as an index key column, so a longer logical id is carried
    /// through a digest and the durable logical value is kept in the document.
    /// </summary>
    public static string ProjectIntentId(string intentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        return intentId.Length <= GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdProjectionLength
            ? intentId
            : $"{ProjectionDigestPrefix}{Digest(intentId)}";
    }

    /// <summary>
    /// The create-only write that makes <paramref name="intent"/> durable. Staged with the design mutation it
    /// belongs to, so the intent exists exactly when the mutation becomes done — never before, never after.
    /// </summary>
    public static SaveDocumentRequest CreateSaveRequest(DesignPostCommitIntent intent, DateTimeOffset recordedAt)
    {
        ArgumentNullException.ThrowIfNull(intent);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.IntentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(intent.Kind);
        ArgumentNullException.ThrowIfNull(intent.Document);

        var projected = ProjectIntentId(intent.IntentId);
        var envelope = new IntentEnvelope(
            projected,
            recordedAt,
            // Claimable from the moment it lands. The commit's own caller normally delivers it first; the
            // redrive is what happens when that caller never comes back.
            recordedAt,
            intent.Kind,
            intent.Document,
            LogicalIntentId: StringComparer.Ordinal.Equals(projected, intent.IntentId) ? null : intent.IntentId);
        return Save(envelope, expectedVersion: 0);
    }

    /// <summary>
    /// Leases the due intents for <paramref name="request"/>'s owner. The bounded route establishes the
    /// candidate page inside the store session's tenant scope; each candidate is then re-read and written back
    /// at its exact optimistic version, so two sweeps cannot both come away owning one intent.
    /// </summary>
    public async Task<IReadOnlyList<DesignPostCommitIntentClaim>> ClaimAsync(
        DesignPostCommitClaimRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        cancellationToken.ThrowIfCancellationRequested();

        var page = await boundedStore.QueryAsync(
            new DocumentQuery(
                DocumentKind,
                GroundworkDesignAtomicWriteStorageManifest.ListClaimableDesignPostCommitIntentsQuery,
                [Due(request.Now)],
                ClaimOrder,
                take: request.Limit),
            cancellationToken);

        var claims = new List<DesignPostCommitIntentClaim>();
        foreach (var candidate in page.Documents)
        {
            var loaded = await LoadAsync(candidate.Id, cancellationToken);
            // Gone, or leased by a sweep that started between the page read and now.
            if (loaded is null || loaded.Envelope.ClaimableAt > request.Now)
                continue;

            var leased = loaded.Envelope with
            {
                ClaimableAt = request.Now.Add(request.VisibilityTimeout),
                OwnerId = request.OwnerId,
                FencingToken = checked(loaded.Envelope.FencingToken + 1)
            };
            var result = await store.SaveAsync(Save(leased, loaded.Version), cancellationToken);
            if (result.Status is DocumentStoreWriteStatus.ConcurrencyConflict)
                continue;
            if (result.Status is not DocumentStoreWriteStatus.Saved)
            {
                throw new InvalidOperationException(
                    $"Groundwork rejected the claim on design post-commit intent '{candidate.Id}' with status '{result.Status}'.");
            }

            claims.Add(new(
                new(leased.LogicalIntentId ?? leased.IntentId, leased.Kind, leased.Document),
                candidate.Id,
                request.OwnerId,
                leased.FencingToken,
                leased.RecordedAt,
                leased.AttemptCount,
                VersionOf(result, candidate.Id)));
        }

        return claims;
    }

    /// <summary>
    /// Deletes a delivered intent, fenced by the version the claim wrote. A lost fence means the lease expired
    /// and another owner took the intent over; that owner's delivery is the one that decides its fate, so this
    /// caller must not delete underneath it. An absent intent is success: someone already finished this work.
    /// </summary>
    public async Task<bool> TryCompleteAsync(
        DesignPostCommitIntentClaim claim,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        var result = await store.DeleteAsync(
            new DeleteDocumentRequest(DocumentKind, claim.DocumentId, claim.Version),
            cancellationToken);
        return result.Status is DocumentStoreWriteStatus.Deleted or DocumentStoreWriteStatus.NotFound;
    }

    /// <summary>
    /// Returns an undelivered intent to the queue at <paramref name="claimableAt"/>, recording why. A lost
    /// fence is not an error here either: the intent is already someone else's problem.
    /// </summary>
    public async Task<bool> TryReleaseAsync(
        DesignPostCommitIntentClaim claim,
        DateTimeOffset claimableAt,
        string failureMessage,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(claim);
        // Rebuilt from the claim rather than reloaded: the claim already carries every stored field, and this
        // owner's write is fenced by the version it wrote, so a reload could only tell us what the fence
        // already enforces.
        var released = new IntentEnvelope(
            claim.DocumentId,
            claim.RecordedAt,
            claimableAt,
            claim.Intent.Kind,
            claim.Intent.Document,
            AttemptCount: claim.AttemptCount == int.MaxValue ? int.MaxValue : claim.AttemptCount + 1,
            OwnerId: null,
            FencingToken: claim.FencingToken,
            LastFailureMessage: failureMessage,
            LogicalIntentId: StringComparer.Ordinal.Equals(claim.DocumentId, claim.Intent.IntentId)
                ? null
                : claim.Intent.IntentId);
        var result = await store.SaveAsync(Save(released, claim.Version), cancellationToken);
        return result.Status is DocumentStoreWriteStatus.Saved;
    }

    /// <summary>
    /// Drops an intent the committing caller delivered itself, without a claim. Unconditional on purpose: this
    /// runs on the ordinary path where no redrive is involved, and racing one is harmless — delivery is
    /// idempotent, so the redrive's own fenced delete simply finds the intent already gone.
    /// </summary>
    public async Task DiscardAsync(string intentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        await store.DeleteAsync(new DeleteDocumentRequest(DocumentKind, ProjectIntentId(intentId)), cancellationToken);
    }

    /// <summary>Reads one intent, for tests and diagnostics.</summary>
    public async Task<DesignPostCommitIntent?> FindAsync(string intentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(intentId);
        var loaded = await LoadAsync(ProjectIntentId(intentId), cancellationToken);
        return loaded is null
            ? null
            : new(loaded.Envelope.LogicalIntentId ?? loaded.Envelope.IntentId, loaded.Envelope.Kind, loaded.Envelope.Document);
    }

    private static readonly DocumentQueryOrder[] ClaimOrder =
    [
        new(GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentClaimableAtField),
        new(GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentRecordedAtField),
        new(GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentIdField)
    ];

    private static DocumentQueryClause Due(DateTimeOffset now) =>
        DocumentQueryClause.Of(DocumentQueryComparison.LessThanOrEqual(
            GroundworkDesignAtomicWriteStorageManifest.DesignPostCommitIntentClaimableAtField,
            now.ToString("O", CultureInfo.InvariantCulture)));

    private async Task<LoadedIntent?> LoadAsync(string documentId, CancellationToken cancellationToken)
    {
        var envelope = await store.LoadAsync(DocumentKind, documentId, cancellationToken);
        if (envelope is null)
            return null;

        var intent = JsonSerializer.Deserialize<IntentEnvelope>(envelope.ContentJson, Json)
                     ?? throw new InvalidOperationException($"Design post-commit intent '{documentId}' is unreadable.");
        return new(intent, envelope.Version);
    }

    private static SaveDocumentRequest Save(IntentEnvelope envelope, long expectedVersion) =>
        new(
            DocumentKind,
            envelope.IntentId,
            GroundworkDesignAtomicWriteStorageManifest.SchemaVersion,
            JsonSerializer.Serialize(envelope, Json),
            expectedVersion);

    private static long VersionOf(DocumentStoreWriteResult result, string documentId) =>
        result.Document?.Version
        ?? throw new InvalidOperationException(
            $"Groundwork acknowledged the claim on design post-commit intent '{documentId}' without returning its version.");

    private static string Digest(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        var framed = new byte[sizeof(int) + bytes.Length];
        BinaryPrimitives.WriteInt32BigEndian(framed, bytes.Length);
        bytes.CopyTo(framed, sizeof(int));
        return Convert.ToHexStringLower(SHA256.HashData(framed));
    }

    private sealed record LoadedIntent(IntentEnvelope Envelope, long Version);

    /// <summary>
    /// The stored shape. Every claim-index field is a top-level property here; see the type remarks for why
    /// that is load-bearing rather than stylistic.
    /// </summary>
    private sealed record IntentEnvelope(
        string IntentId,
        DateTimeOffset RecordedAt,
        DateTimeOffset ClaimableAt,
        string Kind,
        DesignPostCommitDocumentWrite Document,
        int AttemptCount = 0,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? OwnerId = null,
        long FencingToken = 0,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LastFailureMessage = null,
        [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        string? LogicalIntentId = null);
}

/// <summary>One sweep's request for work.</summary>
public sealed class DesignPostCommitClaimRequest
{
    public DesignPostCommitClaimRequest(string ownerId, DateTimeOffset now, TimeSpan visibilityTimeout, int limit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerId);
        if (visibilityTimeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(visibilityTimeout), "A claim visibility timeout must be positive.");
        if (limit is <= 0 or > GroundworkDesignPostCommitOutbox.MaximumClaimLimit)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                $"A claim limit must be between 1 and {GroundworkDesignPostCommitOutbox.MaximumClaimLimit}.");
        }

        OwnerId = ownerId;
        Now = now;
        VisibilityTimeout = visibilityTimeout;
        Limit = limit;
    }

    public string OwnerId { get; }
    public DateTimeOffset Now { get; }
    public TimeSpan VisibilityTimeout { get; }
    public int Limit { get; }
}

/// <summary>One leased intent, and the fence that keeps its completion honest.</summary>
public sealed record DesignPostCommitIntentClaim(
    DesignPostCommitIntent Intent,
    string DocumentId,
    string OwnerId,
    long FencingToken,
    DateTimeOffset RecordedAt,
    int AttemptCount,
    long Version);
