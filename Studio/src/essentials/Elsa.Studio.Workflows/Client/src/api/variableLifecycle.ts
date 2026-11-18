import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

// Spec 1229 (S-6): the Runtime variable lifecycle inspection reads. Each route is an advertised Runtime relation; values
// never arrive inline, only as value states with a payload reference for the permission-checked payload read.

export type VariableValueState = "captured" | "omitted" | "redacted" | "expired";

export interface VariableValuePayloadRef {
  ownerKind: "activityExecution" | "alteration";
  ownerId: string;
  entryId: string;
  value: "written" | "overwritten" | "refused";
}

export interface VariableValueStateView {
  state: VariableValueState | string;
  digest?: string | null;
  payloadRef?: VariableValuePayloadRef | null;
  isOperational: boolean;
}

export interface VariableWriterView {
  source: string;
  executableNodeId?: string | null;
  activityExecutionId?: string | null;
  logicalInvocationId?: string | null;
  attemptId?: string | null;
  branchId?: string | null;
  executionPathId?: string | null;
  alterationId?: string | null;
}

export interface VariableOverwriteView {
  overwrittenEntryId: string;
  overwrittenWriter: VariableWriterView;
  overwrittenCommitSequence?: number | null;
  isLostUpdate: boolean;
}

export type VariableWriteOutcome = "applied" | "lostUpdateWarning" | "lostUpdateFault" | "acceptedOverride";

export interface VariableWriteEntry {
  entryId: string;
  scopeId: string;
  frameId?: string | null;
  variableKey: string;
  writer: VariableWriterView;
  source: string;
  outcome: VariableWriteOutcome | string;
  baseVersion?: number | null;
  currentVersion: number;
  newVersion: number;
  commitSequence?: number | null;
  frameRevision: number;
  orderApproximate: boolean;
  isApplied: boolean;
  isCurrentValue: boolean;
  concurrentOverwrite?: VariableOverwriteView | null;
  value: VariableValueStateView;
  overwrittenValue?: VariableValueStateView | null;
  refusedValue?: VariableValueStateView | null;
  resolvesEntryId?: string | null;
  occurredAt: string;
}

export interface VariableWriteHistoryPage {
  items: VariableWriteEntry[];
  next?: string | null;
  concurrentOverwriteTotal?: number | null;
}

export interface VariableSummary {
  scopeId: string;
  frameId: string;
  variableKey: string;
  frameKind: string;
  version: number;
  lastWriter?: VariableWriterView | null;
  lastCommitSequence?: number | null;
  value: VariableValueStateView;
  historyRecorded: boolean;
}

export interface JournalTransition {
  activityExecutionId: string;
  executableNodeId: string;
  runTimePath: string;
  kind: string;
  branchId?: string | null;
  executionPathId?: string | null;
  evaluation?: number | null;
}

export interface JournalInputRefresh {
  activityExecutionId: string;
  supersededEvaluation: number;
  replacedByEvaluation: number;
  reason: string;
  changedVariables: string[];
}

export interface JournalCommit {
  commitSequence?: number | null;
  orderApproximate: boolean;
  occurredAt?: string | null;
  transitions: JournalTransition[];
  inputRefreshes: JournalInputRefresh[];
  writes: VariableWriteEntry[];
}

export interface JournalPage {
  items: JournalCommit[];
  next?: string | null;
}

export interface VariableComparison {
  framePath: string;
  variableKey: string;
  finalDigestDiffers: boolean;
  writtenDigestsDiffer: boolean;
  leftWriteCount: number;
  rightWriteCount: number;
}

export interface InstanceComparison {
  leftWorkflowExecutionId: string;
  rightWorkflowExecutionId: string;
  executable: string;
  comparable: boolean;
  refusalReason?: string | null;
  firstDivergence?: { commitIndex: number; left?: JournalCommit | null; right?: JournalCommit | null } | null;
  variables: VariableComparison[];
  partial: boolean;
}

export interface VariableWritePayload {
  entryId: string;
  value: string;
  captureMode: string;
  payload: unknown;
}

export interface VariableWriteFilter {
  frameId?: string;
  scopeId?: string;
  variableKey?: string;
  source?: string;
  outcome?: VariableWriteOutcome;
  overwrites?: boolean;
}

export interface PageRequest {
  cursor?: string | null;
  take?: number;
}

export const variableLifecycleRelations = {
  variables: "workflow-instance-variables",
  writes: "workflow-instance-variable-writes",
  payload: "workflow-instance-variable-write-payload",
  journal: "workflow-instance-journal",
  compare: "workflow-instance-compare"
} as const;

/** Whether the host advertises the variable lifecycle reads (an older host does not). */
export function supportsVariableLifecycleInspection(context: StudioEndpointContext) {
  return hasCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.writes);
}

export async function listInstanceVariables(context: StudioEndpointContext, workflowExecutionId: string, signal?: AbortSignal) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.variables, { workflowExecutionId });
  const response = await get<VariableSummary[]>(context, path, signal);
  if (!Array.isArray(response)) throw new Error("Runtime returned an invalid variable list.");
  return response;
}

export async function listVariableWrites(
  context: StudioEndpointContext,
  workflowExecutionId: string,
  filter: VariableWriteFilter = {},
  page: PageRequest = {},
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.writes, { workflowExecutionId });
  const response = await get<VariableWriteHistoryPage>(context, withQuery(path, {
    frameId: filter.frameId,
    scopeId: filter.scopeId,
    variableKey: filter.variableKey,
    source: filter.source,
    outcome: filter.outcome,
    overwrites: filter.overwrites ? "true" : undefined,
    cursor: page.cursor ?? undefined,
    take: page.take?.toString()
  }), signal);
  return requirePage(response, "write history");
}

export async function listInstanceJournal(context: StudioEndpointContext, workflowExecutionId: string, page: PageRequest = {}, signal?: AbortSignal) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.journal, { workflowExecutionId });
  const response = await get<JournalPage>(context, withQuery(path, { cursor: page.cursor ?? undefined, take: page.take?.toString() }), signal);
  return requirePage(response, "journal");
}

/**
 * Reads every page of a paged read in order, continuing from each page's cursor. A cursor that repeats would never
 * end, so it is refused.
 */
export async function readAllPages<T>(load: (cursor: string | null) => Promise<{ items: T[]; next?: string | null }>, maximumPages = 50) {
  const items: T[] = [];
  const seen = new Set<string>();
  let cursor: string | null = null;
  for (let page = 0; page < maximumPages; page++) {
    const result = await load(cursor);
    items.push(...result.items);
    if (!result.next) return { items, complete: true };
    if (seen.has(result.next)) throw new Error("Runtime returned a repeating page cursor.");
    seen.add(result.next);
    cursor = result.next;
  }
  return { items, complete: false };
}

export async function compareInstances(context: StudioEndpointContext, workflowExecutionId: string, otherWorkflowExecutionId: string, signal?: AbortSignal) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.compare, { workflowExecutionId, otherWorkflowExecutionId });
  try {
    return await get<InstanceComparison>(context, path, signal);
  } catch (error) {
    // A refused pair answers 409 with the comparison view and its reason (StudioHttpError.payload).
    const failure = error as { status?: number; payload?: unknown };
    if (failure?.status === 409 && isComparison(failure.payload)) return failure.payload;
    throw error;
  }
}

/** Resolves one value of a write-history entry through the permission-checked payload relation. */
export async function getVariableWritePayload(
  context: StudioEndpointContext,
  workflowExecutionId: string,
  reference: Pick<VariableValuePayloadRef, "entryId" | "value">,
  signal?: AbortSignal
) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, variableLifecycleRelations.payload, { workflowExecutionId, entryId: reference.entryId });
  const response = await get<VariableWritePayload>(context, withQuery(path, { value: reference.value }), signal);
  if (!response || typeof response !== "object" || response.entryId !== reference.entryId || response.value !== reference.value) {
    throw new Error("Runtime returned a value for a different write.");
  }
  if (!Object.hasOwn(response, "payload")) throw new Error("Runtime value response did not include a payload.");
  return response;
}

function withQuery(path: string, query: Record<string, string | undefined>) {
  const parameters = Object.entries(query)
    .filter((entry): entry is [string, string] => entry[1] !== undefined && entry[1] !== "")
    .map(([name, value]) => `${encodeURIComponent(name)}=${encodeURIComponent(value)}`);
  return parameters.length === 0 ? path : `${path}?${parameters.join("&")}`;
}

function get<T>(context: StudioEndpointContext, path: string, signal?: AbortSignal) {
  return signal ? context.http.getJson<T>(path, { signal }) : context.http.getJson<T>(path);
}

function requirePage<T extends { items: unknown[] }>(response: T, name: string) {
  if (!response || typeof response !== "object" || !Array.isArray(response.items)) {
    throw new Error(`Runtime returned an invalid ${name} page.`);
  }
  return response;
}

function isComparison(value: unknown): value is InstanceComparison {
  return !!value && typeof value === "object" && "comparable" in value && "variables" in value;
}
