using Elsa.Activities.Design.Core.Models;
using Elsa.Primitives.Entities;
using System.Security.Cryptography;
using System.Text;

namespace Elsa.Activities.Design.Persistence.Core.Entities;

/// <summary>
/// Explicit lifecycle for a bounded reusable-activity fork reservation.
/// Expiry is derived from <see cref="ExpiresAt"/> and never requires a write merely to reject use.
/// </summary>
public enum ActivityForkCandidateStatus
{
    Reserved,
    Applied
}

/// <summary>
/// Durable, actor-scoped reservation of the exact identities and authoring material reviewed by a
/// caller before a source-owned activity version is forked.
/// </summary>
public sealed class ActivityForkCandidate : TenantEntity
{
    public string CandidateId { get; init; } = null!;

    public string PreviewIdempotencyKey { get; init; } = null!;

    public string RequestFingerprint { get; init; } = null!;

    public string AccessBindingFingerprint { get; init; } = null!;

    public string ActorId { get; init; } = null!;

    public string AuthorizationProfile { get; init; } = null!;

    public string SourceDefinitionId { get; init; } = null!;

    public string SourceVersionId { get; init; } = null!;

    public string SourceVersion { get; init; } = null!;

    public string SourceProviderFingerprint { get; init; } = null!;

    public string TargetProviderFingerprint { get; init; } = null!;

    public ActivityDefinition ReservedDefinition { get; init; } = null!;

    public ActivityDefinitionAuthoringState ReservedAuthoringState { get; init; } = null!;

    public ActivityDefinitionDraft ReservedDraft { get; init; } = null!;

    public ActivityDefinitionDraftLayout ReservedLayout { get; init; } = null!;

    public ICollection<ActivityDiagnostic> MigrationDiagnostics { get; init; } = [];

    public string SourceContractFingerprint { get; init; } = null!;

    public string TargetContractFingerprint { get; init; } = null!;

    public DateTimeOffset ExpiresAt { get; init; }

    public DateTimeOffset RetainUntil { get; init; }

    public string RetentionKey { get; init; } = null!;

    public ActivityForkCandidateStatus Status { get; set; } = ActivityForkCandidateStatus.Reserved;

    public string? AppliedIdempotencyKey { get; set; }
}

public enum ActivityForkReceiptStatus
{
    Applied
}

/// <summary>Durable terminal proof used to reconcile a lost successful fork response.</summary>
public sealed class ActivityForkReceipt : TenantEntity
{
    public string IdempotencyKey { get; init; } = null!;

    public string CandidateId { get; init; } = null!;

    public string PublicCandidateId { get; init; } = null!;

    public string RequestFingerprint { get; init; } = null!;

    public string AccessBindingFingerprint { get; init; } = null!;

    public string ActorId { get; init; } = null!;

    public string AuthorizationProfile { get; init; } = null!;

    public ActivityForkReceiptStatus Status { get; init; } = ActivityForkReceiptStatus.Applied;

    public string DefinitionId { get; init; } = null!;

    public string ActivityTypeKey { get; init; } = null!;

    public string DraftId { get; init; } = null!;

    public ActivityDefinition Definition { get; init; } = null!;

    public ActivityDefinitionAuthoringState AuthoringState { get; init; } = null!;

    public ActivityDefinitionDraft Draft { get; init; } = null!;

    public DateTimeOffset AppliedAt { get; init; }
}

public static class ActivityForkReceiptIdentity
{
    public static string Compute(string? tenantId, string actorId, string idempotencyKey)
    {
        var material = $"{tenantId ?? "<global>"}\u001f{actorId}\u001f{idempotencyKey}";
        return $"activity-fork-receipt-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }
}

public static class ActivityForkCandidateIdentity
{
    public static string Compute(string? tenantId, string actorId, string idempotencyKey)
    {
        var material = $"{tenantId ?? "<global>"}\u001f{actorId}\u001f{idempotencyKey}";
        return $"activity-fork-candidate-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant()}";
    }

    public static string RetentionKey(DateTimeOffset retainUntil) =>
        retainUntil.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
}
