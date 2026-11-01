using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Elsa.Agent.Specialists.Editorial.Models;
using Elsa.Secrets.Core.Contracts;

namespace Elsa.Agent.Specialists.Editorial.Services;

/// <summary>Everything a decision is bound to. The actor is always derived server-side.</summary>
internal sealed record EditorialDecisionClaims(
    string Purpose,
    string Codec,
    string TenantId,
    string Partition,
    string WorkflowExecutionId,
    string ActivityExecutionId,
    string ExecutableNodeId,
    string WaitId,
    string BookmarkId,
    int Round,
    string ArticleHash,
    string ActorId,
    EditorialDecision Decision,
    string? Comment,
    string RequestId,
    DateTimeOffset IssuedAt,
    DateTimeOffset ExpiresAt)
{
    /// <summary>True when every wait-identity field equals the pinned review state.</summary>
    public bool Targets(EditorialReviewState state, string bookmarkId) =>
        TenantId == state.TenantId && Partition == state.Partition && WorkflowExecutionId == state.WorkflowExecutionId &&
        ActivityExecutionId == state.ActivityExecutionId && ExecutableNodeId == state.ExecutableNodeId &&
        WaitId == state.WaitId && Round == state.Round && ArticleHash == state.ArticleHash && BookmarkId == bookmarkId;

    /// <summary>The decision content that one request ID may carry; replays must repeat it exactly.</summary>
    public bool SameContent(EditorialDecisionClaims other) =>
        RequestId == other.RequestId && ActorId == other.ActorId && Decision == other.Decision && Comment == other.Comment &&
        WaitId == other.WaitId && ArticleHash == other.ArticleHash;
}

/// <summary>
/// Purpose-bound authenticated envelope over the existing <see cref="ISecretValueProtector"/>. The protector
/// itself carries no purpose, so the purpose/codec discriminator lives inside the authenticated plaintext and
/// is checked after decryption: a protected secret produced for any other purpose is refused. The codec keeps
/// reading every format it ever wrote; keys follow the Secrets key-ring retention.
/// </summary>
public sealed class EditorialDecisionCodec(ISecretValueProtector protector)
{
    internal const string Purpose = "elsa.agent.editorial-decision/1";
    internal const string Codec = "editorial-decision/1";
    private const int MaximumEnvelopeCharacters = 32_768;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal string Protect(EditorialDecisionClaims claims)
    {
        if (claims.Purpose != Purpose || claims.Codec != Codec) throw EditorialReviewException.Invalid("The decision envelope purpose is not editorial.");
        return protector.Protect(JsonSerializer.Serialize(claims, Json));
    }

    internal EditorialDecisionClaims Read(string? envelope)
    {
        if (string.IsNullOrWhiteSpace(envelope) || envelope.Length > MaximumEnvelopeCharacters)
            throw EditorialReviewException.Invalid("The decision envelope is missing or oversized.");
        string plaintext;
        try { plaintext = protector.Unprotect(envelope); }
        catch (Exception exception) when (exception is CryptographicException or FormatException or InvalidOperationException or ArgumentException)
        {
            throw EditorialReviewException.Invalid("The decision envelope is not authentic.");
        }
        EditorialDecisionClaims? claims;
        try { claims = JsonSerializer.Deserialize<EditorialDecisionClaims>(plaintext, Json); }
        catch (JsonException) { throw EditorialReviewException.Invalid("The protected value is not an editorial decision."); }
        if (claims is null || claims.Purpose != Purpose || claims.Codec != Codec ||
            new[] { claims.TenantId, claims.Partition, claims.WorkflowExecutionId, claims.ActivityExecutionId, claims.ExecutableNodeId,
                claims.WaitId, claims.BookmarkId, claims.ArticleHash, claims.ActorId, claims.RequestId }.Any(string.IsNullOrWhiteSpace) ||
            claims.Round is < 1 or > EditorialReviewIds.MaximumRounds || !Enum.IsDefined(claims.Decision) ||
            claims.Comment?.Length > EditorialReviewIds.MaximumCommentCharacters || claims.ExpiresAt <= claims.IssuedAt)
            throw EditorialReviewException.Invalid("The protected value is not an editorial decision.");
        return claims;
    }

    internal static string HashArticle(string article) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(article)));

    internal static string WaitId(string workflowExecutionId, string activityExecutionId, int round, string articleHash) =>
        "editorial-review:" + Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{workflowExecutionId}\n{activityExecutionId}\n{round}\n{articleHash}")))[..40];
}

/// <summary>A safe, typed refusal. <see cref="IsUnavailable"/> marks a temporary dependency that leaves work owed.</summary>
public sealed class EditorialReviewException : Exception
{
    private EditorialReviewException(string code, string message, bool unavailable, Exception? inner = null) : base(message, inner)
    {
        Code = code;
        IsUnavailable = unavailable;
    }

    public string Code { get; }

    public bool IsUnavailable { get; }

    internal static EditorialReviewException Invalid(string message) => new("editorial.review.invalid", message, false);

    internal static EditorialReviewException Refused(string code, string message) => new(code, message, false);

    internal static EditorialReviewException Unavailable(string message, Exception? inner = null) => new("editorial.review.unavailable", message, true, inner);
}
