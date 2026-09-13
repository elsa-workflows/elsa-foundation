import type { StudioActivityDiagnostic, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import type {
  DesignMetadataRecord,
  IncidentStrategiesResponse,
  IncidentStrategyDescriptor,
  IncidentStrategyReference,
  StartWorkflowDraftTestRunRequest,
  WorkflowDefinitionState,
  WorkflowTestRunView
} from "../workflowTypes";
import {
  ApiCapabilityUnavailableError,
  ApiCapabilityVersionMismatchError,
  capabilityIds,
  hasCapabilityLink,
  resolveCapabilityLink
} from "./capabilities";
import {
  getWorkflowActivationSlot,
  listWorkflowActivationSlots,
  publishingActivationSourceKind,
  type WorkflowActivationSlot
} from "./runtime";
import { createWorkflowExecutionRequestInit } from "../workflowRunInputs";

export interface PublicationIntent {
  slotName?: string;
  action?: "replace" | "sideBySide";
  expectedPublicationId?: string | null;
  preflightToken?: string;
}

export async function listIncidentStrategies(context: StudioEndpointContext): Promise<IncidentStrategiesResponse> {
  const path = await resolveCapabilityLink(context, capabilityIds.publishing, "incident-strategies");
  return parseIncidentStrategiesResponse(await context.http.getJson<unknown>(path));
}

function parseIncidentStrategiesResponse(value: unknown): IncidentStrategiesResponse {
  if (!isRecord(value) || !Array.isArray(value.items)) throw invalidIncidentStrategiesResponse();
  const defaultStrategy = parseIncidentStrategyReference(value.defaultStrategy);
  if (!defaultStrategy) throw invalidIncidentStrategiesResponse();
  const items: IncidentStrategyDescriptor[] = [];
  for (const item of value.items) {
    const descriptor = parseIncidentStrategyDescriptor(item);
    if (!descriptor) throw invalidIncidentStrategiesResponse();
    items.push(descriptor);
  }
  return { items, defaultStrategy };
}

function parseIncidentStrategyDescriptor(value: unknown): IncidentStrategyDescriptor | null {
  if (!isRecord(value)) return null;
  const reference = parseIncidentStrategyReference(value);
  if (!reference || typeof value.displayName !== "string") return null;
  if (value.description !== undefined && value.description !== null && typeof value.description !== "string") return null;
  return {
    ...reference,
    displayName: value.displayName,
    ...(value.description !== undefined ? { description: value.description as string | null } : {})
  };
}

function parseIncidentStrategyReference(value: unknown): IncidentStrategyReference | null {
  if (!isRecord(value)) return null;
  return typeof value.alias === "string" && value.alias.length > 0
    && typeof value.version === "string" && value.version.length > 0
    ? { alias: value.alias, version: value.version }
    : null;
}

function isRecord(value: unknown): value is Record<string, unknown> {
  return value != null && typeof value === "object" && !Array.isArray(value);
}

function invalidIncidentStrategiesResponse() {
  return new Error("Incident strategy discovery returned an invalid response.");
}

export interface PublicationSnapshotPreflightRequest extends PublicationIntent {
  definitionId: string;
  state: WorkflowDefinitionState;
  layout: DesignMetadataRecord[];
}

export interface PublicationTriggerClaim {
  key: string;
  cardinality: "exclusive" | "fanOut";
}

export interface PublicationTriggerChange {
  change: "added" | "removed" | "retained";
  key: string;
  cardinality: "exclusive" | "fanOut";
}

export interface PublicationTriggerConflict {
  key: string;
  cardinality: "exclusive" | "fanOut";
  publicationId: string;
  slotName: string;
}

/**
 * The non-publishing activation source occupying the resolved target slot, when one is active. Set
 * exactly when `canActivate` is false because the slot is foreign-owned; taking over such a slot is an
 * operator action (ADR 0043), so the review can only point the author at another channel.
 */
export interface PublicationSlotOwner {
  sourceKind: string;
  sourceId: string | null;
}

export interface PublicationPreflight {
  preflightToken: string;
  candidateHash: string;
  definitionId: string;
  versionId: string | null;
  slotName: string;
  resolvedAction: "replace" | "sideBySide";
  policySource: "request" | "workflow" | "host";
  policyRevision?: number | null;
  canActivate: boolean;
  claims: PublicationTriggerClaim[];
  triggers?: PublicationTriggerChange[];
  changes?: PublicationTriggerChange[];
  conflicts: PublicationTriggerConflict[];
  /** Optional: older hosts without elsa-foundation#1659 never send it. Treat absent the same as `null`. */
  targetSlotOwner?: PublicationSlotOwner | null;
}

export type ActivityVersionChangeImpact = "Breaking" | "Additive" | "NonBehavioral" | string;

export interface ActivityPublicationChange {
  changeId: string;
  area: "Contract" | "Default" | "Outcome" | "Durability" | "Provider" | "Implementation" | "Dependency" | "Presentation" | string;
  kind: string;
  subject: {
    memberKind?: string | null;
    referenceKey?: string | null;
    dependencyVersionId?: string | null;
    occurrenceId?: string | null;
  };
  before?: unknown;
  after?: unknown;
  impact: ActivityVersionChangeImpact;
  requiredBump: string;
  message: string;
}

export interface ActivityPublicationReadiness {
  kind: string;
  key: string;
  schemaVersion?: string | null;
  status: string;
  supportedSchemaVersions: string[];
}

export interface ActivityPublicationPreflight {
  draftId: string;
  draftRevision: number;
  definitionId: string;
  definitionHeadVersionId?: string | null;
  hasBaseline: boolean;
  reviewToken: string;
  isPublishable: boolean;
  minimumVersion: string;
  validVersions: string[];
  diff?: {
    compatibility: string;
    requiredBump: string;
    behaviorChanged: boolean;
    summary: { breaking: number; additive: number; nonBehavioral: number; warnings: number };
  } | null;
  impactFirstChanges: ActivityPublicationChange[];
  dependencies: Array<{
    definitionId: string;
    versionId: string;
    version: string;
    templateHash: string;
    occurrenceId: string;
  }>;
  provider: ActivityPublicationReadiness;
  storage: ActivityPublicationReadiness[];
  runtime: ActivityPublicationReadiness[];
  diagnostics: StudioActivityDiagnostic[];
}

export interface ActivityPublicationOutcome {
  definitionId: string;
  definitionVersionId: string;
  draftId: string;
  version: string;
  templateId: string;
  templateHash: string;
  sourceReferenceId: string;
  publishedAt: string;
}

export interface ActivityPublicationReceipt {
  idempotencyKey: string;
  status: "Applied" | "Rejected" | "Stale" | "Failed" | "OutcomeUnknown" | string;
  draftId: string;
  expectedDraftRevision: number;
  expectedDefinitionHeadVersionId?: string | null;
  reviewToken: string;
  requestedVersion: string;
  outcome?: ActivityPublicationOutcome | null;
  errorCode?: string | null;
  diagnostics: StudioActivityDiagnostic[];
  updatedAt: string;
}

export interface PublishActivityDraftRequest {
  expectedDraftRevision: number;
  expectedDefinitionHeadVersionId?: string | null;
  version: string;
  reviewToken: string;
  idempotencyKey: string;
}

export type ActivityDraftTestRunInputState = "Absent" | "Present";

export interface ActivityDraftTestRunInput {
  state: ActivityDraftTestRunInputState;
  value?: unknown;
}

export interface StartActivityDraftTestRunRequest {
  expectedRevision: number;
  idempotencyKey: string;
  inputs?: Record<string, ActivityDraftTestRunInput>;
  correlationId?: string | null;
}

export interface ActivityDraftTestRunFailure {
  kind: "Validation" | "RuntimeDispatch" | string;
  code: string;
  message: string;
  diagnostics: StudioActivityDiagnostic[];
}

export interface ActivityDraftTestRunExpiration {
  sourceReferenceExpiresAt: string;
  sourceReferenceExpired: boolean;
  sourceReferenceRetained: boolean;
  evidenceRetention: string;
  evidenceExpiresAt?: string | null;
  evidenceRetained: boolean;
  runExpiresAt?: string | null;
  runStillActive: boolean;
  receiptExpiresAt: string;
}

export interface ActivityDraftTestRunCancellation {
  capabilityAdvertised: boolean;
  available: boolean;
  status: string;
  reason?: string | null;
}

export interface ActivityDraftTestRunView {
  testRunId: string;
  draftId: string;
  draftRevision: number;
  artifactId?: string | null;
  sourceReferenceId?: string | null;
  workflowExecutionId: string;
  outerActivityExecutionId?: string | null;
  status: string;
  commandDispatchStatus?: string | null;
  reason?: string | null;
  failure?: ActivityDraftTestRunFailure | null;
  expiration: ActivityDraftTestRunExpiration;
  cancellation: ActivityDraftTestRunCancellation;
  requestedAt: string;
  updatedAt: string;
}

export async function preflightPublicationSnapshot(
  context: StudioEndpointContext,
  request: PublicationSnapshotPreflightRequest
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-snapshot-preflight");
  return context.http.postJson<PublicationPreflight>(path, {
    ...request,
    state: canonicalizeStateForWire(request.state)
  });
}

export type PublicationStatus = "preparing" | "pending" | "active" | "retiring" | "retired" | "failed";

export interface Publication {
  publicationId: string;
  definitionId: string;
  versionId: string;
  definitionVersionId?: string;
  artifactId: string;
  slotName: string;
  sourceReferenceId: string | null;
  status: PublicationStatus;
  createdAt?: string;
  activatedAt?: string | null;
  retiredAt?: string | null;
  artifactHash?: string;
  rootActivityId?: string;
  nodeCount?: number;
}

/**
 * A Runtime activation slot joined to the publication record behind its active activation. `publication`
 * is set exactly when the slot is occupied by a publishing-sourced activation; an empty slot and a slot
 * occupied by another activation source (for example artifact reconciliation) carry `null`. A slot is
 * occupied when `activeActivationId` is set, whatever its source.
 */
export interface PublicationSlot extends WorkflowActivationSlot {
  publication: Publication | null;
}

/**
 * The publication slots of one workflow, or an honest account of why the host cannot provide them.
 * Studio never substitutes a guessed route or an empty list for an unadvertised read.
 */
export type PublicationSlotsView =
  | { available: true; slots: PublicationSlot[] }
  | { available: false; reason: string };

export const activationSlotReadsUnavailableReason =
  "This backend does not advertise workflow activation slot reads. No fallback is used, so Studio cannot tell which publication channels are occupied or which versions they serve.";

export const publicationRecordReadsUnavailableReason =
  "This backend does not advertise publication record reads. No fallback is used, so Studio cannot resolve the versions behind occupied publication channels.";

export interface PublicationPolicy {
  definitionId?: string;
  defaultAction: "replace" | "requireExplicitSlot";
  defaultSlotName: string;
  source: "workflow" | "host";
  revision?: number;
  updatedAt?: string;
}

export interface SetPublicationPolicyRequest {
  defaultAction: "replace" | "requireExplicitSlot";
  defaultSlotName?: string;
  expectedRevision?: number;
}

export async function preflightPublication(
  context: StudioEndpointContext,
  versionId: string,
  intent: PublicationIntent = {}
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-preflight",
    { versionId });
  return context.http.postJson<PublicationPreflight>(path, intent);
}

export async function publishVersion(
  context: StudioEndpointContext,
  versionId: string,
  intent: PublicationIntent = {}
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "workflow-publish",
    { versionId });
  return context.http.postJson<Publication>(path, intent);
}

export async function preflightActivityDraftPublication(
  context: StudioEndpointContext,
  draftId: string,
  expectedDraftRevision: number,
  expectedDefinitionHeadVersionId?: string | null
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-publication-preflight",
    { draftId });
  return context.http.postJson<ActivityPublicationPreflight>(path, {
    expectedDraftRevision,
    expectedDefinitionHeadVersionId: expectedDefinitionHeadVersionId ?? null
  });
}

export async function publishActivityDraft(
  context: StudioEndpointContext,
  draftId: string,
  request: PublishActivityDraftRequest
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-publication",
    { draftId });
  return context.http.postJson<ActivityPublicationReceipt>(path, request);
}

export async function getActivityPublicationReceipt(
  context: StudioEndpointContext,
  idempotencyKey: string
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-publication-receipt",
    { idempotencyKey });
  return context.http.getJson<ActivityPublicationReceipt>(path);
}

export async function startActivityDraftTestRun(
  context: StudioEndpointContext,
  draftId: string,
  request: StartActivityDraftTestRunRequest
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-draft-test-run-dispatch",
    { draftId });
  return context.http.requestJson<ActivityDraftTestRunView>(
    path,
    createWorkflowExecutionRequestInit(request));
}

export async function getActivityDraftTestRun(
  context: StudioEndpointContext,
  testRunId: string
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-draft-test-run-status",
    { testRunId });
  return context.http.getJson<ActivityDraftTestRunView>(path);
}

export async function getActivityDraftTestRunByIdempotencyKey(
  context: StudioEndpointContext,
  draftId: string,
  idempotencyKey: string
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-draft-test-run-idempotency-status",
    { draftId, idempotencyKey });
  return context.http.getJson<ActivityDraftTestRunView>(path);
}

export async function cancelActivityDraftTestRun(
  context: StudioEndpointContext,
  testRunId: string
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "activity-draft-test-run-cancel",
    { testRunId });
  return context.http.postJson<ActivityDraftTestRunView>(path, {});
}

/** Reads one publication journal record. A 404 means the id names no record, never "no version". */
export async function getPublicationRecord(context: StudioEndpointContext, publicationId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-record",
    { publicationId });
  return context.http.getJson<Publication>(path);
}

export function isPublishingActivation(slot: WorkflowActivationSlot) {
  return Boolean(slot.activeActivationId) && slot.sourceKind === publishingActivationSourceKind;
}

/**
 * Names an activation source, for a slot whose activation is not a publication or for a preflight's
 * `targetSlotOwner`. Both shapes carry the same `sourceKind`/`sourceId` pair.
 */
export function describeActivationSource(source: Pick<WorkflowActivationSlot, "sourceKind" | "sourceId">) {
  const kind = source.sourceKind ?? "an unidentified activation source";
  return source.sourceId ? `${kind} (${source.sourceId})` : kind;
}

/**
 * Lists the Runtime activation slots of a workflow and joins every publishing-sourced activation to its
 * publication record. Slot reads that the host does not advertise resolve to an unavailable view; a
 * publication record that is advertised but cannot be read rejects, because a slot without its version
 * cannot back a trustworthy review.
 */
export async function listPublicationSlots(context: StudioEndpointContext, definitionId: string): Promise<PublicationSlotsView> {
  if (!(await hasCapabilityLink(context, capabilityIds.runtime, "workflow-activation-slots"))) {
    return { available: false, reason: activationSlotReadsUnavailableReason };
  }
  const slots = await listWorkflowActivationSlots(context, definitionId);
  if (slots.some(isPublishingActivation) && !(await hasCapabilityLink(context, capabilityIds.publishing, "publication-record"))) {
    return { available: false, reason: publicationRecordReadsUnavailableReason };
  }
  return { available: true, slots: await Promise.all(slots.map(slot => joinPublicationRecord(context, slot))) };
}

export async function getPublicationSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  return joinPublicationRecord(context, await getWorkflowActivationSlot(context, definitionId, slotName));
}

async function joinPublicationRecord(context: StudioEndpointContext, slot: WorkflowActivationSlot): Promise<PublicationSlot> {
  if (!isPublishingActivation(slot)) return { ...slot, publication: null };
  try {
    return { ...slot, publication: await getPublicationRecord(context, slot.activeActivationId!) };
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError || error instanceof ApiCapabilityVersionMismatchError) throw error;
    const reason = error instanceof Error ? error.message : String(error);
    throw new Error(
      `Publication slot '${slot.slotName}' is occupied by publication '${slot.activeActivationId}', but its publication record could not be read: ${reason}`,
      { cause: error });
  }
}

/** Which slot lifecycle commands the host advertises; Studio offers only those, never a guessed route. */
export async function getPublicationSlotLifecycleSupport(context: StudioEndpointContext) {
  const [unpublish, restore] = await Promise.all([
    hasCapabilityLink(context, capabilityIds.publishing, "publication-slot-unpublish"),
    hasCapabilityLink(context, capabilityIds.publishing, "publication-slot-restore")
  ]);
  return { unpublish, restore };
}

// The lifecycle responses carry Publishing's own slot shape; Studio reloads the joined slot view instead.
export async function unpublishSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-slot-unpublish",
    { definitionId, slotName });
  return context.http.deleteJson<unknown>(path);
}

export async function restorePublicationSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-slot-restore",
    { definitionId, slotName });
  return context.http.postJson<unknown>(path, {});
}

export async function getPublicationPolicy(context: StudioEndpointContext, definitionId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-policy",
    { definitionId });
  return context.http.getJson<PublicationPolicy>(path);
}

export async function savePublicationPolicy(
  context: StudioEndpointContext,
  definitionId: string,
  request: SetPublicationPolicyRequest
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-policy",
    { definitionId });
  return context.http.putJson<PublicationPolicy>(path, request);
}

export async function startWorkflowVersionTestRun(context: StudioEndpointContext, versionId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "workflow-test-runs",
    { versionId });
  return context.http.postJson<WorkflowTestRunView>(path, {});
}

export async function startWorkflowDraftTestRun(
  context: StudioEndpointContext,
  request: StartWorkflowDraftTestRunRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.publishing, "workflow-draft-test-runs");
  const wireRequest = { ...request, state: canonicalizeStateForWire(request.state) };
  try {
    return await context.http.requestJson<WorkflowTestRunView>(path, createWorkflowExecutionRequestInit(wireRequest));
  } catch (error) {
    const rejected = parseRejectedTestRun(error);
    if (rejected) return rejected;
    throw error;
  }
}

function parseRejectedTestRun(error: unknown): WorkflowTestRunView | null {
  const payload = error && typeof error === "object" && "payload" in error
    ? (error as { payload?: unknown }).payload
    : null;
  const payloadView = parseTestRunView(payload);
  if (payloadView) return payloadView;
  if (!(error instanceof Error)) return null;
  try {
    return parseTestRunView(JSON.parse(error.message));
  } catch {
    return null;
  }
}

function parseTestRunView(value: unknown): WorkflowTestRunView | null {
  if (!value || typeof value !== "object") return null;
  const candidate = value as Partial<WorkflowTestRunView>;
  return typeof candidate.testRunId === "string" && typeof candidate.status === "string"
    ? candidate as WorkflowTestRunView
    : null;
}
