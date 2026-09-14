namespace Elsa.Workflows.Publishing.Core.Models;

public enum PublicationAction
{
    Replace,
    PublishSideBySide
}

public enum PublicationPolicyDefaultAction
{
    ReplaceDefaultSlot,
    RequireExplicitSlot
}

public enum PublicationPolicySource
{
    Request,
    Workflow,
    Host
}

public enum PublicationStatus
{
    Candidate,
    PendingProjection,
    Active,
    Retired,
    Failed
}

public sealed record PublicationRequestIntent(PublicationAction Action, string? SlotName = null);

public sealed record PublicationPolicy(
    string? WorkflowDefinitionId,
    PublicationPolicyDefaultAction DefaultAction,
    string DefaultSlotName,
    long Revision,
    DateTimeOffset UpdatedAt);

public sealed record ResolvedPublicationAction(
    string WorkflowDefinitionId,
    string WorkflowDefinitionVersionId,
    PublicationAction Action,
    string SlotName,
    PublicationPolicySource PolicySource,
    long? PolicyRevision,
    string? ExpectedPublicationId = null);

public sealed class PublicationPolicyResolutionException(string code, string message) : ArgumentException(message)
{
    public string Code { get; } = code;
}

public sealed record PublicationFailure(string Code, string Message);

/// <summary>
/// The single source for every Publishing failure code, surfaced either as a problem response's
/// <c>errorCode</c> or as a publication record's <see cref="PublicationFailure.Code"/>, so a client can match on
/// these values without parsing English prose (issue #1699).
/// </summary>
public static class PublicationFailureCodes
{
    /// <summary>The target activation slot is owned by another activation source; ownership transfer is an
    /// explicit operator action (ADR 0043).</summary>
    public const string SlotOwnerConflict = "slot_owner_conflict";

    /// <summary>The publication slot's optimistic revision changed between preflight and activation.</summary>
    public const string SlotRevisionConflict = "slot_revision_conflict";

    /// <summary>Activation failed and its compensation did not converge.</summary>
    public const string ActivationCompensationFailed = "activation_compensation_failed";

    /// <summary>Preparing the activation's serving projection failed.</summary>
    public const string ProjectionPreparationFailed = "projection_preparation_failed";

    /// <summary>Activating the serving projection, or notifying its trigger observers, failed.</summary>
    public const string ProjectionActivationFailed = "projection_activation_failed";

    /// <summary>Publication activation failed for a reason not otherwise classified.</summary>
    public const string PublicationActivationFailed = "publication_activation_failed";

    /// <summary>The runtime activation coordinator refused to run the activation lifecycle.</summary>
    public const string PublicationActivationRefused = "publication_activation_refused";

    /// <summary>A serving-projection intent could not be delivered.</summary>
    public const string ProjectionDeliveryFailed = "projection_delivery_failed";

    /// <summary>A workflow publication policy is scoped to a different workflow definition than the one it was
    /// resolved for.</summary>
    public const string WorkflowPolicyMismatch = "workflow_policy_mismatch";

    /// <summary>A host publication policy is scoped to a workflow definition, which is not permitted.</summary>
    public const string InvalidHostPolicy = "invalid_host_policy";

    /// <summary>The workflow's policy requires an explicit publication slot, but the request did not supply one.</summary>
    public const string ExplicitSlotRequired = "explicit_slot_required";

    /// <summary>The resolved policy's default slot name is missing or blank.</summary>
    public const string InvalidDefaultSlot = "invalid_default_slot";

    /// <summary>The requested <see cref="PublicationAction"/> is not supported.</summary>
    public const string UnsupportedAction = "unsupported_action";

    /// <summary>Side-by-side publication requires a meaningful named slot other than the default slot.</summary>
    public const string NamedSlotRequired = "named_slot_required";

    /// <summary>The publication slot's active publication no longer matches the request's expected publication.</summary>
    public const string ExpectedPublicationMismatch = "expected_publication_mismatch";

    /// <summary>A publication snapshot review token is stale or does not match the current publish request.</summary>
    public const string PublicationSnapshotStale = "publication_snapshot_stale";

    /// <summary>Publication trigger preflight found one or more authoritative conflicts.</summary>
    public const string TriggerConflict = "trigger_conflict";

    /// <summary>The optimistic publication policy write lost its revision race.</summary>
    public const string PolicyRevisionConflict = "policy_revision_conflict";
}

/// <summary>Transport-facing publication status projection. Lives in Core so <see cref="PublishedWorkflowView"/>
/// (relocated for the engine/API split, spec 145) can carry it without an Api reference. The Api mapper
/// <c>PublicationContract.ToView</c> and other view types remain in the Api transport layer.</summary>
public enum PublicationStatusView
{
    Preparing,
    Pending,
    Active,
    Retiring,
    Retired,
    Failed
}
