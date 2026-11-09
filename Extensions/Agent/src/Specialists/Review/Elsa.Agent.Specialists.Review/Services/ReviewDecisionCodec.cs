using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Specialists.Review.Models;
using Elsa.Secrets.Core.Contracts;

namespace Elsa.Agent.Specialists.Review.Services;

/// <summary>Everything a decision is bound to. The actor is always derived server-side.</summary>
internal sealed record ReviewDecisionClaims(
    string Purpose,
    string Codec,
    string TenantId,
    string Partition,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    string WaitId,
    string BookmarkId,
    string ContentHash,
    string ActorId,
    ReviewDecision Decision,
    string? Comment,
    string RequestId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    /// <summary>
    /// True when every wait-identity field equals the pinned review state. The bookmark is compared when the
    /// caller knows it: the pre-claim guard does, while the activity's resume context exposes only the delivery.
    /// </summary>
    public bool Targets(HumanReviewState state, string? bookmarkId) =>
        TenantId == state.TenantId && Partition == state.Partition && WorkflowExecutionId == state.WorkflowExecutionId &&
        ActivityExecutionId == state.ActivityExecutionId && ExecutableNodeId == state.ExecutableNodeId &&
        WaitId == state.WaitId && ContentHash == state.ContentHash && (bookmarkId is null || BookmarkId == bookmarkId);

    /// <summary>The decision content that one request ID may carry; replays must repeat it exactly.</summary>
    public bool SameContent(ReviewDecisionClaims other) =>
        RequestId == other.RequestId && ActorId == other.ActorId && Decision == other.Decision && Comment == other.Comment &&
        WaitId == other.WaitId && ContentHash == other.ContentHash;
}

/// <summary>
/// Purpose-bound authenticated envelope over the existing <see cref="ISecretValueProtector"/>. The protector
/// itself carries no purpose, so the purpose/codec discriminator lives inside the authenticated plaintext and
/// is checked after decryption: a protected secret produced for any other purpose is refused. The codec keeps
/// reading every format it ever wrote; keys follow the Secrets key-ring retention.
/// </summary>
internal sealed class ReviewDecisionCodec(ISecretValueProtector protector)
{
    internal const string Purpose = "elsa.agent.human-review-decision/1";
    internal const string Codec = "human-review-decision/1";
    private const int MaximumEnvelopeCharacters = 32_768;

    internal string Protect(ReviewDecisionClaims claims)
    {
        if (claims.Purpose != Purpose || claims.Codec != Codec) throw HumanReviewException.Invalid("The decision envelope purpose is not a human review decision.");
        return protector.Protect(JsonSerializer.Serialize(claims, HumanReviewJson.Web));
    }

    internal ReviewDecisionClaims Read(string? envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope) || envelope.Length > MaximumEnvelopeCharacters)
            throw HumanReviewException.Invalid("The decision envelope is missing or oversized.");
        string plaintext;
        try { plaintext = protector.Unprotect(envelope); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or InvalidOperationException or ArgumentException)
        {
            throw HumanReviewException.Invalid("The decision envelope is not authentic.");
        }
        ReviewDecisionClaims? claims;
        try { claims = JsonSerializer.Deserialize<ReviewDecisionClaims>(plaintext, HumanReviewJson.Web); }
        catch (JsonException) { throw HumanReviewException.Invalid("The protected value is not a human review decision."); }
        if (claims is null || claims.Purpose != Purpose || claims.Codec != Codec ||
            new[] { claims.TenantId, claims.Partition, claims.WorkflowExecutionId, claims.ActivityExecutionId, claims.ExecutableNodeId,
                claims.WaitId, claims.BookmarkId, claims.ContentHash, claims.ActorId, claims.RequestId }.Any(string.IsNullOrWhiteSpace) ||
            !Enum.IsDefined(claims.Decision) ||
            claims.Comment?.Length > HumanReviewIds.MaximumTextCharacters || claims.ExpiresAt <= claims.IssuedAt)
            throw HumanReviewException.Invalid("The protected value is not a human review decision.");
        return claims;
    }

    // One canonical encoding (compact, default encoder) bounds, hashes and identifies content, so neither whitespace nor
    // the escaping of any upstream writer or persistence round trip changes its size or identity.
    // It does not normalize key order or number spelling, so stores must keep private state and results as written.
    internal static byte[] CanonicalBytes(JsonElement content) => JsonSerializer.SerializeToUtf8Bytes(content);

    internal static string HashContent(JsonElement content) => Convert.ToHexStringLower(SHA256.HashData(CanonicalBytes(content)));

    internal static string WaitId(string workflowExecutionId, string activityExecutionId, string contentHash) =>
        "agent-review:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{workflowExecutionId}\n{activityExecutionId}\n{contentHash}")))[..40];
}

/// <summary>A safe, typed refusal. <see cref="IsUnavailable"/> marks a temporary dependency that leaves work owed.</summary>
public sealed class HumanReviewException : Exception
{
    private HumanReviewException(string code, string message, bool unavailable, Exception? inner = null) : base(message, inner)
    {
        Code = code;
        IsUnavailable = unavailable;
    }

    public string Code { get; }

    public bool IsUnavailable { get; }

    internal static HumanReviewException Invalid(string message) => new("agent.review.invalid", message, false);

    internal static HumanReviewException Refused(string code, string message) => new(code, message, false);

    internal static HumanReviewException Unavailable(string message, Exception? inner = null) => new("agent.review.unavailable", message, true, inner);
}
