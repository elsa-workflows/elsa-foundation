import React from "react";
import { createRoot } from "react-dom/client";
import type { ElsaStudioModuleApi } from "@elsa-workflows/studio-sdk";
import { OpenTelemetryPage, OpenTelemetryPanel, register } from "../../../extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/module";
import "../../src/essentials/Elsa.Studio.Hosting/Client/src/app/styles.css";
import "../../../extensions/Diagnostics.OpenTelemetry/studio/Elsa.Studio.Diagnostics.OpenTelemetry/Client/src/styles.css";

const trace = {
  traceId: "trace-resize-fixture",
  rootSpanId: "span-root",
  name: "Workflow resize fixture",
  startTime: "2026-11-21T09:00:00.000Z",
  endTime: "2026-11-21T09:00:02.000Z",
  duration: "00:00:02.0000000",
  status: "Ok",
  resourceIds: ["resource-engine"],
  workflowInstanceIds: ["instance-resize"],
  spanCount: 3
};

const resources = [{
  id: "resource-engine",
  serviceName: "elsa-workflow-engine",
  serviceInstanceId: "resize-fixture",
  telemetrySdkLanguage: "dotnet",
  attributes: { environment: "test" },
  lastSeen: trace.endTime,
  status: "Active"
}];

const spans = [
  {
    spanId: "span-root",
    name: "elsa.workflow.execute",
    kind: "Internal",
    startTime: trace.startTime,
    endTime: trace.endTime,
    status: "Unset",
    resourceId: "resource-engine",
    attributes: Object.fromEntries(Array.from({ length: 28 }, (_, index) => [`workflow.attribute.${index + 1}`, `value-${index + 1}`])),
    events: [],
    links: []
  },
  {
    spanId: "span-activity",
    parentSpanId: "span-root",
    name: "WriteLine activity",
    kind: "Internal",
    startTime: "2026-11-21T09:00:00.250Z",
    endTime: "2026-11-21T09:00:01.250Z",
    status: "Ok",
    resourceId: "resource-engine",
    attributes: { "elsa.activity.type": "WriteLine" },
    events: [],
    links: []
  },
  {
    spanId: "span-checkpoint",
    parentSpanId: "span-root",
    name: "Checkpoint commit",
    kind: "Internal",
    startTime: "2026-11-21T09:00:01.300Z",
    endTime: "2026-11-21T09:00:01.600Z",
    status: "Error",
    resourceId: "resource-engine",
    attributes: { "elsa.checkpoint": "commit" },
    events: [],
    links: []
  }
];

const storage = {
  traceCapacity: 100,
  spanCapacity: 1_000,
  metricPointCapacity: 0,
  logRecordCapacity: 0,
  resourceCount: 1,
  traceCount: 1,
  spanCount: 3,
  metricInstrumentCount: 0,
  metricPointCount: 0,
  logRecordCount: 0,
  droppedTraceCount: 0,
  droppedSpanCount: 0,
  droppedMetricPointCount: 0,
  droppedLogRecordCount: 0
};

const isEmpty = new URLSearchParams(window.location.search).has("empty");
const detail = { trace, resources, spans: isEmpty ? [] : spans };
const api = {
  backend: {
    baseUrl: "https://backend.example/",
    http: {
      getJson: async (url: string) => url.endsWith("/storage") ? storage : detail,
      postJson: async (url: string) => {
        if (url.endsWith("/traces/search")) return { items: [trace], droppedCount: 0 };
        if (url.endsWith("/resources/search")) return { items: resources, droppedCount: 0 };
        return {};
      }
    }
  },
  navigation: { add: () => undefined },
  routes: { add: () => undefined },
  panels: { add: () => undefined }
} as unknown as ElsaStudioModuleApi;

register(api);

const rootElement = document.getElementById("root")!;
const surface = new URLSearchParams(window.location.search).get("surface");
rootElement.dataset.surface = surface === "panel" ? "panel" : "page";
createRoot(rootElement).render(surface === "panel" ? <OpenTelemetryPanel /> : <OpenTelemetryPage />);