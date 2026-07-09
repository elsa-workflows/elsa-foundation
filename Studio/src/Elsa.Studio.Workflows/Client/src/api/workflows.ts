import { useEffect, useMemo, useState } from "react";
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { canonicalizeStateForWire, expandStateFromWire } from "../activityInputWire";
import { scopedVariableSignature } from "../scopedVariables";
import type { ActivityCatalogLookup } from "../workflowAdapter";
import type {
  ActivityAvailabilityDiagnostics,
  ActivityAvailabilitySettings,
  ActivityCatalogResponse,
  ActivityExecutionInspection,
  ActivityDescriptor,
  ActivityDescriptorsResponse,
  CreateDefinitionRequest,
  DefinitionListState,
  ExpressionDescriptor,
  ExpressionDescriptorsResponse,
  ScopedVariableAnalysisResponse,
  StorageDriverDescriptor,
  StorageDriverDescriptorsResponse,
  VariableTypeDescriptor,
  VariableTypeDescriptorsResponse,
  PromoteDraftResponse,
  PublishedWorkflowResponse,
  RuntimeDiagnosticsSettingsView,
  SaveRuntimeDiagnosticsSettingsRequest,
  SaveActivityAvailabilitySettingsRequest,
  StartWorkflowDraftTestRunRequest,
  WorkflowDefinitionState,
  WorkflowInstanceDetails,
  WorkflowInstanceSummary,
  WorkflowExecutableRunResponse,
  WorkflowExecutablesResponse,
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
  activityExecution: (workflowExecutionId: string, activityExecutionId: string) => ["workflows", "instances", workflowExecutionId, "activity-executions", activityExecutionId] as const,
  activities: ["workflows", "activities"] as const,
  activityDescriptors: ["workflows", "activity-descriptors"] as const,
  expressionDescriptors: ["workflows", "expression-descriptors"] as const,
  activityAvailabilitySettings: ["workflows", "activity-availability", "settings"] as const,
  activityAvailabilityDiagnostics: ["workflows", "activity-availability", "diagnostics"] as const,
  runtimeDiagnosticsSettings: ["workflows", "runtime-diagnostics", "settings"] as const
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

export function useActivityAvailabilitySettings(context: StudioEndpointContext) {
  return useQuery({
    queryKey: workflowKeys.activityAvailabilitySettings,
    queryFn: () => getActivityAvailabilitySettings(context)
  });
}

export function useActivityAvailabilityDiagnostics(context: StudioEndpointContext) {
  return useQuery({
    queryKey: workflowKeys.activityAvailabilityDiagnostics,
    queryFn: () => listActivityAvailabilityDiagnostics(context)
  });
}

export function useSaveActivityAvailabilitySettings(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveActivityAvailabilitySettingsRequest) => saveActivityAvailabilitySettings(context, request),
    onSuccess: saved => {
      // Seed the cache with the PUT response so the page's dirty indicator clears immediately,
      // then refresh the management views and the picker, which now reflect the saved policy.
      queryClient.setQueryData(workflowKeys.activityAvailabilitySettings, saved);
      void queryClient.invalidateQueries({ queryKey: workflowKeys.activityAvailabilityDiagnostics });
      void queryClient.invalidateQueries({ queryKey: workflowKeys.activities });
    }
  });
}

export function useRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  return useQuery({
    queryKey: workflowKeys.runtimeDiagnosticsSettings,
    queryFn: () => getRuntimeDiagnosticsSettings(context)
  });
}

export function useSaveRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (request: SaveRuntimeDiagnosticsSettingsRequest) => saveRuntimeDiagnosticsSettings(context, request),
    onSuccess: () => {
      void queryClient.invalidateQueries({ queryKey: workflowKeys.runtimeDiagnosticsSettings });
    }
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

// Scoped-variable design analysis (elsa-foundation#285): visible variables for the selected activity
// (nearest-scope first) plus non-blocking shadowing warnings. Sends the live, canonicalized state so
// unsaved declarations are reflected. The endpoint is a pending backend dependency; callers degrade
// gracefully (status "unavailable") until it ships — they never fall back to client-side computation.
export async function analyzeScopedVariables(
  context: StudioEndpointContext,
  state: WorkflowDefinitionState,
  nodeId: string | null
): Promise<ScopedVariableAnalysisResponse> {
  const response = await context.http.postJson<ScopedVariableAnalysisResponse>(
    `${basePath}/design/scoped-variables/analyze`,
    { state: canonicalizeStateForWire(state), nodeId });
  return {
    visibleVariables: Array.isArray(response?.visibleVariables) ? response.visibleVariables : [],
    shadowingWarnings: Array.isArray(response?.shadowingWarnings) ? response.shadowingWarnings : []
  };
}

export type ScopedVariableAnalysisStatus = "loading" | "ready" | "unavailable";

export interface ScopedVariableAnalysis extends ScopedVariableAnalysisResponse {
  status: ScopedVariableAnalysisStatus;
}

const emptyAnalysis = (status: ScopedVariableAnalysisStatus): ScopedVariableAnalysis =>
  ({ visibleVariables: [], shadowingWarnings: [], status });

// Re-runs only when the selected node or a visibility-affecting declaration/structure change occurs
// (see scopedVariableSignature), not on every keystroke. Uses the imperative load pattern the
// workflow designer relies on (no QueryClientProvider in that tree). A failed/absent endpoint surfaces
// as "unavailable" with empty results — callers never fall back to client-side computation.
export function useScopedVariableAnalysis(
  context: StudioEndpointContext,
  state: WorkflowDefinitionState | null | undefined,
  nodeId: string | null,
  catalog?: ActivityCatalogLookup
): ScopedVariableAnalysis {
  const signature = useMemo(() => scopedVariableSignature(state, catalog), [catalog, state]);
  const [result, setResult] = useState<ScopedVariableAnalysis>(() => emptyAnalysis("loading"));

  useEffect(() => {
    if (!state) {
      setResult(emptyAnalysis("unavailable"));
      return;
    }

    let cancelled = false;
    setResult(previous => ({ ...previous, status: "loading" }));
    analyzeScopedVariables(context, state, nodeId).then(
      data => { if (!cancelled) setResult({ ...data, status: "ready" }); },
      () => { if (!cancelled) setResult(emptyAnalysis("unavailable")); }
    );

    return () => { cancelled = true; };
    // `signature` captures the visibility-relevant parts of `state`; re-running on the full object
    // would refetch on every unrelated edit.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [context, nodeId, signature]);

  return result;
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

// Updates a definition's name/description after creation (the draft save path only carries State+Layout).
// The Studio SDK http client has no PATCH helper, so we use the generic requestJson escape hatch. The
// backend endpoint (PATCH /definitions/{id}, a partial update) returns the same WorkflowDefinitionDetails
// shape as GET /definitions/{id}; callers read `.definition` for the refreshed summary.
export async function updateDefinitionMetadata(
  context: StudioEndpointContext,
  definitionId: string,
  patch: { name: string; description?: string | null }
): Promise<WorkflowDefinitionDetails> {
  return context.http.requestJson<WorkflowDefinitionDetails>(
    `${basePath}/definitions/${encodeURIComponent(definitionId)}`,
    {
      method: "PATCH",
      headers: { "Content-Type": "application/json", Accept: "application/json" },
      body: JSON.stringify(patch)
    }
  );
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
  const paths = [`${basePath}/executables`, "/_demo/workflows/executables"];
  const errors: unknown[] = [];

  for (const path of paths) {
    try {
      const response = await context.http.getJson<WorkflowExecutablesResponse | WorkflowExecutableSummary[]>(path);
      return normalizeWorkflowExecutables(response);
    } catch (error) {
      errors.push(error);
    }
  }

  if (errors.length > 0 && errors.every(isNotFoundError)) return [];
  throw errors.find(error => !isNotFoundError(error)) ?? errors[errors.length - 1] ?? new Error("Workflow executables could not be loaded.");
}

function normalizeWorkflowExecutables(response: WorkflowExecutablesResponse | WorkflowExecutableSummary[]): WorkflowExecutableSummary[] {
  return Array.isArray(response) ? response : response.executables ?? [];
}

function isNotFoundError(error: unknown): boolean {
  if (!(error instanceof Error)) return false;
  const message = error.message.toLowerCase();
  return /\b404\b/.test(message) || message.includes("not found");
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

export async function getActivityExecutionInspection(context: StudioEndpointContext, workflowExecutionId: string, activityExecutionId: string) {
  return context.http.getJson<ActivityExecutionInspection>(
    `/runtime/workflows/instances/${encodeURIComponent(workflowExecutionId)}/activity-executions/${encodeURIComponent(activityExecutionId)}`
  );
}

export async function listActivities(context: StudioEndpointContext) {
  return context.http.getJson<ActivityCatalogResponse>(`${basePath}/activities`);
}

export async function getActivityAvailabilitySettings(context: StudioEndpointContext) {
  return context.http.getJson<ActivityAvailabilitySettings>(`${basePath}/activities/availability/settings`);
}

export async function saveActivityAvailabilitySettings(context: StudioEndpointContext, request: SaveActivityAvailabilitySettingsRequest) {
  return context.http.putJson<ActivityAvailabilitySettings>(`${basePath}/activities/availability/settings`, request);
}

export async function listActivityAvailabilityDiagnostics(context: StudioEndpointContext) {
  return context.http.getJson<ActivityAvailabilityDiagnostics>(`${basePath}/activities/availability/diagnostics`);
}

export async function getRuntimeDiagnosticsSettings(context: StudioEndpointContext) {
  return context.http.getJson<RuntimeDiagnosticsSettingsView>(`${basePath}/runtime-diagnostics/settings`);
}

export async function saveRuntimeDiagnosticsSettings(context: StudioEndpointContext, request: SaveRuntimeDiagnosticsSettingsRequest) {
  return context.http.putJson<RuntimeDiagnosticsSettingsView>(`${basePath}/runtime-diagnostics/settings`, request);
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

// Variable type descriptors populate the Type picker in the Properties tab. The backend proxies
// the Elsa `descriptors/variables` endpoint; callers fall back to a free-text input when it 404s.
// Items are keyed on a bare `alias` (typed-argument-model contract); `typeName` is tolerated for
// older backends that predate the alias field.
export async function listVariableTypeDescriptors(context: StudioEndpointContext): Promise<VariableTypeDescriptor[]> {
  const response = await getFirstAvailable<VariableTypeDescriptorsResponse | VariableTypeDescriptor[]>(context, [
    `${basePath}/descriptors/variables`,
    "/descriptors/variables"
  ]);
  const descriptors = Array.isArray(response) ? response : response.items ?? response.descriptors ?? [];
  return descriptors.filter((descriptor): descriptor is VariableTypeDescriptor => isSelectableDescriptor(descriptor));
}

// A descriptor is selectable if it carries a non-empty alias (or legacy typeName).
function isSelectableDescriptor(value: unknown): value is VariableTypeDescriptor {
  if (!value || typeof value !== "object") return false;
  const descriptor = value as { alias?: unknown; typeName?: unknown };
  const alias = typeof descriptor.alias === "string" && descriptor.alias.length > 0;
  const typeName = typeof descriptor.typeName === "string" && descriptor.typeName.length > 0;
  return alias || typeName;
}

// Storage driver descriptors populate the Storage picker. Same proxy/fallback contract as above.
export async function listStorageDriverDescriptors(context: StudioEndpointContext): Promise<StorageDriverDescriptor[]> {
  const response = await getFirstAvailable<StorageDriverDescriptorsResponse | StorageDriverDescriptor[]>(context, [
    `${basePath}/descriptors/storage-drivers`,
    "/descriptors/storage-drivers"
  ]);
  const descriptors = Array.isArray(response) ? response : response.items ?? response.descriptors ?? [];
  return descriptors.filter((descriptor): descriptor is StorageDriverDescriptor => isNonEmptyTypeName(descriptor));
}

function isNonEmptyTypeName(value: unknown): value is { typeName: string } {
  return !!value && typeof value === "object" && typeof (value as { typeName?: unknown }).typeName === "string" && (value as { typeName: string }).typeName.length > 0;
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
