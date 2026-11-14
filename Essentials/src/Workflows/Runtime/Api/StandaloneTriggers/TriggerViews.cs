using System.Text.Json;
using System.Text.Json.Serialization;
using Elsa.Workflows.Runtime.Core.Models;
using Elsa.Workflows.Runtime.Core.StandaloneTriggers;

namespace Elsa.Workflows.Runtime.Api.StandaloneTriggers;

internal sealed record TriggerAction(string Action, bool Allowed, string? ReasonCode = null, string? Message = null);
internal sealed record TriggerActor(string SubjectId, bool CanRead, bool CanManage, bool CanOperate, bool CanAuthorize,
    bool CanReadDeliveries, bool CanRetryDeliveries, bool CanCancelPending);
internal sealed record TriggerHttpView(string Mode, string? AdmissionPolicy, string ReservationState, string? ProposedEndpoint, string? ReservedEndpoint = null);
internal sealed record TriggerScheduleView(long Generation, string Timezone, DateTimeOffset NextDueUtc, string? Offset,
    string MissedPolicy, string PausePolicy, bool IsActive, bool IsOverdue, int? FailureCount = null,
    DateTimeOffset? NextRetryAt = null, bool? OccurrenceInFlight = null, string? FailureReason = null);
internal sealed record TriggerView(string Id, string Name, string? Description, string TypeKey, string SchemaVersion,
    long Revision, JsonElement Configuration, TriggerManagementOrigin Origin, string DesiredState, string EffectiveState,
    int SubscriptionCount, int EligibleSubscriptionCount, IReadOnlyList<TriggerFinding> Findings,
    IReadOnlyList<TriggerAction> Actions, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, TriggerHttpView? Http = null, TriggerScheduleView? Schedule = null,
    IReadOnlyList<TriggerConfigurationRedaction>? ConfigurationRedactions = null);
internal sealed record TriggerConfigurationRedaction(IReadOnlyList<string> Path, string Reason = "Protected");
internal sealed record TriggerTargetSelector(string DefinitionId, string SlotName = "default");
internal sealed record TriggerServing(long? SlotRevision, string? PublicationId, string? VersionId, string? Version,
    string ModelKey, string? InputContractRevision);
internal sealed record TriggerTargetView(string DefinitionId, string SlotName, string? DisplayName, string State,
    TriggerServing? Serving, WorkflowExecutableInputContract? InputContract, string? ContractRevision,
    bool SupportsWorkflowOwnedHttpResponse, IReadOnlyList<TriggerFinding> Findings)
{
    internal static TriggerTargetView From(TriggerTargetDescription value) => new(value.DefinitionId, value.SlotName,
        value.DisplayName, value.State.ToString(), value.SlotRevision is null ? null : new(value.SlotRevision,
            value.PublicationId, value.VersionId, value.Version, value.ModelKey, value.ContractRevision),
        value.InputContract, value.ContractRevision, value.SupportsWorkflowOwnedHttpResponse, value.Findings);
}
internal sealed record TriggerGrantView(string State, long? Revision = null, DateTimeOffset? GrantedAt = null,
    string? GrantedBy = null, IReadOnlyList<TriggerFinding>? Findings = null);
internal sealed record TriggerSubscriptionView(string Id, string TriggerId, long Revision, bool Enabled,
    TriggerManagementOrigin Origin, TriggerTargetSelector Target, IReadOnlyList<JsonElement> Mappings,
    TriggerGrantView InvocationGrant, string Compatibility, IReadOnlyList<TriggerFinding> Findings,
    TriggerServing? Serving, IReadOnlyList<TriggerAction> Actions, object? TriggerSummary = null);
internal sealed record TriggerPage<T>(IReadOnlyList<T> Items, string? Next);
internal sealed record TriggerTypeView(string TypeKey, string SchemaVersion, string DisplayName, string Family,
    IReadOnlyList<TriggerConfigurationField> ConfigurationFields, bool CanCreate, string? Description)
{
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ConfigurationEditorId { get; init; }
}
internal sealed record TriggerImpact(string Label, string? Before = null, string? After = null, string? Description = null);
internal sealed record TriggerPreview(string PreviewId, DateTimeOffset ExpiresAt, long ObservedRevision,
    string CommandFingerprint, bool CanApply, IReadOnlyList<TriggerImpact> Impact, IReadOnlyList<TriggerFinding> Findings);
internal sealed record TriggerChangeResult(string RequestId, string Disposition, long Revision, TriggerView? Trigger,
    TriggerSubscriptionView? Subscription = null, IReadOnlyList<object>? Effects = null);
internal sealed record TriggerProblem(string Code, string Message, IReadOnlyDictionary<string, string>? Errors = null);
internal sealed record TriggerPreviewProof(string Actor, TriggerScope Scope, string TriggerId, long Revision,
    string CommandFingerprint, string ObservationFingerprint, DateTimeOffset ExpiresAt);
internal sealed record TriggerResourcePermission(string TriggerId, string? DefinitionId = null, string? SlotName = null);
