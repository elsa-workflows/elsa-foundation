import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import type {
  ActivityCatalogResponse,
  ActivityDescriptor,
  ActivityDescriptorsResponse,
  CreateDefinitionRequest,
  DefinitionListState,
  ExpressionDescriptor,
  ExpressionDescriptorsResponse,
  PromoteDraftResponse,
  PublishedWorkflowResponse,
  StartWorkflowDraftTestRunRequest,
  WorkflowInstanceDetails,
  WorkflowInstanceSummary,
  WorkflowExecutableRunResponse,
  WorkflowExecutableSummary,
  WorkflowDefinitionDetails,
  WorkflowDefinitionVersionDetails,
  WorkflowDefinitionsResponse,
  WorkflowDraft,
  WorkflowTestRunView
} from "../workflowTypes";
const basePath = "/_elsa/workflow-management";
const publishingBasePath = "/publishing";

export const workflowKeys = {
  all: ["workflows"] as const,
  definitions: ["workflows", "definitions"] as const,
  definitionsList: (request: DefinitionListRequest) => [...workflowKeys.definitions, "list", request] as const,
  definition: (definitionId: string) => [...workflowKeys.definitions, "detail", definitionId] as const,
  version: (versionId: string) => ["workflows", "versions", versionId] as const,
  executables: (definitionId?: string | null) => ["workflows", "executables", definitionId ?? "all"] as const,
  instances: ["workflows", "instances"] as const,
  instance: (workflowExecutionId: string) => ["workflows", "instances", workflowExecutionId] as const,
  activities: ["workflows", "activities"] as const,
  activityDescriptors: ["workflows", "activity-descriptors"] as const,
  expressionDescriptors: ["workflows", "expression-descriptors"] as const
};

export function useWorkflowDefinitions(context: StudioEndpointContext, request: DefinitionListRequest) {
  return useQuery({
    queryKey: workflowKeys.definitionsList(request),
    queryFn: () => listDefinitions(context, request),
    placeholderData: previous => previous
  });
}

export function useWorkflowDefinition(context: StudioEndpointContext, definitionId: string | null | undefined) {
  return useQuery({
    queryKey: workflowKeys.definition(definitionId ?? ""),
    enabled: !!definitionId,
    queryFn: () => getDefinition(context, definitionId!)
  });
}

export function useWorkflowExecutables(context: StudioEndpointContext, definitionId?: string | null) {
  return useQuery({
    queryKey: workflowKeys.executables(definitionId),
    queryFn: async () => {
      const executables = await listExecutables(context);
      return definitionId
        ? executables.filter(executable => executable.definitionId === definitionId || executable.sourceId === definitionId)
        : executables;
    },
    placeholderData: previous => previous
  });
}

export function useWorkflowActivities(context: StudioEndpointContext) {
  return useQuery({
    queryKey: workflowKeys.activities,
    queryFn: () => listActivities(context),
    staleTime: 60_000
  });
}

export function useCreateWorkflowDefinition(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateDefinitionRequest) => createDefinition(context, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workflowKeys.all })
  });
}

export function useDeleteWorkflowDefinition(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (definitionId: string) => deleteDefinition(context, definitionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workflowKeys.all })
  });
}

export function useRunWorkflowExecutable(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (artifactId: string) => runExecutable(context, artifactId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workflowKeys.all })
  });
}

export interface DefinitionListRequest {
  search: string;
  state?: DefinitionListState;
  page: number;
  pageSize: number;
}

export async function listDefinitions(context: StudioEndpointContext, request: DefinitionListRequest) {
  const parameters = new URLSearchParams({
    state: request.state ?? "active",
    page: request.page.toString(),
    pageSize: request.pageSize.toString()
  });
  const search = request.search.trim();

  if (search) parameters.set("search", search);
  return context.http.getJson<WorkflowDefinitionsResponse>(`${basePath}/definitions?${parameters.toString()}`);
}

export async function getDefinition(context: StudioEndpointContext, definitionId: string) {
  const details = await context.http.getJson<WorkflowDefinitionDetails>(`${basePath}/definitions/${encodeURIComponent(definitionId)}`);
  return details.draft
    ? { ...details, draft: { ...details.draft, state: expandStateFromWire(details.draft.state) } }
    : details;
}

export async function getWorkflowDefinitionVersion(context: StudioEndpointContext, versionId: string) {
  const details = await context.http.getJson<WorkflowDefinitionVersionDetails>(`${basePath}/versions/${encodeURIComponent(versionId)}`);
  return { ...details, state: expandStateFromWire(details.state) };
}

export async function createDefinition(context: StudioEndpointContext, request: CreateDefinitionRequest) {
  return context.http.postJson<WorkflowDefinitionDetails>(`${basePath}/definitions`, request);
}

export async function deleteDefinition(context: StudioEndpointContext, definitionId: string) {
  await context.http.deleteJson<unknown>(`${basePath}/definitions/${encodeURIComponent(definitionId)}`);
}

export async function restoreDefinition(context: StudioEndpointContext, definitionId: string) {
  await context.http.postJson<unknown>(`${basePath}/definitions/${encodeURIComponent(definitionId)}/restore`, {});
}

export async function deleteDefinitionPermanently(context: StudioEndpointContext, definitionId: string) {
  await context.http.deleteJson<unknown>(`${basePath}/definitions/${encodeURIComponent(definitionId)}/permanent`);
}

export async function updateDraft(context: StudioEndpointContext, draft: WorkflowDraft) {
  const saved = await context.http.putJson<WorkflowDraft>(
    `${basePath}/drafts/${encodeURIComponent(draft.id)}`,
    { state: canonicalizeStateForWire(draft.state), layout: draft.layout });
  return { ...saved, state: expandStateFromWire(saved.state) };
}

export async function promoteDraft(context: StudioEndpointContext, draftId: string) {
  return context.http.postJson<PromoteDraftResponse>(`${basePath}/drafts/${encodeURIComponent(draftId)}/promote`, {});
}

export async function publishVersion(context: StudioEndpointContext, versionId: string) {
  return context.http.postJson<PublishedWorkflowResponse>(`${basePath}/versions/${encodeURIComponent(versionId)}/publish`, {});
}

export async function startWorkflowDraftTestRun(context: StudioEndpointContext, request: StartWorkflowDraftTestRunRequest) {
  const wireRequest = { ...request, state: canonicalizeStateForWire(request.state) };
  try {
    return await context.http.postJson<WorkflowTestRunView>(`${publishingBasePath}/workflows/drafts/test-runs`, wireRequest);
  } catch (error) {
    const rejected = parseRejectedTestRun(error);
    if (rejected) return rejected;
    throw error;
  }
}

export async function runExecutable(context: StudioEndpointContext, artifactId: string) {
  return context.http.postJson<WorkflowExecutableRunResponse>(`${basePath}/executables/${encodeURIComponent(artifactId)}/run`, {});
}

export async function listExecutables(context: StudioEndpointContext) {
  return context.http.getJson<WorkflowExecutableSummary[]>("/_demo/workflows/executables");
}

export interface ListWorkflowInstancesRequest {
  status?: string;
  runKind?: string;
  definitionId?: string;
  correlationId?: string;
  take?: number;
}

export async function listWorkflowInstances(context: StudioEndpointContext, request: ListWorkflowInstancesRequest = {}) {
  const parameters = new URLSearchParams();
  if (request.status) parameters.set("status", request.status);
  if (request.runKind) parameters.set("runKind", request.runKind);
  if (request.definitionId) parameters.set("definitionId", request.definitionId);
  if (request.correlationId) parameters.set("correlationId", request.correlationId);
  if (request.take) parameters.set("take", String(request.take));

  const query = parameters.toString();
  return context.http.getJson<WorkflowInstanceSummary[]>(`/runtime/workflows/instances${query ? `?${query}` : ""}`);
}

export async function getWorkflowInstance(context: StudioEndpointContext, workflowExecutionId: string) {
  return context.http.getJson<WorkflowInstanceDetails>(`/runtime/workflows/instances/${encodeURIComponent(workflowExecutionId)}`);
}

export async function listActivities(context: StudioEndpointContext) {
  return context.http.getJson<ActivityCatalogResponse>(`${basePath}/activities`);
}

export async function listActivityDescriptors(context: StudioEndpointContext): Promise<ActivityDescriptor[]> {
  const response = await getFirstAvailable<ActivityDescriptorsResponse | ActivityDescriptor[]>(context, [
    `${basePath}/descriptors/activities`,
    "/descriptors/activities"
  ]);
  if (Array.isArray(response)) return normalizeActivityDescriptors(response);
  return normalizeActivityDescriptors(response.items ?? response.activities ?? response.descriptors ?? []);
}

export async function listExpressionDescriptors(context: StudioEndpointContext): Promise<ExpressionDescriptor[]> {
  const response = await getFirstAvailable<ExpressionDescriptorsResponse | ExpressionDescriptor[]>(context, [
    `${basePath}/descriptors/expression-descriptors`,
    "/descriptors/expression-descriptors"
  ]);
  if (Array.isArray(response)) return response;
  const descriptors = response.items ?? response.descriptors ?? response.expressionDescriptors ?? [];
  return descriptors.length > 0 ? descriptors : fallbackExpressionDescriptors;
}

async function getFirstAvailable<T>(context: StudioEndpointContext, paths: string[]) {
  let lastError: unknown;
  for (const path of paths) {
    try {
      return await context.http.getJson<T>(path);
    } catch (error) {
      lastError = error;
    }
  }

  throw lastError;
}

function normalizeActivityDescriptors(value: unknown): ActivityDescriptor[] {
  if (!Array.isArray(value)) return [];
  return value
    .filter((item): item is ActivityDescriptor => {
      if (!item || typeof item !== "object") return false;
      const descriptor = item as Partial<ActivityDescriptor>;
      return typeof descriptor.typeName === "string" && Array.isArray(descriptor.inputs);
    })
    .map(descriptor => ({
      ...descriptor,
      inputs: descriptor.inputs ?? [],
      outputs: descriptor.outputs ?? [],
      ports: descriptor.ports ?? []
    }));
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

export const fallbackExpressionDescriptors: ExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
