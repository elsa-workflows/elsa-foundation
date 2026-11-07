export const triggerCapability = { id: "elsa.api.standalone-triggers", contractVersion: "1" } as const;
export type Revision = number;
export type JsonValue = null | boolean | number | string | JsonValue[] | { [key: string]: JsonValue };
export interface Page<T> { items: T[]; next?: string | null }
export interface Finding { code: string; message: string; inputName?: string; field?: string }
export interface ActionAvailability { action: string; allowed: boolean; reasonCode?: string; message?: string }
export interface ActorView { subjectId: string; canRead: boolean; canManage: boolean; canOperate: boolean; canAuthorize: boolean; canReadDeliveries: boolean; canRetryDeliveries: boolean; canCancelPending: boolean }
export interface Origin { kind: "Studio" | "Provider"; sourceId?: string; configurationReadOnly: boolean }
export type HttpMode = "WorkflowResponse" | "AsyncFanOut";
export type AdmissionPolicy = "RequireAll" | "AcceptAvailable";
export interface TriggerHttp { mode: HttpMode; admissionPolicy?: AdmissionPolicy; proposedEndpoint?: string; reservationState: "Unreserved" | "Reserved" | "Conflict" | "Unavailable"; reservedEndpoint?: string }
export interface TriggerSchedule { generation?: number; timezone?: string; nextDueUtc?: string; offset?: string; missedPolicy?: string; pausePolicy?: string; isActive?: boolean; isOverdue?: boolean; failureCount?: number; nextRetryAt?: string; occurrenceInFlight?: boolean; failureReason?: "NoEligibleSubscribers" | "AdmissionUnavailable" | null }
export interface TriggerView {
  id: string; name: string; description?: string; typeKey: string; schemaVersion: string; revision: Revision;
  configuration: Record<string, JsonValue>; origin: Origin;
  desiredState: "Paused" | "Enabled" | "Deleted";
  effectiveState: "Paused" | "Enabled" | "EnabledUnavailable" | "Unavailable" | "Deleted";
  subscriptionCount: number; eligibleSubscriptionCount: number; findings: Finding[];
  http?: TriggerHttp; schedule?: TriggerSchedule; actions: ActionAvailability[]; createdAt?: string; updatedAt?: string;
}
export interface ConfigurationField { key: string; label: string; description?: string; type: "string" | "number" | "integer" | "boolean" | "json"; required?: boolean; defaultValue?: JsonValue; choices?: { value: string; label: string }[]; sensitive?: boolean; readOnly?: boolean }
export interface TriggerTypeDescriptor { typeKey: string; schemaVersion: string; displayName: string; description?: string; family: "Http" | "Event" | "Timer" | "Cron" | string; configurationFields: ConfigurationField[]; canCreate: boolean; findings?: Finding[]; configurationEditorId?: string }
export interface TargetSelector { definitionId: string; slotName: string }
export interface DeclaredInput { name: string; type: { alias: string; collectionKind: string }; isRequired: boolean; defaultValue?: JsonValue }
export interface InputContract { version: number; inputs: DeclaredInput[] }
export interface ServingTarget { slotRevision: Revision; publicationId?: string; versionId?: string; version?: string; modelKey?: string; profile?: string; inputContractRevision?: string }
export interface TargetView {
  definitionId: string; slotName: string; displayName?: string; state: "Available" | "Unpublished" | "Unavailable";
  serving?: ServingTarget; inputContract?: InputContract | null; contractRevision?: string;
  supportsWorkflowOwnedHttpResponse?: boolean; findings: Finding[];
}
export type ValueSelector = { kind: "Literal"; value: JsonValue } | { kind: "SourceField"; path: string[] };
export type InputBinding = ValueSelector | { kind: "Expression"; template: { language: string; source: string; resultType: Record<string, JsonValue>; options: Record<string, JsonValue>; capabilityProfile: "binding-pure-v1" }; parameters: Record<string, ValueSelector> };
export interface InputMapping { inputName: string; binding: InputBinding }
export interface InvocationGrant { state: "Absent" | "Granted" | "Revoked" | "Unavailable"; revision?: Revision; scopeFingerprint?: string; grantedAt?: string; grantedBy?: string; findings?: Finding[] }
export interface SubscriptionView {
  id: string; triggerId: string; revision: Revision; enabled: boolean; origin: Origin; target: TargetSelector; mappings: InputMapping[];
  invocationGrant: InvocationGrant; compatibility: "Valid" | "ValidationPending" | "Blocked"; findings: Finding[]; serving?: ServingTarget;
  triggerSummary?: { name?: string; effectiveState: string; http?: Pick<TriggerHttp, "mode" | "admissionPolicy"> }; actions: ActionAvailability[];
}
export type TriggerCommand =
  | { kind: "SaveConfiguration"; name: string; description?: string; configuration: Record<string, JsonValue>; http?: { mode: HttpMode; admissionPolicy?: AdmissionPolicy } }
  | { kind: "AddSubscription"; target: TargetSelector; mappings: InputMapping[]; enabled: boolean; invocationRequest: "None" | "Grant" }
  | { kind: "SaveSubscription"; subscriptionId: string; expectedSubscriptionRevision: Revision; target: TargetSelector; mappings: InputMapping[]; enabled: boolean; invocationRequest: "Keep" | "Grant" }
  | { kind: "RemoveSubscription" | "EnableSubscription" | "PauseSubscription"; subscriptionId: string; expectedSubscriptionRevision: Revision }
  | { kind: "GrantInvocation" | "RevokeInvocation"; subscriptionId: string; expectedSubscriptionRevision: Revision; expectedGrantRevision: Revision | null }
  | { kind: "EnableTrigger" | "PauseTrigger" | "DeleteTrigger" }
  | { kind: "RetryRecipients" | "CancelPendingRecipients"; deliveryId: string; expectedDeliveryRevision: Revision; recipients: { recipientId: string; expectedRevision: Revision }[] };
export interface ImpactItem { label: string; before?: string; after?: string; description?: string }
export interface ChangePreview { previewId: string; expiresAt: string; observedRevision: Revision; commandFingerprint: string; canApply: boolean; impact: ImpactItem[]; findings: Finding[] }
export interface ChangeResult { requestId: string; disposition: "Applied" | "AlreadyApplied"; revision: Revision; trigger?: TriggerView; subscription?: SubscriptionView; effects?: { message: string; recipientId?: string; outcome?: string }[] }
export interface CreateTriggerRequest { requestId: string; name: string; description?: string; typeKey: string; schemaVersion: string; configuration: Record<string, JsonValue>; http?: { mode: HttpMode; admissionPolicy?: AdmissionPolicy } }
export interface MappingContext { sourceSchema: { fields: { path: string[]; label: string; type?: string }[] }; target: TargetView; supportedExpressions: { language: string; displayName: string; available: boolean; reason?: string }[]; findings: Finding[] }
export interface MappingPreview { advisory: true; contractRevision?: string; fields: { inputName: string; state: "Mapped" | "Defaulted" | "Omitted" | "Invalid" | "Redacted"; value?: JsonValue }[]; findings: Finding[] }
export interface RecipientView {
  id: string; revision: Revision; subscriptionId?: string; target?: TargetSelector; capturedVersion?: string; currentVersion?: string;
  admission: "Accepted" | "Refused" | "NotAcceptedDueToPolicy";
  dispatch: "NotDispatched" | "DispatchCommitted" | "AdmissionUnknown" | "Queued" | "Started" | "Failed" | "Cancelled";
  plannedExecutionIdentity?: string;
  executionReference?: { id: string; kind: string; status?: string; observedAt?: string; inspectionPath?: string };
  findings: Finding[]; actions: ActionAvailability[]; nextRetryAt?: string; attemptCount?: number;
}
export interface DeliveryView {
  id: string; revision: Revision; triggerId: string; triggerName?: string; triggerRevision: Revision; acceptedTriggerRevision?: Revision; sourceOccurrenceId?: string;
  admission: "Refused" | "Accepted" | "PartiallyAccepted"; admissionPolicy?: AdmissionPolicy; acceptedCount: number; refusedCount: number;
  recordedAt?: string; acceptedAt?: string; recipients: RecipientView[]; findings: Finding[];
  transport?: { state: "NotApplicable" | "AwaitingResponse" | "WriteCompleted" | "ClientDisconnected" | "TimedOut" | "WriteFailed" | "ReceiptUnknown"; statusCode?: number; observedAt?: string };
}
