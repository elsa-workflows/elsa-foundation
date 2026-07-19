import { useCallback, useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { scopedVariableSignature } from "../scopedVariables";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import type {
  ActivityInputOptionsResponse,
  CreateDefinitionRequest,
  DefinitionListState,
  PromoteDraftResponse,
  ScopedVariableAnalysisResponse,
  WorkflowDefinitionDetails,
  WorkflowDefinitionState,
  WorkflowDefinitionsResponse,
  WorkflowDefinitionVersionDetails,
  WorkflowDraft,
  WorkflowFolder,
  WorkflowFolderDetail
} from "../workflowTypes";
import {
  ApiCapabilityUnavailableError,
  capabilityIds,
  clearApiCapabilityCache,
  resolveCapabilityLink
} from "./capabilities";

export interface DefinitionListRequest {
  search: string;
  state?: DefinitionListState;
  page: number;
  pageSize: number;
  continuationToken?: string;
  folderId?: string | null;
  unfiled?: boolean;
}

type WorkflowDraftResponse = Omit<WorkflowDraft, "validationErrors"> & {
  validationErrors?: WorkflowDraft["validationErrors"] | null;
};

type WorkflowDefinitionDetailsResponse = Omit<WorkflowDefinitionDetails, "draft"> & {
  draft?: WorkflowDraftResponse | null;
};

export const workflowDesignKeys = {
  all: ["workflow-design"] as const,
  definitions: ["workflow-design", "definitions"] as const,
  definitionsList: (request: DefinitionListRequest) => [...workflowDesignKeys.definitions, "list", request] as const,
  definition: (definitionId: string) => [...workflowDesignKeys.definitions, "detail", definitionId] as const,
  folders: ["workflow-design", "folders"] as const,
  folderChildren: (parentId?: string | null) => [...workflowDesignKeys.folders, "children", parentId ?? null] as const,
  version: (versionId: string) => ["workflow-design", "versions", versionId] as const
};

export function useWorkflowDefinitions(context: StudioEndpointContext, request: DefinitionListRequest) {
  return useQuery({
    queryKey: workflowDesignKeys.definitionsList(request),
    queryFn: () => listDefinitions(context, request),
    placeholderData: previous => previous
  });
}

export function useWorkflowDefinition(context: StudioEndpointContext, definitionId: string | null | undefined) {
  return useQuery({
    queryKey: workflowDesignKeys.definition(definitionId ?? ""),
    enabled: !!definitionId,
    queryFn: () => getDefinition(context, definitionId!)
  });
}

export function useCreateWorkflowDefinition(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: CreateDefinitionRequest) => createDefinition(context, request),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workflowDesignKeys.all })
  });
}

export function useDeleteWorkflowDefinition(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (definitionId: string) => deleteDefinition(context, definitionId),
    onSuccess: () => queryClient.invalidateQueries({ queryKey: workflowDesignKeys.all })
  });
}

async function definitionsPath(context: StudioEndpointContext) {
  return resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-definitions");
}

async function definitionPagePath(context: StudioEndpointContext) {
  try {
    return await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-definitions-page");
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError && error.relation === "workflow-definitions-page") return null;
    throw error;
  }
}

/** Returns the advertised folder root, never a host-constructed URL. */
export async function workflowFoldersPath(context: StudioEndpointContext) {
  try {
    return await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-folders");
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError && error.relation === "workflow-folders") return null;
    throw error;
  }
}

/** Returns the advertised bulk-placement endpoint, never a host-constructed URL. */
export async function workflowDefinitionFolderMovePath(context: StudioEndpointContext) {
  try {
    return await resolveCapabilityLink(context, capabilityIds.workflowDesign, "workflow-definition-folder-move");
  } catch (error) {
    if (error instanceof ApiCapabilityUnavailableError && error.relation === "workflow-definition-folder-move") return null;
    throw error;
  }
}

export interface WorkflowFolderPage {
  items: WorkflowFolder[];
  nextContinuationToken: string | null;
}

export async function listWorkflowFolders(context: StudioEndpointContext, request: { parentId?: string | null; continuationToken?: string | null; pageSize?: number } = {}) {
  const root = await workflowFoldersPath(context);
  if (!root) return null;
  const parameters = new URLSearchParams({ pageSize: String(request.pageSize ?? 100) });
  if (request.parentId) parameters.set("parentId", request.parentId);
  if (request.continuationToken) parameters.set("continuationToken", request.continuationToken);
  const response = await context.http.getJson<{ items?: WorkflowFolder[]; nextContinuationToken?: string | null }>(`${root}?${parameters}`);
  return { items: response.items ?? [], nextContinuationToken: response.nextContinuationToken ?? null };
}

export async function getWorkflowFolder(context: StudioEndpointContext, folderId: string) {
  const root = await workflowFoldersPath(context);
  if (!root) return null;
  return context.http.getJson<WorkflowFolderDetail>(`${root}/${encodeURIComponent(folderId)}`);
}

export async function createWorkflowFolder(context: StudioEndpointContext, request: { name: string; parentId?: string | null }) {
  const root = await workflowFoldersPath(context);
  if (!root) throw new ApiCapabilityUnavailableError(capabilityIds.workflowDesign, "workflow-folders");
  const response = await context.http.postJson<WorkflowFolder | { folder: WorkflowFolder }>(root, request);
  return "folder" in response ? response.folder : response;
}

/** Moves one or more definitions to a persisted folder, or to Unfiled when folderId is null. */
export async function moveWorkflowDefinitions(
  context: StudioEndpointContext,
  request: { definitionIds: string[]; folderId: string | null }
) {
  const path = await workflowDefinitionFolderMovePath(context);
  if (!path) throw new ApiCapabilityUnavailableError(capabilityIds.workflowDesign, "workflow-definition-folder-move");
  await context.http.postJson<unknown>(path, request);
}

export async function listDefinitions(context: StudioEndpointContext, request: DefinitionListRequest) {
  const parameters = new URLSearchParams({
    state: request.state ?? "active"
  });
  const search = request.search.trim();
  if (search) parameters.set("search", search);

  const pagedPath = await definitionPagePath(context);
  if (pagedPath) {
    parameters.set("pageSize", String(request.pageSize));
    if (request.continuationToken) parameters.set("continuationToken", request.continuationToken);
    if (request.folderId) parameters.set("folderId", request.folderId);
    if (request.unfiled) parameters.set("unfiled", "true");
    const response = await context.http.getJson<{
      items: WorkflowDefinitionsResponse["definitions"];
      nextContinuationToken?: string | null;
    }>(`${pagedPath}?${parameters.toString()}`);
    return {
      definitions: response.items ?? [],
      page: request.page,
      pageSize: request.pageSize,
      nextContinuationToken: response.nextContinuationToken ?? null,
      isPaged: true
    } satisfies WorkflowDefinitionsResponse;
  }

  const response = await context.http.getJson<{ items: WorkflowDefinitionsResponse["definitions"] }>(
    `${await definitionsPath(context)}?${parameters.toString()}`);
  const offset = Math.max(0, request.page - 1) * request.pageSize;
  return {
    definitions: (response.items ?? []).slice(offset, offset + request.pageSize),
    page: request.page,
    pageSize: request.pageSize,
    totalCount: response.items?.length ?? 0
  } satisfies WorkflowDefinitionsResponse;
}

export async function getDefinition(context: StudioEndpointContext, definitionId: string) {
  const details = await context.http.getJson<WorkflowDefinitionDetailsResponse>(
    `${await definitionsPath(context)}/${encodeURIComponent(definitionId)}`);
  return normalizeDefinitionDetails(details);
}

export async function createDefinition(context: StudioEndpointContext, request: CreateDefinitionRequest) {
  const wireRequest = request.initialState
    ? { ...request, initialState: canonicalizeStateForWire(request.initialState) }
    : request;
  const details = await context.http.postJson<WorkflowDefinitionDetailsResponse>(await definitionsPath(context), wireRequest);
  return normalizeDefinitionDetails(details);
}

export async function deleteDefinition(context: StudioEndpointContext, definitionId: string) {
  await context.http.deleteJson<unknown>(`${await definitionsPath(context)}/${encodeURIComponent(definitionId)}`);
}

export async function restoreDefinition(context: StudioEndpointContext, definitionId: string) {
  await context.http.postJson<unknown>(`${await definitionsPath(context)}/${encodeURIComponent(definitionId)}/restore`, {});
}

export async function deleteDefinitionPermanently(context: StudioEndpointContext, definitionId: string) {
  await context.http.deleteJson<unknown>(`${await definitionsPath(context)}/${encodeURIComponent(definitionId)}/permanent`);
}

export async function updateDefinitionMetadata(
  context: StudioEndpointContext,
  definitionId: string,
  patch: { name: string; description?: string | null }
): Promise<WorkflowDefinitionDetails> {
  const details = await context.http.requestJson<WorkflowDefinitionDetailsResponse>(
    `${await definitionsPath(context)}/${encodeURIComponent(definitionId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(patch)
    }
  );
  return normalizeDefinitionDetails(details);
}

export async function getWorkflowDefinitionVersion(context: StudioEndpointContext, versionId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "workflow-versions",
    { versionId });
  const details = await context.http.getJson<WorkflowDefinitionVersionDetails>(path);
  return { ...details, state: expandStateFromWire(details.state) };
}

export async function updateDraft(context: StudioEndpointContext, draft: WorkflowDraft) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "workflow-drafts",
    { draftId: draft.id });
  const saved = await context.http.putJson<WorkflowDraftResponse>(path, {
    state: canonicalizeStateForWire(draft.state),
    layout: draft.layout
  });
  return normalizeWorkflowDraft(saved);
}

export async function getDraft(context: StudioEndpointContext, draftId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "workflow-drafts",
    { draftId });
  const draft = await context.http.getJson<WorkflowDraftResponse>(path);
  return normalizeWorkflowDraft(draft);
}

export async function discardDraft(context: StudioEndpointContext, draftId: string) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "workflow-drafts",
    { draftId });
  await context.http.deleteJson<unknown>(path);
}

export async function promoteDraft(context: StudioEndpointContext, draftId: string) {
  const draftPath = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "workflow-drafts",
    { draftId });
  return context.http.postJson<PromoteDraftResponse>(`${draftPath}/promote`, {});
}

export async function analyzeScopedVariables(
  context: StudioEndpointContext,
  state: WorkflowDefinitionState,
  nodeId: string,
  signal?: AbortSignal
): Promise<ScopedVariableAnalysisResponse> {
  const path = await resolveCapabilityLink(context, capabilityIds.workflowDesign, "scoped-variable-analysis");
  const response = await context.http.postJson<ScopedVariableAnalysisResponse>(
    path,
    { state: canonicalizeStateForWire(state), nodeId },
    { signal });
  return {
    visibleVariables: Array.isArray(response?.visibleVariables) ? response.visibleVariables : [],
    shadowingWarnings: Array.isArray(response?.shadowingWarnings) ? response.shadowingWarnings : []
  };
}

export async function getActivityInputOptions(
  context: StudioEndpointContext,
  activityVersionId: string,
  inputName: string,
  nodeId: string,
  workflowState: WorkflowDefinitionState,
  signal: AbortSignal
) {
  const path = await resolveCapabilityLink(
    context,
    capabilityIds.workflowDesign,
    "activity-input-options",
    { activityVersionId, inputName });
  const response = await context.http.postJson<ActivityInputOptionsResponse>(
    path,
    { nodeId, workflowState: canonicalizeStateForWire(workflowState) },
    { signal });
  return Array.isArray(response?.options)
    ? response.options.filter(option =>
      !!option &&
      typeof option.label === "string" && option.label.trim().length > 0 &&
      (typeof option.value === "string" || typeof option.value === "number" || typeof option.value === "boolean"))
    : [];
}

export type ScopedVariableAnalysisStatus = "loading" | "ready" | "unavailable" | "error";

export interface ScopedVariableAnalysis extends ScopedVariableAnalysisResponse {
  status: ScopedVariableAnalysisStatus;
  retry?: () => void;
}

const emptyAnalysis = (status: ScopedVariableAnalysisStatus): ScopedVariableAnalysis =>
  ({ visibleVariables: [], shadowingWarnings: [], status });

export function useScopedVariableAnalysis(
  context: StudioEndpointContext,
  state: WorkflowDefinitionState | null | undefined,
  nodeId: string | null,
  catalog?: ActivityCatalogLookup
): ScopedVariableAnalysis {
  const signature = useMemo(() => scopedVariableSignature(state, catalog), [catalog, state]);
  const [result, setResult] = useState<ScopedVariableAnalysis>(() => emptyAnalysis("loading"));
  const [retryVersion, setRetryVersion] = useState(0);
  const retry = useCallback(() => {
    clearApiCapabilityCache();
    setRetryVersion(version => version + 1);
  }, []);

  useEffect(() => {
    let active = true;
    const controller = new AbortController();
    setResult(previous => ({ ...previous, status: "loading" }));

    if (!state) {
      // Capability discovery still runs so the shell performs one predictable bootstrap.
      resolveCapabilityLink(context, capabilityIds.workflowDesign, "scoped-variable-analysis").then(
        () => { if (active) setResult(emptyAnalysis("unavailable")); },
        error => { if (active) setResult(emptyAnalysis(error instanceof ApiCapabilityUnavailableError ? "unavailable" : "error")); });
      return () => { active = false; controller.abort(); };
    }

    if (nodeId === null) {
      resolveCapabilityLink(context, capabilityIds.workflowDesign, "scoped-variable-analysis").then(
        () => { if (active) setResult(emptyAnalysis("ready")); },
        error => { if (active) setResult(emptyAnalysis(error instanceof ApiCapabilityUnavailableError ? "unavailable" : "error")); });
      return () => { active = false; controller.abort(); };
    }

    analyzeScopedVariables(context, state, nodeId, controller.signal).then(
      data => { if (active) setResult({ ...data, status: "ready" }); },
      error => { if (active) setResult(previous => ({
        ...previous,
        status: error instanceof ApiCapabilityUnavailableError ? "unavailable" : "error"
      })); });

    return () => { active = false; controller.abort(); };
    // `signature` captures visibility-affecting state; full state would refetch on unrelated edits.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [context, nodeId, retryVersion, signature]);

  return { ...result, retry };
}

function normalizeDefinitionDetails(details: WorkflowDefinitionDetailsResponse): WorkflowDefinitionDetails {
  return {
    ...details,
    draft: details.draft ? normalizeWorkflowDraft(details.draft) : details.draft
  };
}

function normalizeWorkflowDraft(draft: WorkflowDraftResponse): WorkflowDraft {
  return {
    ...draft,
    state: expandStateFromWire(draft.state),
    validationErrors: Array.isArray(draft.validationErrors) ? draft.validationErrors : []
  };
}
