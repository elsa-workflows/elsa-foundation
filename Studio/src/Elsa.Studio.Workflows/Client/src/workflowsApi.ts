import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type {
  ActivityCatalogResponse,
  CreateDefinitionRequest,
  DefinitionListState,
  PromoteDraftResponse,
  PublishedWorkflowResponse,
  WorkflowExecutableSummary,
  WorkflowDefinitionDetails,
  WorkflowDefinitionsResponse,
  WorkflowDraft
} from "./workflowTypes";

const basePath = "/_elsa/workflow-management";

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
  return requestJson<WorkflowDefinitionsResponse>(context, `${basePath}/definitions?${parameters.toString()}`);
}

export async function getDefinition(context: StudioEndpointContext, definitionId: string) {
  return requestJson<WorkflowDefinitionDetails>(context, `${basePath}/definitions/${encodeURIComponent(definitionId)}`);
}

export async function createDefinition(context: StudioEndpointContext, request: CreateDefinitionRequest) {
  return postJson<WorkflowDefinitionDetails>(context, `${basePath}/definitions`, request);
}

export async function deleteDefinition(context: StudioEndpointContext, definitionId: string) {
  await requestJson<unknown>(context, `${basePath}/definitions/${encodeURIComponent(definitionId)}`, {
    method: "DELETE"
  });
}

export async function restoreDefinition(context: StudioEndpointContext, definitionId: string) {
  await postJson<unknown>(context, `${basePath}/definitions/${encodeURIComponent(definitionId)}/restore`, {});
}

export async function deleteDefinitionPermanently(context: StudioEndpointContext, definitionId: string) {
  await requestJson<unknown>(context, `${basePath}/definitions/${encodeURIComponent(definitionId)}/permanent`, {
    method: "DELETE"
  });
}

export async function updateDraft(context: StudioEndpointContext, draft: WorkflowDraft) {
  return requestJson<WorkflowDraft>(context, `${basePath}/drafts/${encodeURIComponent(draft.id)}`, {
    method: "PUT",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify({ state: draft.state, layout: draft.layout })
  });
}

export async function promoteDraft(context: StudioEndpointContext, draftId: string) {
  return postJson<PromoteDraftResponse>(context, `${basePath}/drafts/${encodeURIComponent(draftId)}/promote`, {});
}

export async function publishVersion(context: StudioEndpointContext, versionId: string) {
  return postJson<PublishedWorkflowResponse>(context, `${basePath}/versions/${encodeURIComponent(versionId)}/publish`, {});
}

export async function runExecutable(context: StudioEndpointContext, artifactId: string) {
  return postJson<unknown>(context, `${basePath}/executables/${encodeURIComponent(artifactId)}/run`, {});
}

export async function listExecutables(context: StudioEndpointContext) {
  return requestJson<WorkflowExecutableSummary[]>(context, "/_demo/workflows/executables");
}

export async function listActivities(context: StudioEndpointContext) {
  return requestJson<ActivityCatalogResponse>(context, `${basePath}/activities`);
}

async function postJson<T>(context: StudioEndpointContext, url: string, body: unknown) {
  return requestJson<T>(context, url, {
    method: "POST",
    headers: { "Content-Type": "application/json", "Accept": "application/json" },
    body: JSON.stringify(body)
  });
}

async function requestJson<T>(context: StudioEndpointContext, url: string, init?: RequestInit) {
  const response = await fetch(new URL(url, context.baseUrl).toString(), init);
  const text = await response.text();
  if (!response.ok) {
    throw new Error(readError(text) || `Request failed with ${response.status}.`);
  }

  return (text ? JSON.parse(text) : {}) as T;
}

function readError(text: string) {
  if (!text.trim()) return "";
  try {
    const payload = JSON.parse(text) as Record<string, unknown>;
    if (typeof payload.error === "string") return payload.error;
    if (typeof payload.detail === "string") return payload.detail;
    if (typeof payload.title === "string") return payload.title;
  } catch {
    return text;
  }

  return text;
}
