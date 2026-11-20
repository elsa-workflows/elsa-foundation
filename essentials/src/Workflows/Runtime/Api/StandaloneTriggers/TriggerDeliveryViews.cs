using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed record TriggerExecutionReference(string Id, string Kind, string? Status, DateTimeOffset ObservedAt, string? InspectionPath = null);
internal sealed record TriggerRecipientView(string Id, long Revision, string? SubscriptionId, TriggerTargetSelector? Target,
    string? CapturedVersion, string? CurrentVersion, string Admission, string Dispatch, string? PlannedExecutionIdentity,
    TriggerExecutionReference? ExecutionReference, IReadOnlyList<TriggerFinding> Findings, IReadOnlyList<TriggerAction> Actions,
    DateTimeOffset? NextRetryAt = null, int AttemptCount = 0);
internal sealed record TriggerTransportView(string State, int? StatusCode = null, DateTimeOffset? ObservedAt = null);
internal sealed record TriggerDeliveryView(string Id, long Revision, string TriggerId, string TriggerName,
    long? AcceptedTriggerRevision, string? SourceOccurrenceId, string Admission, string? AdmissionPolicy,
    int AcceptedCount, int RefusedCount, DateTimeOffset RecordedAt, DateTimeOffset? AcceptedAt,
    IReadOnlyList<TriggerRecipientView> Recipients, IReadOnlyList<TriggerFinding> Findings, TriggerTransportView Transport, long TriggerRevision);
internal sealed record TriggerDeliveryPreviewProof(string Actor, TriggerScope Scope, string TriggerId, long TriggerRevision,
    string SourceId, string SourceFingerprint, string SelectionFingerprint, string CommandFingerprint,
    long DeliveryRevision, IReadOnlyList<RuntimeIngressRecipientExpectation> Recipients, DateTimeOffset ExpiresAt);
internal sealed record TriggerDeliveryCursor(string Actor, TriggerScope Scope, string? TriggerId, string? SourceAfter,
    string? SourceId = null, string? AfterTrigger = null);
