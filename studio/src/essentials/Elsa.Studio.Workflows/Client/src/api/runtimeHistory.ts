import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { capabilityIds, hasCapabilityLink, resolveCapabilityLink } from "./capabilities";
import type { RuntimeHistoryBookmark, RuntimeHistoryBookmarkDetails, RuntimeHistoryBookmarkPage, RuntimeHistoryBookmarkRequest,
  RuntimeHistoryCoverage, RuntimeHistoryEvent, RuntimeHistoryEventDetails, RuntimeHistoryPage, RuntimeHistoryRequest } from "../runtimeHistoryTypes";

export class RuntimeHistoryIdentityError extends Error {}

export function supportsWorkflowHistory(context: StudioEndpointContext) {
  return hasCapabilityLink(context, capabilityIds.runtime, "workflow-history-page");
}

export async function supportsWorkflowHistoryStream(context: StudioEndpointContext) {
  return !!context.http.requestResponse && await hasCapabilityLink(context, capabilityIds.runtime, "workflow-history-live");
}

export class RuntimeHistoryStreamError extends Error {
  constructor(readonly status: number) { super(`The committed history stream returned ${status}.`); }
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
  return validateHistoryPage(page, workflowExecutionId, request);
}

export async function* getWorkflowHistoryStream(context: StudioEndpointContext, workflowExecutionId: string,
  request: RuntimeHistoryRequest, signal: AbortSignal): AsyncGenerator<RuntimeHistoryPage> {
  // The host releases each stream after 30 seconds. Bound the entire body too, including a broken proxy after headers.
  const controller = new AbortController();
  const forwardAbort = () => controller.abort(signal.reason);
  signal.addEventListener("abort", forwardAbort, { once: true });
  if (signal.aborted) forwardAbort();
  const timeout = globalThis.setTimeout(() => controller.abort(new DOMException("The committed history stream timed out.", "TimeoutError")), 35_000);
  const streamSignal = controller.signal;
  let reader: ReadableStreamDefaultReader<string> | undefined;
  try {
    const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-live", { workflowExecutionId });
    streamSignal.throwIfAborted();
    const response = await context.http.requestResponse!(`${path}?${queryString(request)}`, {
      headers: { Accept: "application/x-ndjson" }, cache: "no-store", signal: streamSignal
    });
    if (!response.ok) throw new RuntimeHistoryStreamError(response.status);
    if (response.headers.get("content-type")?.split(";")[0] !== "application/x-ndjson" || !response.body)
      throw new RuntimeHistoryIdentityError("The committed history stream has an unexpected response type.");

    reader = response.body.pipeThrough(new TextDecoderStream()).getReader();
    let buffer = "";
    while (true) {
      const chunk = await reader.read();
      if (chunk.done) break;
      buffer += chunk.value;
      for (let newline = buffer.indexOf("\n"); newline >= 0; newline = buffer.indexOf("\n")) {
        streamSignal.throwIfAborted();
        const line = buffer.slice(0, newline);
        buffer = buffer.slice(newline + 1);
        let frame: { page?: RuntimeHistoryPage; status?: number };
        try { frame = JSON.parse(line); }
        catch { throw new RuntimeHistoryIdentityError("A committed history frame could not be read."); }
        if (frame?.status) throw new RuntimeHistoryStreamError(frame.status);
        if (!frame?.page) throw new RuntimeHistoryIdentityError("A committed history frame has no page.");
        yield validateHistoryPage(frame.page, workflowExecutionId, request);
      }
    }
    if (buffer.length) throw new Error("The committed history stream ended partway through a page.");
  } finally {
    globalThis.clearTimeout(timeout);
    signal.removeEventListener("abort", forwardAbort);
    if (reader) {
      void reader.cancel().catch(() => undefined);
      reader.releaseLock();
    }
  }
}

function validateHistoryPage(page: RuntimeHistoryPage, workflowExecutionId: string, request: RuntimeHistoryRequest) {
  validateHistoryCoverage(page.coverage);
  let previous = 0;
  for (const event of page.items) {
    validateHistoryEvent(event, workflowExecutionId);
    if (event.sequence <= previous || event.sequence > page.upperSequence) throw new RuntimeHistoryIdentityError("History page order was inconsistent.");
    if (request.activityExecutionId && !request.includeDescendants && event.activityExecutionId !== request.activityExecutionId
      || request.bookmarkId && event.bookmarkId !== request.bookmarkId)
      throw new RuntimeHistoryIdentityError("History event did not belong to the requested exact scope.");
    previous = event.sequence;
  }
  return page;
}

export async function getWorkflowHistoryEvent(context: StudioEndpointContext, workflowExecutionId: string,
  historyEventId: string, signal?: AbortSignal): Promise<RuntimeHistoryEventDetails> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-event", { workflowExecutionId, historyEventId });
  const result = await context.http.getJson<RuntimeHistoryEventDetails>(path, { signal });
  validateHistoryCoverage(result.coverage);
  validateHistoryEvent(result.event, workflowExecutionId, historyEventId);
  return result;
}

export async function listWorkflowHistoryBookmarks(context: StudioEndpointContext, workflowExecutionId: string,
  request: RuntimeHistoryBookmarkRequest = {}, signal?: AbortSignal): Promise<RuntimeHistoryBookmarkPage> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-bookmarks", { workflowExecutionId });
  const result = await context.http.getJson<RuntimeHistoryBookmarkPage>(`${path}?${queryString(request)}`, { signal });
  validateHistoryCoverage(result.coverage);
  for (const item of result.items) {
    validateHistoryBookmark(item, workflowExecutionId);
    if (request.activityExecutionId && !request.includeDescendants && item.bookmark.identity.activityExecutionId !== request.activityExecutionId)
      throw new RuntimeHistoryIdentityError("Bookmark history identity did not match the requested execution.");
  }
  return result;
}

export async function getWorkflowHistoryBookmark(context: StudioEndpointContext, workflowExecutionId: string,
  bookmarkId: string, signal?: AbortSignal): Promise<RuntimeHistoryBookmarkDetails> {
  const path = await resolveCapabilityLink(context, capabilityIds.runtime, "workflow-history-bookmark", { workflowExecutionId, bookmarkId });
  const result = await context.http.getJson<RuntimeHistoryBookmarkDetails>(path, { signal });
  validateHistoryCoverage(result.coverage);
  validateHistoryBookmark(result.bookmark, workflowExecutionId, bookmarkId);
  return result;
}

function validateHistoryBookmark(item: RuntimeHistoryBookmark, run: string, expectedId?: string) {
  const identity = item?.bookmark?.identity;
  if (!identity || identity.workflowExecutionId !== run || !identity.activityExecutionId || !identity.bookmarkId
    || expectedId && identity.bookmarkId !== expectedId)
    throw new RuntimeHistoryIdentityError("Bookmark history identity did not match this run or handle.");
}

function validateHistoryCoverage(coverage: RuntimeHistoryCoverage) {
  if (coverage.state === "invalid")
    throw new RuntimeHistoryIdentityError("History integrity could not be established. Refresh after repair.");
}
