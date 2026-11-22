using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Agent.Core.Models;

/// <summary>Finite content-retention scan in one explicit tenant and persistence partition.</summary>
public sealed record AgentRetentionQuery(string? TenantId, string PersistencePartition, string? AfterHandle = null, int PageSize = 32);

/// <summary>Content-free retention evidence. Snapshot is opaque and valid only for its originating store and unchanged turn.</summary>
public sealed record AgentRetentionCandidate(string Handle, AgentInvocationIdentity Identity, AgentTurnStatus Status,
    DateTimeOffset Deadline, int DeliveryProtocol, IReadOnlyList<AgentTurnDelivery>? Deliveries,
    IReadOnlyList<AgentTurnApplication>? Applications, string Snapshot)
{
    /// <summary>Optional conversation whose unsettled ownership continues to pin retained history.</summary>
    public AgentConversationAdmission? Conversation { get; init; }
    /// <summary>Retained claim generation; zero distinguishes cancellation before any conversation claim.</summary>
    public int AdmissionGeneration { get; init; }
}

/// <summary>A bounded metadata page. NextHandle advances over scanned records, including records pinned or already purged.</summary>
public sealed record AgentRetentionPage(IReadOnlyList<AgentRetentionCandidate> Items, string? NextHandle);

internal static class AgentRetentionRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    internal static bool Settled(AgentTurnStatus status) => status is AgentTurnStatus.Accepted or AgentTurnStatus.Failed or AgentTurnStatus.Cancelled;
    internal static void Query(AgentRetentionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.PersistencePartition);
        if (query.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId);
        var utf8 = new UTF8Encoding(false, true);
        _ = utf8.GetByteCount(query.PersistencePartition);
        if (query.TenantId is not null) _ = utf8.GetByteCount(query.TenantId);
        if (query.PageSize is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(query));
        if (query.AfterHandle is not null) AgentTurnDocumentRules.Token(query.AfterHandle);
    }
    internal static string Snapshot(AgentTurnDocument document) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, Json))).ToLowerInvariant();
    internal static AgentRetentionCandidate Candidate(AgentTurnDocument document, string snapshot) => new(document.Handle, document.Identity, document.Status,
        document.Deadline, document.DeliveryProtocol, document.Deliveries?.ToArray(), document.Applications?.ToArray(), snapshot) { Conversation = document.Conversation, AdmissionGeneration = document.AdmissionGeneration };
    internal static bool Matches(AgentTurnDocument document, AgentRetentionCandidate expected, string snapshot) =>
        expected == Candidate(document, snapshot) with { Deliveries = expected.Deliveries, Applications = expected.Applications } &&
        (document.Deliveries ?? []).SequenceEqual(expected.Deliveries ?? []) && (document.Applications ?? []).SequenceEqual(expected.Applications ?? []);
    internal static bool CanLackConversation(AgentRetentionCandidate candidate) => candidate.Status == AgentTurnStatus.Cancelled &&
        candidate.AdmissionGeneration == 0 && candidate.Conversation is { ExpectedRevision: 0 } conversation && conversation.ConversationId == candidate.Handle;

    internal static AgentTurnDocument Purge(AgentTurnDocument document, DateTimeOffset now)
    {
        if (!Settled(document.Status) || document.Deadline >= now || document.ContentPurgedAt is not null)
            throw new InvalidOperationException("Only unchanged settled agent content can be purged after its deadline.");
        var next = document with { ProtectedRequest = null, ProtectedResult = null, ContentPurgedAt = now };
        AgentTurnDocumentRules.Validate(next);
        return next;
    }
}
