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
import { capabilityIds, resolveCapabilityLink } from "./capabilities";

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
  _sourceReferenceId?: string | null
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-executable",
    { artifactId });
  return context.http.getJson<WorkflowExecutableDetails>(path);
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
  inputs: WorkflowExecutionInputs = {}
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.runtime,
    "workflow-execute",
    { artifactId });
  return context.http.postJson<WorkflowExecutableRunResponse>(path, { inputs });
}

export interface ListWorkflowInstancesRequest {
  status?: string;
  runKind?: string;
  definitionId?: string;
  correlationId?: string;
  take?: number;
}

export async function listWorkflowInstances(context: StudioEndpointContext, request: ListWorkflowInstancesRequest = {}) {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-instances");
  const parameters = new URLSearchParams();
  if (request.status) parameters.set("status", request.status);
  if (request.runKind) parameters.set("runKind", request.runKind);
  if (request.definitionId) parameters.set("definitionId", request.definitionId);
  if (request.correlationId) parameters.set("correlationId", request.correlationId);
  if (request.take) parameters.set("take", String(request.take));
  const query = parameters.toString();
  const response = await context.http.getJson<
    { items?: WorkflowInstanceSummary[] } | WorkflowInstanceSummary[]
  >(`${path}${query ? `?${query}` : ""}`);
  return Array.isArray(response) ? response : response.items ?? [];
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
