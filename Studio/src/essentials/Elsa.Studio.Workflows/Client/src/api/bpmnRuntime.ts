import type { StudioBackendSession, StudioRequestInit } from "@elsa-workflows/studio-sdk";
import { capabilityIds, resolveCapabilityLink, type ApiCapabilityId } from "./capabilities";

export interface BpmnPin { artifactId: string; definitionId: string; definitionVersionId: string; artifactVersion: string; artifactHash: string }
export interface BpmnPage<T> { items: T[]; nextContinuationToken: string | null }
export interface BpmnPageOptions { limit?: number; continuationToken?: string | null; artifactId?: string }
export interface BpmnPublishedModel { executable: BpmnPin; incarnationId: string; sourceReferenceId: string; profile: string }
export interface BpmnPublication {
  sourceReferenceId: string; sourceId: string; sourceVersion: string | null; executable: BpmnPin;
  materialIncarnationId: string; profile: string; displayName: string | null; createdAt: string; publishedAt: string | null;
}
export interface BpmnCommandOutcome { executionId: string; inputId: string; replay: boolean; pending: boolean; result: Record<string, unknown> | null }
export interface BpmnExecution {
  executionId: string; revision: number; status: "Running" | "WaitingIncident" | "TerminalPendingSettlement" | "TerminalSettled";
  protocol: { key: string; version: number }; executable: BpmnPin; materialIncarnationId: string;
  createdAt: string; updatedAt: string; result: Record<string, unknown> | null;
}
export interface BpmnHumanWork { elementId: string; bindingId: string; tokenId: string; iterationId: string | null; invocationId: string; attemptId: string }
export interface BpmnIncident {
  incidentId: string; elementId: string; bindingId: string; invocationId: string; failedAttemptId: string; workerExecutionId: string;
  faultCode: string; failedAt: string; attemptNumber: number; maxAttempts: number; status: "Open" | "Retried" | "Retired" | "Final"; retryAttemptId: string | null;
}
export interface BpmnInspection {
  execution: BpmnExecution;
  authority: { systemIdentity: string; rootInitiator: string; metadata: Record<string, string> };
  profile: string;
  process: Record<string, unknown>;
  source: {
    sourceReferenceId: string; sourceKind: string; sourceId: string; sourceVersion: string | null;
    createdAt: string; publishedAt: string | null; retiredAt: string | null;
    layout: { nodeId: string; x: number; y: number; width?: number | null; height?: number | null; additionalProperties?: unknown }[];
    activityPresentation: { executableNodeId: string; displayName?: string | null; description?: string | null }[];
  } | null;
  work: (BpmnHumanWork & { kind: "Human" | "Native" | "Timer"; workerExecutionId: string | null; dueAt: string | null })[];
  attempts: {
    bindingId: string; invocationId: string; attemptId: string; workerExecutionId: string; workerExecutable: BpmnPin;
    status: string; attemptNumber: number | null; outcomeKey: string | null; faultCode: string | null;
  }[];
  incidents: BpmnIncident[];
  settlements: { kind: "NativeCancellation" | "NativeParent"; executionId: string; invocationId: string | null; attemptId: string | null; settled: boolean; notAdmitted: boolean }[];
}

export type BpmnOperationInput =
  | { kind: "publish"; versionId: string }
  | { kind: "start"; sourceReferenceId: string }
  | { kind: "complete-human"; executionId: string; invocationId: string; attemptId: string; outcomes?: readonly string[] }
  | { kind: "cancel"; executionId: string; reason?: string }
  | { kind: "retry-service"; executionId: string; incidentId: string; invocationId: string; failedAttemptId: string };
export type BpmnOperation = BpmnOperationInput & { readonly requestId: string; readonly scope: string };
type OperationResult<T extends BpmnOperation> = T extends { kind: "publish" } ? BpmnPublishedModel : BpmnCommandOutcome;

function operationScope(session: StudioBackendSession) {
  return JSON.stringify([session.profile.id, session.profile.baseUrl, session.identity.subject ?? null, session.identity.tenantId ?? null]);
}
function required(value: string, name: string) {
  if (typeof value !== "string" || !value.trim()) throw new Error(`BPMN ${name} is required.`);
  return value;
}
function query(options: BpmnPageOptions) {
  const limit = options.limit ?? 50;
  if (!Number.isInteger(limit) || limit < 1 || limit > 500) throw new Error("BPMN page size must be between 1 and 500.");
  const values = new URLSearchParams({ limit: String(limit) });
  if (options.continuationToken != null) values.set("continuationToken", required(options.continuationToken, "continuation"));
  if (options.artifactId != null) values.set("artifactId", required(options.artifactId, "artifact identity"));
  return `?${values}`;
}

/** Bound to one SDK connection lifetime; a prepared operation can be retained and replayed by a fresh matching session. */
export function createBpmnRuntimeClient(session: StudioBackendSession) {
  const context = session.context, key = session.key, scope = operationScope(session), baseUrl = context.baseUrl;
  const assertActive = (signal?: AbortSignal) => {
    if (signal?.aborted || session.disposed || session.key !== key || session.context !== context ||
        context.baseUrl !== baseUrl || operationScope(session) !== scope) {
      throw new DOMException("The BPMN request no longer belongs to the selected backend session.", "AbortError");
    }
  };
  const path = async (capability: ApiCapabilityId, relation: string, parameters: Record<string, string>, signal?: AbortSignal) => {
    assertActive(signal);
    const value = await resolveCapabilityLink(context, capability, relation, parameters);
    assertActive(signal);
    return value;
  };
  const read = async <T>(capability: ApiCapabilityId, relation: string, parameters: Record<string, string> = {}, suffix = "", signal?: AbortSignal) => {
    const url = await path(capability, relation, parameters, signal);
    const value = await context.http.getJson<T>(url + suffix, signal ? { signal } : undefined);
    assertActive(signal);
    return value;
  };
  const prepare = <T extends BpmnOperationInput>(input: T, requestId: string = crypto.randomUUID()): Readonly<T & { requestId: string; scope: string }> => {
    assertActive(); required(requestId, "request identity");
    if (session.identity.status !== "authenticated" || !session.identity.subject) throw new Error("Sign in before preparing a BPMN command.");
    // Each operation contains only primitive identities and an optional outcome list. Never retain a mutable form object.
    const snapshot = input.kind === "complete-human" && input.outcomes
      ? { ...input, outcomes: Object.freeze([...input.outcomes]) } : { ...input };
    return Object.freeze({ ...snapshot, requestId, scope }) as Readonly<T & { requestId: string; scope: string }>;
  };
  const execute = async <T extends BpmnOperation>(operation: T, signal?: AbortSignal): Promise<OperationResult<T>> => {
    assertActive(signal);
    if (operation.scope !== scope) throw new Error("This BPMN operation belongs to another backend, account or tenant.");
    const requestId = required(operation.requestId, "request identity");
    let relation: string, parameters: Record<string, string>, body: object;
    let capability: ApiCapabilityId = capabilityIds.bpmnRuntime;
    switch (operation.kind) {
      case "publish":
        capability = capabilityIds.bpmnPublishing; relation = "bpmn-authored-publish";
        parameters = { versionId: required(operation.versionId, "version identity") }; body = { requestId }; break;
      case "start":
        relation = "bpmn-start"; parameters = {}; body = { requestId, sourceReferenceId: required(operation.sourceReferenceId, "source identity") }; break;
      case "complete-human":
        relation = "bpmn-complete-human";
        parameters = { id: required(operation.executionId, "execution identity"), invocation: required(operation.invocationId, "invocation identity") };
        body = { requestId, attemptId: required(operation.attemptId, "attempt identity"), ...(operation.outcomes ? { outcomes: [...operation.outcomes] } : {}) }; break;
      case "cancel":
        relation = "bpmn-cancel"; parameters = { id: required(operation.executionId, "execution identity") };
        body = { requestId, ...(operation.reason === undefined ? {} : { reason: operation.reason }) }; break;
      case "retry-service":
        relation = "bpmn-retry-service"; parameters = { id: required(operation.executionId, "execution identity") };
        body = { requestId, incidentId: required(operation.incidentId, "incident identity"), invocationId: required(operation.invocationId, "invocation identity"),
          failedAttemptId: required(operation.failedAttemptId, "failed attempt identity") }; break;
      default: throw new Error("The retained BPMN operation kind is unsupported.");
    }
    const url = await path(capability, relation, parameters, signal);
    const init: StudioRequestInit = { retryOnUnauthorized: false, ...(signal ? { signal } : {}) };
    const result = await context.http.postJson<OperationResult<T>>(url, body, init);
    assertActive(signal);
    return result; // Pending remains pending; a request never creates its own replacement operation or retry.
  };
  return Object.freeze({
    sessionKey: key, scope, queryKey: ["bpmn-runtime", key] as const, prepare, execute,
    publications: (options: BpmnPageOptions = {}, signal?: AbortSignal) => read<BpmnPage<BpmnPublication>>(capabilityIds.bpmnCatalog, "bpmn-publications", {}, query(options), signal),
    executions: (options: BpmnPageOptions = {}, signal?: AbortSignal) => read<BpmnPage<BpmnExecution>>(capabilityIds.bpmnCatalog, "bpmn-executions", {}, query(options), signal),
    inspection: (id: string, signal?: AbortSignal) => read<BpmnInspection>(capabilityIds.bpmnInspection, "bpmn-inspection", { id: required(id, "execution identity") }, "", signal),
    execution: (id: string, signal?: AbortSignal) => read<BpmnExecution>(capabilityIds.bpmnRuntime, "bpmn-execution", { id: required(id, "execution identity") }, "", signal),
    admission: (signal?: AbortSignal) => read<{ admitNewExecutions: boolean }>(capabilityIds.bpmnRuntime, "bpmn-admission", {}, "", signal),
    work: (id: string, options: BpmnPageOptions = {}, signal?: AbortSignal) => read<BpmnPage<BpmnHumanWork>>(capabilityIds.bpmnRuntime, "bpmn-work", { id: required(id, "execution identity") }, query(options), signal),
    incidents: (id: string, options: BpmnPageOptions = {}, signal?: AbortSignal) => read<BpmnPage<BpmnIncident>>(capabilityIds.bpmnRuntime, "bpmn-incidents", { id: required(id, "execution identity") }, query(options), signal),
    command: (id: string, requestId: string, signal?: AbortSignal) => read<BpmnCommandOutcome>(capabilityIds.bpmnRuntime, "bpmn-command", { id: required(id, "execution identity"), request: required(requestId, "request identity") }, "", signal)
  });
}
