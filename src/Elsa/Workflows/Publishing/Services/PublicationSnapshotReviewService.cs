using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using Elsa.Workflows.Design.Core.Models;
using Elsa.Workflows.Design.Persistence.Core.Entities;
using Elsa.Workflows.Publishing.Core.Contracts;
using Elsa.Workflows.Publishing.Core.Models;

namespace Elsa.Workflows.Publishing.Services;

/// <summary>
/// Creates short-lived, single-use review tokens that bind an authored candidate snapshot to the policy and slot
/// authority observed by publication preflight. The token itself is opaque; no authored content is exposed in it.
/// </summary>
public sealed class PublicationSnapshotReviewService(TimeProvider timeProvider, IPublicationSnapshotReviewStore store)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(15);
    private const int CleanupBatchSize = 128;

    public string ComputeCandidateHash(
        WorkflowDefinitionState state,
        IEnumerable<DesignMetadataRecord> layout)
    {
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(layout);
        var snapshot = JsonSerializer.SerializeToElement(
            new PublicationCandidateSnapshot(
                state,
                layout.OrderBy(record => record.NodeId, StringComparer.Ordinal).ToArray()),
            SerializerOptions);
        var buffer = new ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
            WriteCanonical(writer, snapshot);
        return $"sha256:{Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan))}";
    }

    public async ValueTask<PublicationSnapshotReviewIssued> IssueAsync(
        string candidateHash,
        WorkflowPublicationPreflightPlan plan,
        PublicationAction? requestedAction = null,
        string? requestedSlotName = null,
        string? requestedExpectedPublicationId = null,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateHash);
        ArgumentNullException.ThrowIfNull(plan);
        var resolved = plan.ResolvedAction;
        var now = timeProvider.GetUtcNow();
        await store.DeleteExpiredAsync(now, CleanupBatchSize, cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
                .TrimEnd('=')
                .Replace('+', '-')
                .Replace('/', '_');
            var review = new PublicationSnapshotReview(
                token,
                candidateHash,
                resolved.WorkflowDefinitionId,
                resolved.Action,
                resolved.SlotName,
                resolved.PolicySource,
                resolved.PolicyRevision,
                requestedAction,
                requestedSlotName,
                requestedExpectedPublicationId,
                plan.Slot?.Revision ?? 0,
                plan.Slot?.ActiveActivationId,
                tenantId,
                now.Add(Lifetime));
            if (await store.TryAddAsync(review, cancellationToken))
                return new(token, candidateHash);
        }
        throw new InvalidOperationException("Unable to create a unique publication snapshot review token.");
    }

    public async ValueTask<PublicationSnapshotReview> GetAsync(
        string preflightToken,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(preflightToken);
        var review = await store.FindAsync(preflightToken, cancellationToken);
        if (review is null || review.ExpiresAt <= timeProvider.GetUtcNow() ||
            !StringComparer.Ordinal.Equals(review.TenantId, tenantId))
        {
            if (review?.ExpiresAt <= timeProvider.GetUtcNow())
                await store.TryConsumeAsync(preflightToken, cancellationToken);
            throw PublicationSnapshotReviewException.Stale();
        }

        return review;
    }

    public void ValidateRequestedIntent(
        PublicationSnapshotReview review,
        PublicationAction? action,
        string? slotName,
        string? expectedPublicationId)
    {
        ArgumentNullException.ThrowIfNull(review);
        if (action != review.RequestedAction ||
            !StringComparer.Ordinal.Equals(slotName, review.RequestedSlotName) ||
            !StringComparer.Ordinal.Equals(expectedPublicationId, review.RequestedExpectedPublicationId))
            throw PublicationSnapshotReviewException.Stale();
    }

    public async ValueTask ValidateAndConsumeAsync(
        string preflightToken,
        string candidateHash,
        WorkflowPublicationPreflightPlan currentPlan,
        string? tenantId = null,
        CancellationToken cancellationToken = default)
    {
        var review = await GetAsync(preflightToken, tenantId, cancellationToken);
        var resolved = currentPlan.ResolvedAction;
        var isCurrent = StringComparer.Ordinal.Equals(review.CandidateHash, candidateHash) &&
                        StringComparer.Ordinal.Equals(review.DefinitionId, resolved.WorkflowDefinitionId) &&
                        review.Action == resolved.Action &&
                        StringComparer.Ordinal.Equals(review.SlotName, resolved.SlotName) &&
                        review.PolicySource == resolved.PolicySource &&
                        review.PolicyRevision == resolved.PolicyRevision &&
                        review.SlotRevision == (currentPlan.Slot?.Revision ?? 0) &&
                        StringComparer.Ordinal.Equals(review.ActivePublicationId, currentPlan.Slot?.ActiveActivationId);
        if (!isCurrent || !await store.TryConsumeAsync(preflightToken, cancellationToken))
            throw PublicationSnapshotReviewException.Stale();
    }

    private static void WriteCanonical(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in value.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray())
                    WriteCanonical(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.String:
                writer.WriteStringValue(value.GetString());
                break;
            case JsonValueKind.Number:
                writer.WriteRawValue(value.GetRawText(), skipInputValidation: true);
                break;
            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;
            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;
            case JsonValueKind.Null:
            case JsonValueKind.Undefined:
                writer.WriteNullValue();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(value), value.ValueKind, "Unsupported JSON value kind.");
        }
    }

    private sealed record PublicationCandidateSnapshot(
        WorkflowDefinitionState State,
        IReadOnlyCollection<DesignMetadataRecord> Layout);
}

public sealed record PublicationSnapshotReviewIssued(string PreflightToken, string CandidateHash);

public sealed class PublicationSnapshotReviewException(string code, string message) : InvalidOperationException(message)
{
    public string Code { get; } = code;

    public static PublicationSnapshotReviewException Stale() =>
        new("publication_snapshot_stale", "The publication snapshot review is stale or does not match this publish request.");
}
