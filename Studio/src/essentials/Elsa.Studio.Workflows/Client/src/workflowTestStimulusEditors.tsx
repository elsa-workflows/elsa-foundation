import { lazy } from "react";
import { parseHttpTestRequestHeaders, toHttpTestRequestHeaderValues } from "./httpTestHeaderPolicy";
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
    return parseHttpTestRequestHeaders(toHttpTestRequestHeaderValues(headers)).error;
  },
  serialize: ({ draft }) => readObject(draft) ?? {},
  executeAction: async context => (await import("./workflowTestHttpAction")).sendHttpRequest(context),
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
