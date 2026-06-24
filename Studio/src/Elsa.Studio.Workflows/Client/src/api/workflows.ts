import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
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
  WorkflowExecutableSummary,
  WorkflowDefinitionDetails,
  WorkflowDefinitionsResponse,
  WorkflowDraft
} from "../workflowTypes";

const basePath = "/_elsa/workflow-management";

export const workflowKeys = {
  all: ["workflows"] as const,
  definitions: ["workflows", "definitions"] as const,
  definitionsList: (request: DefinitionListRequest) => [...workflowKeys.definitions, "list", request] as const,
  definition: (definitionId: string) => [...workflowKeys.definitions, "detail", definitionId] as const,
  executables: (definitionId?: string | null) => ["workflows", "executables", definitionId ?? "all"] as const,
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
  return context.http.getJson<WorkflowDefinitionDetails>(`${basePath}/definitions/${encodeURIComponent(definitionId)}`);
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
  return context.http.putJson<WorkflowDraft>(`${basePath}/drafts/${encodeURIComponent(draft.id)}`, { state: draft.state, layout: draft.layout });
}

export async function promoteDraft(context: StudioEndpointContext, draftId: string) {
  return context.http.postJson<PromoteDraftResponse>(`${basePath}/drafts/${encodeURIComponent(draftId)}/promote`, {});
}

export async function publishVersion(context: StudioEndpointContext, versionId: string) {
  return context.http.postJson<PublishedWorkflowResponse>(`${basePath}/versions/${encodeURIComponent(versionId)}/publish`, {});
}

export async function runExecutable(context: StudioEndpointContext, artifactId: string) {
  return context.http.postJson<unknown>(`${basePath}/executables/${encodeURIComponent(artifactId)}/run`, {});
}

export async function listExecutables(context: StudioEndpointContext) {
  return context.http.getJson<WorkflowExecutableSummary[]>("/_demo/workflows/executables");
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

export const fallbackExpressionDescriptors: ExpressionDescriptor[] = [
  { type: "Literal", displayName: "Literal" },
  { type: "JavaScript", displayName: "JavaScript" },
  { type: "Liquid", displayName: "Liquid" },
  { type: "Object", displayName: "Object" },
  { type: "Variable", displayName: "Variable" },
  { type: "Input", displayName: "Input" }
];
