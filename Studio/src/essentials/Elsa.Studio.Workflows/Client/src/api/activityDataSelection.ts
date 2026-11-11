import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowDefinitionState } from "../workflowTypes";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";

/**
 * The language-neutral activity data source client (spec 1215 T018). One capability, `elsa.api.activity-data-selection`
 * v1, advertises one relation per resource kind; each has its own request shape, and both answer with the same
 * metadata-only analysis. Nothing here evaluates an expression, runs a producer or saves a draft: a request carries the
 * author's current document as a snapshot and the server analyzes it without writing anything.
 */

export const workflowDataContextRelation = "workflow-data-context";
export const activityDefinitionDataContextRelation = "activity-data-context";

/** The consuming input: a node of the analyzed document and the input's stable reference key. */
export interface ActivityDataTarget {
  nodeId: string;
  inputKey: string;
}

/** An authored source binding as the server returns it. The client stores it unchanged and never edits one. */
export interface ActivityDataBinding {
  expressionType: string;
  value: unknown;
}

/** The producer identity of an activity-result source, present whether or not a direct binding is offered. */
export interface ActivityResultSourceIdentity {
  producerNodeId: string;
  projectionKey: string;
}

export interface ActivityDataSourceFacts {
  /** Server-assigned stable identity; never a display label. */
  id: string;
  displayName: string;
  /** Selectable exactly when non-null. */
  binding: ActivityDataBinding | null;
  /** Why no binding is offered (or, for an optional read, extra information). */
  reasonCode: string | null;
  availability: string | null;
  activityResultSource: ActivityResultSourceIdentity | null;
  /** The declared type of the value, when known. */
  type: { alias: string; collectionKind: unknown } | null;
}

export interface ActivityDataInputConstraint {
  owner: string;
  allowedExpressionTypes: string[];
  message: string;
}

export interface ActivityDataTargetFacts {
  displayName: string;
  constraint: ActivityDataInputConstraint | null;
}

export type ActivityDataContextState =
  | "supported"
  | "empty"
  | "denied"
  | "stale"
  | "unavailable"
  | "incompatible"
  | "invalid";

export interface ActivityDataContextResult {
  state: ActivityDataContextState;
  /** A safe stable code such as `activity-data.base-changed`; never a source path or exception text. */
  code: string | null;
  /** Echo of the request's document revision: a result answers only that document. */
  documentRevision: string;
  /** The server's current base revision, when it disclosed one. */
  baseRevision: string | null;
  target: ActivityDataTargetFacts | null;
  sources: ActivityDataSourceFacts[];
  hasMoreSources: boolean;
}

/**
 * Bounded whole-value browsing. Member shapes are not requested: member selection is not authored here, so
 * depth and member budgets are zero and the server returns each source's root shape only. Every field is sent,
 * including the empty member path: the workflow endpoint refuses a query that omits it.
 */
export const wholeValueSourceQuery = {
  includeSources: true,
  skip: 0,
  take: 100,
  memberPath: [],
  shapeLimits: { maximumDepth: 0, maximumMembers: 0 }
} as const;

export interface WorkflowActivityDataRequest {
  definitionId: string;
  draftId: string;
  /** The acknowledged draft's authoring revision, as the draft endpoints returned it. */
  expectedBaseRevision: string;
  documentRevision: string;
  target: ActivityDataTarget;
  /** The current canonical (wire-shaped) authored state; analyzed without being saved. */
  snapshot: WorkflowDefinitionState;
}

export interface ActivityDefinitionDataRequest {
  draftId: string;
  /** The acknowledged draft revision. */
  expectedBaseRevision: number;
  documentRevision: string;
  target: ActivityDataTarget;
  /** The current Activity Definition draft state envelope; analyzed without being saved. */
  currentSnapshot: unknown;
}

/** Whether the backend can analyze workflow drafts for data selection; false on servers without the capability. */
export function hasWorkflowActivityDataSelection(context: StudioEndpointContext): Promise<boolean> {
  return hasCapabilityLink(context, capabilityIds.activityDataSelection, workflowDataContextRelation);
}

export async function requestWorkflowActivityData(
  context: StudioEndpointContext,
  request: WorkflowActivityDataRequest,
  signal?: AbortSignal
): Promise<ActivityDataContextResult> {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDataSelection, workflowDataContextRelation);
  const response = await context.http.postJson<unknown>(path, {
    contractVersion: "1",
    definitionId: request.definitionId,
    draftId: request.draftId,
    expectedBaseRevision: request.expectedBaseRevision,
    documentRevision: request.documentRevision,
    nodeId: request.target.nodeId,
    inputKey: request.target.inputKey,
    snapshot: request.snapshot,
    query: wholeValueSourceQuery
  }, { signal });
  return readActivityDataContextResult(response, request.documentRevision);
}

export async function requestActivityDefinitionActivityData(
  context: StudioEndpointContext,
  request: ActivityDefinitionDataRequest,
  signal?: AbortSignal
): Promise<ActivityDataContextResult> {
  const path = await resolveCapabilityLink(context, capabilityIds.activityDataSelection, activityDefinitionDataContextRelation, {
    draftId: request.draftId
  });
  // The endpoint refuses unknown members, so the body carries exactly its declared fields.
  const response = await context.http.postJson<unknown>(path, {
    contractVersion: "1",
    expectedBaseRevision: request.expectedBaseRevision,
    documentRevision: request.documentRevision,
    target: request.target,
    query: wholeValueSourceQuery,
    currentSnapshot: request.currentSnapshot
  }, { signal });
  return readActivityDataContextResult(response, request.documentRevision);
}

const knownStates = new Set<ActivityDataContextState>(["supported", "empty", "denied", "stale", "unavailable", "incompatible", "invalid"]);

/**
 * Normalizes a response defensively. An unknown state, a contract other than v1 or a response for another document
 * becomes `incompatible` and discloses nothing, so a malformed answer can never be presented as current facts.
 */
export function readActivityDataContextResult(response: unknown, requestedDocumentRevision: string): ActivityDataContextResult {
  const record = asRecord(response);
  const state = typeof record?.state === "string" ? record.state : "";
  const documentRevision = typeof record?.documentRevision === "string" ? record.documentRevision : "";
  if (!record || record.contractVersion !== "1" || !knownStates.has(state as ActivityDataContextState) ||
    documentRevision !== requestedDocumentRevision) {
    return emptyResult("incompatible", "activity-data.response-unreadable", requestedDocumentRevision);
  }
  const result = emptyResult(state as ActivityDataContextState, readString(record.code), documentRevision);
  result.baseRevision = readString(record.baseRevision);
  if (state !== "supported" && state !== "empty") return result;
  const analysis = asRecord(record.analysis);
  result.target = readTarget(asRecord(analysis?.target));
  result.sources = Array.isArray(analysis?.sources) ? analysis.sources.flatMap(readSource) : [];
  result.hasMoreSources = analysis?.hasMoreSources === true;
  return result;
}

function emptyResult(state: ActivityDataContextState, code: string | null, documentRevision: string): ActivityDataContextResult {
  return { state, code, documentRevision, baseRevision: null, target: null, sources: [], hasMoreSources: false };
}

function readTarget(target: Record<string, unknown> | null): ActivityDataTargetFacts | null {
  if (!target) return null;
  const constraint = asRecord(target.constraint);
  return {
    displayName: readString(target.displayName) ?? "",
    constraint: constraint ? {
      owner: readString(constraint.owner) ?? "",
      allowedExpressionTypes: Array.isArray(constraint.allowedExpressionTypes)
        ? constraint.allowedExpressionTypes.filter((type): type is string => typeof type === "string")
        : [],
      message: readString(constraint.message) ?? ""
    } : null
  };
}

function readSource(value: unknown): ActivityDataSourceFacts[] {
  const source = asRecord(value);
  const id = readString(source?.id);
  if (!source || !id) return [];
  const binding = asRecord(source.binding);
  const producer = asRecord(source.activityResultSource);
  const producerNodeId = readString(producer?.producerNodeId);
  const projectionKey = readString(producer?.projectionKey);
  const type = asRecord(asRecord(source.shape)?.type);
  return [{
    id,
    displayName: readString(source.displayName) ?? id,
    binding: binding && typeof binding.expressionType === "string" && "value" in binding
      ? { expressionType: binding.expressionType, value: binding.value }
      : null,
    reasonCode: readString(source.reasonCode),
    availability: readString(source.availability),
    activityResultSource: producerNodeId && projectionKey ? { producerNodeId, projectionKey } : null,
    type: type && typeof type.alias === "string" ? { alias: type.alias, collectionKind: type.collectionKind } : null
  }];
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}
