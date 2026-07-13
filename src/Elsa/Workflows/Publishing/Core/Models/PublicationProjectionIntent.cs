namespace Elsa.Workflows.Publishing.Core.Models;

public enum PublicationProjectionOperation
{
    Prepare,
    Activate,
    Remove
}

public enum PublicationProjectionIntentStatus
{
    Pending,
    Delivering,
    Delivered,
    Failed
}

public static class PublicationProjectionKinds
{
    public const string TriggerBindings = "TriggerBindings";
    public const string RecurringSchedules = "RecurringSchedules";
    public const string HttpRoutes = "HttpRoutes";
}

/// <summary>A durable, idempotently delivered instruction for one serving-projection owner.</summary>
public sealed record PublicationProjectionIntent(
    string IntentId,
    string PublicationId,
    string ProjectionKind,
    PublicationProjectionOperation Operation,
    PublicationProjectionIntentStatus Status,
    int AttemptCount,
    DateTimeOffset? NextAttemptAt,
    PublicationFailure? LastFailure);

public sealed record PublicationProjectionIntentTransitionResult(
    bool Succeeded,
    PublicationProjectionIntent Intent);

