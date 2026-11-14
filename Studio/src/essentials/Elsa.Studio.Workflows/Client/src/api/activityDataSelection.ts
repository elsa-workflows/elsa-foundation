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

export interface ActivityDataType {
  alias: string;
  collectionKind: unknown;
}

/** Metadata only: null means unknown, never non-nullable or unprotected. Names are literal JSON keys. */
export interface ActivityDataShape {
  state: "known" | "unknown" | "truncated" | "unavailable";
  kind: "unknown" | "scalar" | "object" | "map" | "collection";
  type: ActivityDataType | null;
  sourceContractRevision: string | null;
  profileRevision: string | null;
  metadataRevision: string | null;
  isNullable: boolean | null;
  presence: "unknown" | "required" | "optional";
  representation: string | null;
  isSensitive: boolean | null;
  isCredential: boolean | null;
  reasonCode: string | null;
  members: { serializedName: string; displayName: string; shape: ActivityDataShape }[];
  element: ActivityDataShape | null;
  mapValue: ActivityDataShape | null;
}

/** A focused query always names a server-published whole-source binding; the server constructs its member form. */
export interface ActivityDataSourceQuery {
  source?: ActivityDataBinding;
  memberPath?: readonly string[];
  includeMemberShapes?: boolean;
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
  type: ActivityDataType | null;
  scopeId?: string | null;
  shape?: ActivityDataShape | null;
}

export interface ActivityDataInputConstraint {
  owner: string;
  reasonCode?: string | null;
  allowedExpressionTypes: string[];
  message: string;
}

export interface ActivityDataTargetFacts {
  displayName: string;
  target?: ActivityDataTarget | null;
  type?: ActivityDataType | null;
  isNullable?: boolean | null;
  isRequired?: boolean | null;
  isStructureOwned?: boolean | null;
  isSensitive?: boolean | null;
  isCredential?: boolean | null;
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
  /** Safe findings may explain an invalid answer without making its candidates selectable. */
  findings?: { path: string | null; type: string | null; message: string | null }[];
  /** Only literal true in a supported/empty analysis enables member selection. */
  memberReadsEnabled?: boolean;
  resourceKind?: string | null;
  resourceId?: string | null;
  snapshotHash?: string | null;
  contextRevision?: string | null;
  contractRevision?: string | null;
}

/**
 * Preserve the existing whole-output browser request. Member discovery opts into the server's default bounded
 * shape budget separately; every query sends its member path explicitly.
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
  /** The first source of the page; pages are 100 sources. */
  skip?: number;
  query?: ActivityDataSourceQuery;
}

export interface ActivityDefinitionDataRequest {
  draftId: string;
  /** The acknowledged draft revision. */
  expectedBaseRevision: number;
  documentRevision: string;
  target: ActivityDataTarget;
  /** The current Activity Definition draft state envelope; analyzed without being saved. */
  currentSnapshot: unknown;
  /** The first source of the page; pages are 100 sources. */
  skip?: number;
  query?: ActivityDataSourceQuery;
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
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  const query = sourceQuery(request);
  const path = await resolveCapabilityLink(context, capabilityIds.activityDataSelection, workflowDataContextRelation);
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  const response = await context.http.postJson<unknown>(path, {
    contractVersion: "1",
    definitionId: request.definitionId,
    draftId: request.draftId,
    expectedBaseRevision: request.expectedBaseRevision,
    documentRevision: request.documentRevision,
    nodeId: request.target.nodeId,
    inputKey: request.target.inputKey,
    snapshot: request.snapshot,
    query
  }, { signal });
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  return readRequestedResult(response, request, query, "WorkflowDefinition");
}

export async function requestActivityDefinitionActivityData(
  context: StudioEndpointContext,
  request: ActivityDefinitionDataRequest,
  signal?: AbortSignal
): Promise<ActivityDataContextResult> {
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  const query = sourceQuery(request);
  const path = await resolveCapabilityLink(context, capabilityIds.activityDataSelection, activityDefinitionDataContextRelation, {
    draftId: request.draftId
  });
  // The endpoint refuses unknown members, so the body carries exactly its declared fields.
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  const response = await context.http.postJson<unknown>(path, {
    contractVersion: "1",
    expectedBaseRevision: request.expectedBaseRevision,
    documentRevision: request.documentRevision,
    target: request.target,
    query,
    currentSnapshot: request.currentSnapshot
  }, { signal });
  if (signal?.aborted) throw new DOMException("The request was aborted.", "AbortError");
  return readRequestedResult(response, request, query, "ActivityDefinition");
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
  if (state !== "supported" && state !== "empty" && state !== "invalid") return result;
  const analysis = asRecord(record.analysis);
  result.resourceKind = readString(record.resourceKind);
  result.resourceId = readString(record.resourceId);
  result.snapshotHash = readString(record.snapshotHash);
  result.contextRevision = readString(record.contextRevision);
  result.contractRevision = readString(analysis?.contractRevision);

  result.findings = Array.isArray(analysis?.findings) ? analysis.findings.slice(0, 128).flatMap(value => {
    const finding = asRecord(value);
    return finding ? [{ path: readString(finding.path), type: readString(finding.type), message: readString(finding.message) }] : [];
  }) : [];
  // Invalid answers may explain their refusal, but neither their sources nor their capability flag are offers.
  if (state === "invalid") return result;
  result.memberReadsEnabled = analysis?.memberReadsEnabled === true;
  result.target = readTarget(asRecord(analysis?.target));
  const budget = { members: memberShapeLimits.maximumMembers };
  result.sources = Array.isArray(analysis?.sources) ? analysis.sources.slice(0, 100).flatMap(source => readSource(source, budget)) : [];
  result.hasMoreSources = analysis?.hasMoreSources === true || (Array.isArray(analysis?.sources) && analysis.sources.length > 100);
  return result;
}

function emptyResult(state: ActivityDataContextState, code: string | null, documentRevision: string): ActivityDataContextResult {
  return { state, code, documentRevision, baseRevision: null, target: null, sources: [], hasMoreSources: false,
    memberReadsEnabled: false, findings: [], resourceKind: null, resourceId: null, snapshotHash: null, contextRevision: null, contractRevision: null };
}

function readTarget(target: Record<string, unknown> | null): ActivityDataTargetFacts | null {
  if (!target) return null;
  const constraint = asRecord(target.constraint);
  return {
    displayName: readString(target.displayName) ?? "",
    target: readTargetIdentity(target.target),
    type: readType(target.type),
    isNullable: readBoolean(target.isNullable),
    isRequired: readBoolean(target.isRequired),
    isStructureOwned: readBoolean(target.isStructureOwned),
    isSensitive: readBoolean(target.isSensitive),
    isCredential: readBoolean(target.isCredential),
    constraint: constraint ? {
      owner: readString(constraint.owner) ?? "",
      reasonCode: readString(constraint.reasonCode),
      allowedExpressionTypes: Array.isArray(constraint.allowedExpressionTypes)
        ? constraint.allowedExpressionTypes.filter((type): type is string => typeof type === "string")
        : [],
      message: readString(constraint.message) ?? ""
    } : null
  };
}

function readSource(value: unknown, budget: { members: number }): ActivityDataSourceFacts[] {
  const source = asRecord(value);
  const id = readString(source?.id);
  if (!source || !id) return [];
  const binding = asRecord(source.binding);
  const producer = asRecord(source.activityResultSource);
  const producerNodeId = readString(producer?.producerNodeId);
  const projectionKey = readString(producer?.projectionKey);
  const shape = readShape(source.shape, budget, 0);
  return [{
    id,
    displayName: readString(source.displayName) ?? id,
    binding: binding && typeof binding.expressionType === "string" && "value" in binding
      ? { expressionType: binding.expressionType, value: binding.value }
      : null,
    reasonCode: readString(source.reasonCode),
    availability: readString(source.availability),
    activityResultSource: producerNodeId && projectionKey ? { producerNodeId, projectionKey } : null,
    type: shape?.type ?? null,
    scopeId: readString(source.scopeId),
    shape
  }];
}

function asRecord(value: unknown): Record<string, unknown> | null {
  return value && typeof value === "object" && !Array.isArray(value) ? value as Record<string, unknown> : null;
}

function readString(value: unknown): string | null {
  return typeof value === "string" && value.length > 0 ? value : null;
}


/** The server's default shape bounds; shared across the complete response, not reset at each member. */
export const memberShapeLimits = { maximumDepth: 4, maximumMembers: 128 } as const;

function sourceQuery(request: { skip?: number; query?: ActivityDataSourceQuery }) {
  const options = request.query;
  const memberPath = [...options?.memberPath ?? []];
  if (memberPath.length > 64 || memberPath.some(segment => typeof segment !== "string") ||
    memberPath.reduce((length, segment) => length + segment.length, 0) > 4096 || (memberPath.length > 0 && !options?.source)) {
    throw new TypeError("A bounded literal member path requires a focused source.");
  }
  return {
    ...wholeValueSourceQuery,
    skip: request.skip ?? 0,
    memberPath,
    ...(options?.source ? { source: options.source } : {}),
    shapeLimits: options?.includeMemberShapes || options?.source ? memberShapeLimits : wholeValueSourceQuery.shapeLimits
  };
}

function readRequestedResult(
  response: unknown,
  request: { draftId: string; documentRevision: string; expectedBaseRevision: string | number; target: ActivityDataTarget },
  query: ReturnType<typeof sourceQuery>,
  resourceKind: string
): ActivityDataContextResult {
  const result = readActivityDataContextResult(response, request.documentRevision);
  if (result.state !== "supported" && result.state !== "empty" && result.state !== "invalid") return result;
  const target = result.target?.target;
  if (result.resourceId !== request.draftId || result.resourceKind !== resourceKind ||
    result.baseRevision !== String(request.expectedBaseRevision) || (result.sources.length > 0 && !target) || (target &&
    (target.nodeId !== request.target.nodeId || target.inputKey !== request.target.inputKey))) {
    return emptyResult("incompatible", "activity-data.response-unreadable", request.documentRevision);
  }
  if (result.state === "invalid") return result;
  if (query.memberPath.length > 0 && (!result.memberReadsEnabled ||
    result.sources.some(source => source.binding && !matchesMemberOffer(query.source!, query.memberPath, source.binding)))) {
    return emptyResult("incompatible", "activity-data.member-response-unreadable", request.documentRevision);
  }
  return result;
}

export function matchesMemberOffer(source: ActivityDataBinding, path: readonly string[], offer: ActivityDataBinding): boolean {
  const value = asRecord(offer.value);
  const original = asRecord(source.value);
  if (!value || !original || !Array.isArray(value.memberPath) || value.memberPath.length !== path.length ||
    !value.memberPath.every((segment, index) => segment === path[index])) return false;
  switch (source.expressionType) {
    case "ActivityResult":
      return offer.expressionType === "ActivityResultMember" && original.producerScopeId == null && value.producerScopeId == null &&
        !!readString(original.producerNodeId) && !!readString(original.projectionKey) &&
        value.producerNodeId === original.producerNodeId && value.projectionKey === original.projectionKey &&
        (value.isOptional === true) === (original.isOptional === true);
    case "Variable":
      return offer.expressionType === "VariableMember" && !!readString(original.referenceKey) && value.referenceKey === original.referenceKey &&
        (value.declaringScopeId ?? null) === (original.declaringScopeId ?? null);
    case "WorkflowRequest":
      return offer.expressionType === "WorkflowRequestMember" && original.path == null && !!readString(original.memberKey) && value.memberKey === original.memberKey;
    case "Input":
      return offer.expressionType === "InputMember" && !!readString(original.referenceKey) && value.referenceKey === original.referenceKey;
    default: return false;
  }
}

function readShape(value: unknown, budget: { members: number }, depth: number): ActivityDataShape | null {
  const shape = asRecord(value);
  if (!shape) return null;
  const state = enumValue(shape.state, ["known", "unknown", "truncated", "unavailable"] as const, "unknown");
  const result: ActivityDataShape = {
    state, kind: enumValue(shape.kind, ["unknown", "scalar", "object", "map", "collection"] as const, "unknown"),
    type: readType(shape.type),
    sourceContractRevision: readString(shape.sourceContractRevision), profileRevision: readString(shape.profileRevision),
    metadataRevision: readString(shape.metadataRevision), isNullable: readBoolean(shape.isNullable),
    presence: enumValue(shape.presence, ["unknown", "required", "optional"] as const, "unknown"),
    representation: readString(shape.representation), isSensitive: readBoolean(shape.isSensitive),
    isCredential: readBoolean(shape.isCredential), reasonCode: readString(shape.reasonCode), members: [], element: null, mapValue: null
  };
  const members = Array.isArray(shape.members) ? shape.members : [];
  if (depth >= memberShapeLimits.maximumDepth) {
    if (members.length || shape.element || shape.mapValue) result.state = "truncated";
    return result;
  }
  for (const item of members) {
    if (budget.members <= 0) { result.state = "truncated"; break; }
    budget.members--;
    const member = asRecord(item);
    const child = readShape(member?.shape, budget, depth + 1);
    if (member && typeof member.serializedName === "string" && child) result.members.push({
      serializedName: member.serializedName, displayName: readString(member.displayName) ?? member.serializedName, shape: child
    });
    else result.state = "truncated";
  }
  result.element = readShape(shape.element, budget, depth + 1);
  result.mapValue = readShape(shape.mapValue, budget, depth + 1);
  return result;
}

function readType(value: unknown): ActivityDataType | null {
  const type = asRecord(value);
  return type && typeof type.alias === "string" ? { alias: type.alias, collectionKind: type.collectionKind } : null;
}

function readTargetIdentity(value: unknown): ActivityDataTarget | null {
  const target = asRecord(value);
  return target && typeof target.nodeId === "string" && typeof target.inputKey === "string"
    ? { nodeId: target.nodeId, inputKey: target.inputKey } : null;
}

function readBoolean(value: unknown): boolean | null { return typeof value === "boolean" ? value : null; }

function enumValue<T extends string>(value: unknown, values: readonly T[], fallback: T): T {
  return typeof value === "string" && values.includes(value as T) ? value as T : fallback;
}
