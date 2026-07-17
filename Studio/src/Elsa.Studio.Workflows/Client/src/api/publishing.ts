import type { StudioActivityDiagnostic, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire } from "../activityInputWire";
import type {
  DesignMetadataRecord,
  StartWorkflowDraftTestRunRequest,
  WorkflowDefinitionState,
  WorkflowTestRunView
} from "../workflowTypes";
import { capabilityIds, resolveCapabilityLink } from "./capabilities";
import { createWorkflowExecutionRequestInit } from "../workflowRunInputs";

export interface PublicationIntent {
  slotName?: string;
  action?: "replace" | "sideBySide";
  expectedPublicationId?: string | null;
  preflightToken?: string;
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
  sourceReferenceId: string;
  status: PublicationStatus;
  createdAt?: string;
  activatedAt?: string | null;
  retiredAt?: string | null;
  artifactVersion?: string;
  artifactHash?: string;
  rootActivityId?: string;
  nodeCount?: number;
}

export interface PublicationSlot {
  slotId?: string;
  definitionId: string;
  slotName: string;
  activePublicationId?: string | null;
  revision?: number;
  updatedAt?: string;
  status: PublicationStatus | null;
  publication?: Publication | null;
}

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

async function publicationSlotsPath(context: StudioEndpointContext, definitionId: string) {
  return resolveCapabilityLink(
    context,
    capabilityIds.publishing,
    "publication-slots",
    { definitionId });
}

export async function listPublicationSlots(context: StudioEndpointContext, definitionId: string) {
  const response = await context.http.getJson<{ items: PublicationSlot[] }>(
    await publicationSlotsPath(context, definitionId));
  return response.items ?? [];
}

export async function getPublicationSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  return context.http.getJson<PublicationSlot>(
    `${await publicationSlotsPath(context, definitionId)}/${encodeURIComponent(slotName)}`);
}

export async function unpublishSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  return context.http.deleteJson<PublicationSlot>(
    `${await publicationSlotsPath(context, definitionId)}/${encodeURIComponent(slotName)}`);
}

export async function restorePublicationSlot(context: StudioEndpointContext, definitionId: string, slotName: string) {
  return context.http.postJson<PublicationSlot>(
    `${await publicationSlotsPath(context, definitionId)}/${encodeURIComponent(slotName)}/restore`,
    {});
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
