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
const requestTimeoutMs = 10000;

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
  const requestUrl = new URL(url, context.baseUrl).toString();
  const controller = new AbortController();
  const timeoutId = window.setTimeout(() => controller.abort(), requestTimeoutMs);

  let response: Response;
  try {
    response = await fetch(requestUrl, {
      ...init,
      signal: controller.signal
    });
  } catch (error) {
    if (controller.signal.aborted) {
      throw new Error(`Request to ${requestUrl} timed out after ${requestTimeoutMs / 1000} seconds. Check Studio:BackendBaseUrl and make sure the backend workflow-management API is responding.`);
    }
    throw error;
  } finally {
    window.clearTimeout(timeoutId);
  }

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
