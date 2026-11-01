using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Elsa.Agent.Specialists.Core.Models;

/// <summary>Finite content-retention scan in one explicit tenant and persistence partition.</summary>
public sealed record SpecialistRetentionQuery(string? TenantId, string PersistencePartition, string? AfterHandle = null, int PageSize = 32);

/// <summary>Content-free retention evidence. Snapshot is opaque and valid only for its originating store and unchanged turn.</summary>
public sealed record SpecialistRetentionCandidate(string Handle, SpecialistInvocationIdentity Identity, SpecialistTurnStatus Status,
    DateTimeOffset Deadline, int DeliveryProtocol, IReadOnlyList<SpecialistTurnDelivery>? Deliveries,
    IReadOnlyList<SpecialistTurnApplication>? Applications, string Snapshot)
{
    /// <summary>Optional conversation whose unsettled ownership continues to pin retained history.</summary>
    public SpecialistConversationAdmission? Conversation { get; init; }
}

/// <summary>A bounded metadata page. NextHandle advances over scanned records, including records pinned or already purged.</summary>
public sealed record SpecialistRetentionPage(IReadOnlyList<SpecialistRetentionCandidate> Items, string? NextHandle);

internal static class SpecialistRetentionRules
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { MaxDepth = 32 };
    internal static bool Settled(SpecialistTurnStatus status) => status is SpecialistTurnStatus.Accepted or SpecialistTurnStatus.Failed or SpecialistTurnStatus.Cancelled;
    internal static void Query(SpecialistRetentionQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        ArgumentException.ThrowIfNullOrWhiteSpace(query.PersistencePartition);
        if (query.TenantId is not null) ArgumentException.ThrowIfNullOrWhiteSpace(query.TenantId);
        var utf8 = new UTF8Encoding(false, true);
        _ = utf8.GetByteCount(query.PersistencePartition);
        if (query.TenantId is not null) _ = utf8.GetByteCount(query.TenantId);
        if (query.PageSize is < 1 or > 128) throw new ArgumentOutOfRangeException(nameof(query));
        if (query.AfterHandle is not null) SpecialistTurnDocumentRules.Token(query.AfterHandle);
    }
    internal static string Snapshot(SpecialistTurnDocument document) => Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(document, Json))).ToLowerInvariant();
    internal static SpecialistRetentionCandidate Candidate(SpecialistTurnDocument document, string snapshot) => new(document.Handle, document.Identity, document.Status,
        document.Deadline, document.DeliveryProtocol, document.Deliveries?.ToArray(), document.Applications?.ToArray(), snapshot) { Conversation = document.Conversation };
    internal static bool Matches(SpecialistTurnDocument document, SpecialistRetentionCandidate expected, string snapshot) =>
        expected == Candidate(document, snapshot) with { Deliveries = expected.Deliveries, Applications = expected.Applications } &&
        (document.Deliveries ?? []).SequenceEqual(expected.Deliveries ?? []) && (document.Applications ?? []).SequenceEqual(expected.Applications ?? []);
    internal static SpecialistTurnDocument Purge(SpecialistTurnDocument document, DateTimeOffset now)
    {
        if (!Settled(document.Status) || document.Deadline >= now || document.ContentPurgedAt is not null)
            throw new InvalidOperationException("Only unchanged settled specialist content can be purged after its deadline.");
        var next = document with { ProtectedRequest = null, ProtectedResult = null, ContentPurgedAt = now };
        SpecialistTurnDocumentRules.Validate(next);
        return next;
    }
}
