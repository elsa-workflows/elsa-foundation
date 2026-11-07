import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";
import type { RuntimeHistoryBookmarkDetails, RuntimeHistoryBookmarkPage, RuntimeHistoryBookmarkRequest,
  RuntimeHistoryEvent, RuntimeHistoryEventDetails, RuntimeHistoryPage, RuntimeHistoryRequest } from "../runtimeHistoryTypes";

export class RuntimeHistoryIdentityError extends Error {}

export function supportsWorkflowHistory(context: StudioEndpointContext) {
  return hasCapabilityLink(context, capabilityIds.runtime, "workflow-history-page");
}

function queryString(request: RuntimeHistoryRequest | RuntimeHistoryBookmarkRequest) {
  const parameters = new URLSearchParams({ limit: String(request.limit ?? 100) });
  for (const [key, value] of Object.entries(request)) {
    if (value !== undefined && value !== null && value !== "") parameters.set(key === "continuationToken" ? "cursor" : key, String(value));
  }
  return parameters;
}

export function validateHistoryEvent(event: RuntimeHistoryEvent, run: string, expectedId?: string) {
  if (!event || event.workflowExecutionId !== run || !event.historyEventId || expectedId && event.historyEventId !== expectedId
    || !Number.isSafeInteger(event.sequence) || event.sequence < 1) {
    throw new RuntimeHistoryIdentityError("History identity did not match this run or event.");
  }
}

export async function getWorkflowHistoryPage(context: StudioEndpointContext, workflowExecutionId: string,
  request: RuntimeHistoryRequest = {}, signal?: AbortSignal): Promise<RuntimeHistoryPage> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-page", { workflowExecutionId });
  const page = await context.http.getJson<RuntimeHistoryPage>(`${path}?${queryString(request)}`, { signal });
  let previous = 0;
  for (const event of page.items) {
    validateHistoryEvent(event, workflowExecutionId);
    if (event.sequence <= previous || event.sequence > page.upperSequence) throw new RuntimeHistoryIdentityError("History page order was inconsistent.");
    previous = event.sequence;
  }
  return page;
}

export async function getWorkflowHistoryEvent(context: StudioEndpointContext, workflowExecutionId: string,
  historyEventId: string, signal?: AbortSignal): Promise<RuntimeHistoryEventDetails> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-event", { workflowExecutionId, historyEventId });
  const result = await context.http.getJson<RuntimeHistoryEventDetails>(path, { signal });
  validateHistoryEvent(result.event, workflowExecutionId, historyEventId);
  return result;
}

export async function listWorkflowHistoryBookmarks(context: StudioEndpointContext, workflowExecutionId: string,
  request: RuntimeHistoryBookmarkRequest = {}, signal?: AbortSignal): Promise<RuntimeHistoryBookmarkPage> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-bookmarks", { workflowExecutionId });
  const result = await context.http.getJson<RuntimeHistoryBookmarkPage>(`${path}?${queryString(request)}`, { signal });
  for (const item of result.items) if (item.bookmark.identity.workflowExecutionId !== workflowExecutionId
    || request.activityExecutionId && !request.includeDescendants && item.bookmark.identity.activityExecutionId !== request.activityExecutionId) {
    throw new RuntimeHistoryIdentityError("Bookmark history identity did not match the requested execution.");
  }
  return result;
}

export async function getWorkflowHistoryBookmark(context: StudioEndpointContext, workflowExecutionId: string,
  bookmarkId: string, signal?: AbortSignal): Promise<RuntimeHistoryBookmarkDetails> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-bookmark", { workflowExecutionId, bookmarkId });
  const result = await context.http.getJson<RuntimeHistoryBookmarkDetails>(path, { signal });
  if (result.bookmark.bookmark.identity.workflowExecutionId !== workflowExecutionId || result.bookmark.bookmark.identity.bookmarkId !== bookmarkId)
    throw new RuntimeHistoryIdentityError("Bookmark history identity did not match this run or handle.");
  return result;
}
