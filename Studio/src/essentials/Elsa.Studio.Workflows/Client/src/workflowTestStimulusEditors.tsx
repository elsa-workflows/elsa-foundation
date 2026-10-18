import { lazy } from "react";
import { validateHttpTestRequestHeaderNames } from "./httpTestHeaderPolicy";
import type { StudioTestStimulusEditorContribution } from "@elsa-workflows/studio-sdk";
import { asRecord, isJsonValue, isRecord, isStringMap, readObject } from "./workflowTestStimulusEditorUtils";

const HttpRequestEditorView = lazy(() => import("./workflowTestStimulusEditorComponents").then(module => ({ default: module.HttpRequestEditorView })));
const EventEditorView = lazy(() => import("./workflowTestStimulusEditorComponents").then(module => ({ default: module.EventEditorView })));
const SchedulingEditorView = lazy(() => import("./workflowTestStimulusEditorComponents").then(module => ({ default: module.SchedulingEditorView })));
const HttpExchangeResultView = lazy(() => import("./workflowTestStimulusEditorComponents").then(module => ({ default: module.HttpExchangeResultView })));

export const httpWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.http-request",
  order: 100,
  startLabel: "Send request and start",
  actionLabel: "Send HTTP request",
  supports: ({ option, mode }) => option.driverId === "Elsa.HttpEndpoint" && mode.modeId === "http-request",
  component: HttpRequestEditorView,
  validate: ({ draft }) => {
    const value = readObject(draft);
    if (!value) return "Enter valid JSON for the HTTP request fields.";
    for (const name of ["routeValues", "query", "headers"]) {
      if (!isStringMap(value[name], name === "routeValues" ? "string" : "string[]")) return `${name} must be an object of ${name === "routeValues" ? "string values" : "string arrays"}.`;
    }
    if (value.body !== undefined && value.body !== null && typeof value.body !== "string") return "body must be text or null.";
    const headers = asRecord(value.headers) ?? {};
    return validateHttpTestRequestHeaderNames(Object.keys(headers));
  },
  serialize: ({ draft }) => readObject(draft) ?? {},
  executeAction: sendHttpRequest,
  actionResultComponent: HttpExchangeResultView
};

export const eventWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.event",
  order: 110,
  startLabel: "Fire event now",
  supports: ({ option, mode }) => option.driverId === "Elsa.Event" && mode.modeId === "event",
  component: EventEditorView,
  validate: ({ draft }) => {
    try {
      const value: unknown = JSON.parse(draft || "{}");
      return isRecord(value) && (value.payload === undefined || isJsonValue(value.payload))
        ? undefined
        : "Event data must be an object with an optional JSON payload.";
    } catch { return "Enter valid JSON for the event payload."; }
  },
  serialize: ({ draft }) => JSON.parse(draft || "{}")
};

export const schedulingWorkflowTestStimulusEditor: StudioTestStimulusEditorContribution = {
  id: "elsa.workflows.test-stimulus.schedule-fire-now",
  order: 120,
  startLabel: "Fire once now",
  supports: ({ option, mode }) =>
    (option.driverId === "Elsa.Timer" || option.driverId === "Elsa.Cron") && mode.modeId === "fire-now",
  component: SchedulingEditorView,
  validate: () => undefined,
  serialize: () => undefined
};

async function sendHttpRequest({ backend, action, data }: Parameters<NonNullable<StudioTestStimulusEditorContribution["executeAction"]>>[0]) {
  const relativeUrl = action.relativeUrl;
  if (!relativeUrl || !relativeUrl.startsWith("/") || relativeUrl.startsWith("//")) {
    throw new Error("The prepared HTTP action did not contain a safe server-relative URL.");
  }
  if (!backend.http.requestResponse) throw new Error("This Studio host does not expose the authenticated HTTP transport.");
  const base = new URL(backend.baseUrl || window.location.href, window.location.href);
  const url = new URL(relativeUrl, base.origin);
  if (url.origin !== base.origin) throw new Error("The prepared HTTP action must stay on the Studio API host.");
  const body = asRecord(data) ?? {};
  const query = asRecord(body.query) ?? {};
  for (const [key, raw] of Object.entries(query)) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") url.searchParams.append(key, value);
  }
  const headers = new Headers();
  for (const [key, raw] of Object.entries(asRecord(body.headers) ?? {})) {
    const values = Array.isArray(raw) ? raw : [raw];
    for (const value of values) if (typeof value === "string") headers.append(key, value);
  }
  const method = (action.method || "POST").toUpperCase();
  const textBody = typeof body.body === "string" ? body.body : undefined;
  const startedAt = performance.now();
  const controller = new AbortController();
  const timeoutMs = typeof body.timeoutMs === "number" && Number.isFinite(body.timeoutMs)
    ? Math.max(1000, Math.min(body.timeoutMs, 120000))
    : 30000;
  const timeout = window.setTimeout(() => controller.abort(), timeoutMs);
  try {
    const response = await backend.http.requestResponse(url.href, {
      method,
      headers,
      ...(textBody !== undefined && !["GET", "HEAD"].includes(method) ? { body: textBody } : {}),
      signal: controller.signal,
      retryOnUnauthorized: false
    });
    const responseText = await response.text();
    return {
      kind: "http-exchange",
      method,
      status: response.status,
      statusText: response.statusText,
      ok: response.ok,
      headers: [...response.headers.entries()],
      body: responseText,
      durationMs: Math.round(performance.now() - startedAt)
    };
  } catch {
    return {
      kind: "http-exchange",
      method,
      error: controller.signal.aborted ? `Request timed out after ${timeoutMs} ms.` : "The HTTP request failed.",
      durationMs: Math.round(performance.now() - startedAt)
    };
  } finally {
    window.clearTimeout(timeout);
  }
}
