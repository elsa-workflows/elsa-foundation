import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityExecutionInspection,
  RuntimeDiagnosticsSettingsView,
  SaveRuntimeDiagnosticsSettingsRequest,
  WorkflowExecutableDetails,
  WorkflowExecutableListScope,
  WorkflowExecutableRunResponse,
  WorkflowExecutableSummary,
  WorkflowExecutionInputs,
  WorkflowInstanceDetails,
  WorkflowInstanceSummary
} from "../workflowTypes";
import { capabilityIds, getApiCapability, resolveCapabilityLink } from "./capabilities";
import { createWorkflowExecutionRequestInit } from "../workflowRunInputs";

export const runtimeKeys = {
  all: ["workflow-runtime"] as const,
  executables: ["workflow-runtime", "executables"] as const,
  instances: ["workflow-runtime", "instances"] as const,
  instance: (workflowExecutionId: string) => ["workflow-runtime", "instances", workflowExecutionId] as const,
  activityExecution: (workflowExecutionId: string, activityExecutionId: string) =>
    ["workflow-runtime", "instances", workflowExecutionId, "activity-executions", activityExecutionId] as const,
  diagnosticsSettings: ["workflow-runtime", "diagnostics", "settings"] as const
};

export function useWorkflowExecutables(context: StudioEndpointContext) {
  return useQuery({
    queryKey: runtimeKeys.executables,
    queryFn: () => listExecutables(context),
    placeholderData: previous => previous
  });
}

export function useRunWorkflowExecutable(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (artifactId: string) => runExecutable(context, artifactId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: runtimeKeys.all })
  });
}

export function useRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  return useQuery({
    queryKey: runtimeKeys.diagnosticsSettings,
    queryFn: () => getRuntimeDiagnosticsSettings(context)
  });
}

export function useSaveRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveRuntimeDiagnosticsSettingsRequest) => saveRuntimeDiagnosticsSettings(context, request),
    onSuccess: () => void queryClient.invalidateQueries({ queryKey: runtimeKeys.diagnosticsSettings })
  });
}

export interface ListExecutablesOptions {
  scope?: WorkflowExecutableListScope;
  includeRetired?: boolean;
}

function executableListQuery(options: ListExecutablesOptions) {
  const parameters = new URLSearchParams();
  if (options.scope) parameters.set("scope", {
    published: "Published",
    "test-runs": "TestRuns",
    all: "All"
  }[options.scope]);
  if (options.includeRetired) parameters.set("includeRetired", "true");
  const query = parameters.toString();
  return query ? `?${query}` : "";
}

export async function listExecutables(context: StudioEndpointContext, options: ListExecutablesOptions = {}) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-executables");
  const response = await context.http.getJson<
    { items?: WorkflowExecutableSummary[]; executables?: WorkflowExecutableSummary[] } | WorkflowExecutableSummary[]
  >(`${path}${executableListQuery(options)}`);
  if (Array.isArray(response)) return response;
  return response.items ?? response.executables ?? [];
}

export async function getExecutable(
  context: StudioEndpointContext,
  artifactId: string,
  sourceReferenceId?: string | null
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-executable",
    { artifactId });
  const query = sourceReferenceId ? `?ref=${encodeURIComponent(sourceReferenceId)}` : "";
  return context.http.getJson<WorkflowExecutableDetails>(`${path}${query}`);
}

export async function getExecutableProvenance(context: StudioEndpointContext, artifactId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-executable-provenance",
    { artifactId });
  return context.http.getJson<unknown>(path);
}

export async function runExecutable(
  context: StudioEndpointContext,
  artifactId: string,
  inputs: WorkflowExecutionInputs = {},
  sourceReferenceId?: string | null
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-execute",
    { artifactId });
  return context.http.requestJson<WorkflowExecutableRunResponse>(path, createWorkflowExecutionRequestInit({
    inputs,
    ...(sourceReferenceId ? { sourceReferenceId } : {})
  }));
}

export interface ListWorkflowInstancesRequest {
  status?: string;
  runKind?: string;
  definitionId?: string;
  correlationId?: string;
  workflowExecutionId?: string;
  artifactId?: string;
  from?: string;
  to?: string;
  take?: number;
  cursor?: string;
}

export interface WorkflowInstanceListPage {
  items: WorkflowInstanceSummary[];
  previousCursor: string | null;
  nextCursor: string | null;
  hasPrevious: boolean;
  hasNext: boolean;
  count: number;
  totalCount: number;
}

export function workflowInstanceListQuery(request: ListWorkflowInstancesRequest = {}) {
  const parameters = new URLSearchParams();
  if (request.status) parameters.set("status", request.status);
  if (request.runKind) parameters.set("runKind", request.runKind);
  if (request.definitionId) parameters.set("definitionId", request.definitionId);
  if (request.correlationId) parameters.set("correlationId", request.correlationId);
  if (request.workflowExecutionId) parameters.set("workflowExecutionId", request.workflowExecutionId);
  if (request.artifactId) parameters.set("artifactId", request.artifactId);
  if (request.from) parameters.set("from", request.from);
  if (request.to) parameters.set("to", request.to);
  if (request.take) parameters.set("take", String(request.take));
  if (request.cursor) parameters.set("cursor", request.cursor);
  return parameters.toString();
}

export async function listWorkflowInstances(context: StudioEndpointContext, request: ListWorkflowInstancesRequest = {}) {
  const capability = await getApiCapability(context, capabilityIds.runtime);
  // Runtime v1 guarantees an array at the legacy relation. The cursor envelope is additive and
  // must be explicitly advertised so Studio can keep interoperating with older Foundation hosts.
  const relation = capability?.links.some(link => link.rel === "workflow-instances-page")
    ? "workflow-instances-page"
    : "workflow-instances";
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, relation);
  const query = workflowInstanceListQuery(request);
  const url = `${path}${query ? `?${query}` : ""}`;

  if (relation === "workflow-instances") {
    const response = await context.http.getJson<WorkflowInstanceSummary[]>(url);
    if (!Array.isArray(response)) throw new Error("Legacy workflow instance relation must return an array.");
    return {
      items: response,
      previousCursor: null,
      nextCursor: null,
      hasPrevious: false,
      hasNext: false,
      count: response.length,
      totalCount: response.length
    } satisfies WorkflowInstanceListPage;
  }

  const response = await context.http.getJson<Partial<WorkflowInstanceListPage>>(url);
  if (!response || Array.isArray(response) || typeof response !== "object") {
    throw new Error("Paged workflow instance relation must return a page object.");
  }
  const items = response.items ?? [];
  return {
    items,
    previousCursor: response.previousCursor ?? null,
    nextCursor: response.nextCursor ?? null,
    hasPrevious: response.hasPrevious ?? Boolean(response.previousCursor),
    hasNext: response.hasNext ?? Boolean(response.nextCursor),
    count: response.count ?? items.length,
    totalCount: response.totalCount ?? items.length
  } satisfies WorkflowInstanceListPage;
}

export async function getWorkflowInstance(context: StudioEndpointContext, workflowExecutionId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-instance",
    { workflowExecutionId });
  return context.http.getJson<WorkflowInstanceDetails>(path);
}

export async function getActivityExecutionInspection(
  context: StudioEndpointContext,
  workflowExecutionId: string,
  activityExecutionId: string
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "activity-execution",
    { workflowExecutionId, activityExecutionId });
  return context.http.getJson<ActivityExecutionInspection>(path);
}

export async function listWorkflowIncidents(context: StudioEndpointContext, workflowExecutionId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-incidents",
    { workflowExecutionId });
  const response = await context.http.getJson<{ items?: unknown[] }>(path);
  return response.items ?? [];
}

export async function getRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "runtime-diagnostics");
  return context.http.getJson<RuntimeDiagnosticsSettingsView>(path);
}

export async function saveRuntimeDiagnosticsSettings(
  context: StudioEndpointContext,
  request: SaveRuntimeDiagnosticsSettingsRequest
) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "runtime-diagnostics");
  return context.http.putJson<RuntimeDiagnosticsSettingsView>(path, request);
}
