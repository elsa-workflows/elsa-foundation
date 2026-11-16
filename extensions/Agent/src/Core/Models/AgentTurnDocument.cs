using Elsa.Agent.Core.Exceptions;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Elsa.Agent.Core.Models;

/// <summary>Journal lifecycle. Provider dispatch uncertainty is never an implicit permission to retry.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AgentTurnStatus>))]
public enum AgentTurnStatus { Prepared, Claimed, Dispatching, Accepted, Failed, OutcomeUnknown, Cancelled }

/// <summary>
/// Protected native turn material and the conditional journal header. Business/session payloads remain
/// opaque to persistence providers; immutable acceptance stores their protected pair in one write.
/// </summary>
public sealed record AgentTurnDocument(string Handle, string Key, string RequestHash, AgentInvocationIdentity Identity,
    string? ProtectedRequest, DateTimeOffset Deadline, AgentTurnStatus Status = AgentTurnStatus.Prepared,
    string? ChildInvocationId = null, string? Fence = null, string? ProtectedResult = null, string? FailureCode = null)
{
    /// <summary>Content was removed under retention; operation and lifecycle metadata remain authoritative.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? ContentPurgedAt { get; init; }

    /// <summary>The published/request-lowered finite model request limit, authenticated by the runtime.</summary>
    public int MaximumModelCalls { get; init; } = 1;
    /// <summary>Retained reservations, including requests with unknown outcomes or usage.</summary>
    public IReadOnlyList<AgentModelRequestReservation>? ModelRequests { get; init; }
    /// <summary>The completed response freezes further requests; unknown counts retain their reservations.</summary>
    public AgentModelResponseObservation? ResponseObservation { get; init; }

    /// <summary>Optional workflow-owned conversation admission. A zero revision creates a fresh conversation at claim.</summary>
    public AgentConversationAdmission? Conversation { get; init; }

    /// <summary>Explicit native delivery protocol; zero retains exact-original legacy behavior.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int DeliveryProtocol { get; init; }
    /// <summary>Verified parent/child associations, separate from immutable dispatch/result provenance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentTurnDelivery>? Deliveries { get; init; }
    /// <summary>Confirmed native process applications, independent of accepted conversation history.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IReadOnlyList<AgentTurnApplication>? Applications { get; init; }
    /// <summary>The parent association that owns the physical dispatch; immutable after acceptance.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ActiveDeliveryActivityId { get; init; }

    /// <summary>Monotonic claim generation; zero identifies a prepared or legacy unleased record.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public int AdmissionGeneration { get; init; }
    /// <summary>Finite journal claim expiry, renewed separately from native background delivery ownership.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public DateTimeOffset? LeaseExpiresAt { get; init; }

    /// <summary>
    /// Identifies one operation in its native workflow and ordinary tenant/partition scope.
    /// Attempts and dispatch ownership remain separate conditional journal state.
    /// </summary>
    /// <exception cref="AgentJournalException">The identity contains malformed Unicode.</exception>
    public static string CalculateOperationKey(AgentInvocationIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        try
        {
            var utf8 = new UTF8Encoding(false, true);
            foreach (var value in new[] { identity.PersistencePartition, identity.WorkflowExecutionId, identity.OperationKey })
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(value);
                _ = utf8.GetByteCount(value);
            }
            if (identity.TenantId is { } tenant)
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(tenant);
                _ = utf8.GetByteCount(tenant);
            }
            var material = JsonSerializer.Serialize(new { identity.TenantId, identity.PersistencePartition, identity.WorkflowExecutionId, identity.OperationKey });
            return Convert.ToHexString(SHA256.HashData(utf8.GetBytes(material))).ToLowerInvariant();
        }
        catch (EncoderFallbackException exception)
        {
            throw new AgentJournalException("agent.journal.identity", null, "The agent operation identity is not valid Unicode.", exception);
        }
    }
}

/// <summary>The configured journal backend; reachability and provider/host qualification are separate evidence.</summary>
public sealed record AgentJournalCapabilities(string Provider, bool IsDurable);

/// <summary>One journal claim's opaque fence and monotonic admission generation. It grants no resource access on its own.</summary>
public sealed record AgentTurnLeaseToken(string Fence, int Generation);

/// <summary>One verified native delivery of a logical operation. An association grants no resource access by itself.</summary>
public sealed record AgentTurnDelivery(AgentInvocationIdentity Identity, string? ChildInvocationId = null);

/// <summary>A native delivery applied to its process, with the exact durable settlement source. It grants no authority by itself.</summary>
public sealed record AgentTurnApplication(AgentInvocationIdentity Identity, string ChildInvocationId,
    string SourceId, string SourceFingerprint, DateTimeOffset AppliedAt);
