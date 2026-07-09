import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityExecutionInspection } from "../api/workflows";
import { WorkflowActivityExecutionDetails, formatSnapshotPayload } from "../workflow-editor/WorkflowInstances";
import type { ActivityCatalogItem, ActivityExecutionInspection, ActivityExecutionStateSummary } from "../workflowTypes";

vi.mock("../api/workflows", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/workflows")>()),
  getActivityExecutionInspection: vi.fn()
}));

let active: { root: Root; container: HTMLElement } | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  vi.mocked(getActivityExecutionInspection).mockReset();
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

async function waitFor(assertion: () => void) {
  let lastError: unknown;

  for (let attempt = 0; attempt < 20; attempt++) {
    try {
      assertion();
      return;
    } catch (error) {
      lastError = error;
      await new Promise(resolve => setTimeout(resolve, 0));
    }
  }

  throw lastError;
}

const context = {} as StudioEndpointContext;

const activity: ActivityExecutionStateSummary = {
  activityExecutionId: "ae-1",
  workflowExecutionId: "wf-1",
  executableNodeId: "node-1",
  authoredActivityId: "write-line",
  activityType: "Elsa.Activities.Primitives.Activities.WriteLine",
  activityTypeVersion: "1.0.0",
  status: "Completed",
  subStatus: null,
  scheduledAt: "2026-07-09T10:00:00Z",
  startedAt: "2026-07-09T10:00:01Z",
  completedAt: "2026-07-09T10:00:02Z",
  schedulingActivityExecutionId: null,
  parentActivityExecutionId: null,
  branchId: null,
  iterationId: null,
  callStackDepth: null,
  bookmarkIds: [],
  incidentIds: [],
  faultCount: 0,
  aggregateFaultCount: 0,
  metadata: {}
};

const catalog: ActivityCatalogItem[] = [
  {
    activityVersionId: "write-line-v1",
    activityTypeKey: activity.activityType,
    version: "1.0.0",
    category: "Primitives",
    displayName: "Write Line",
    description: null,
    executionType: "Action",
    inputs: [],
    outputs: [],
    designFacets: []
  }
];

function inspection(valueSnapshots: ActivityExecutionInspection["valueSnapshots"]): ActivityExecutionInspection {
  return {
    activityExecutionId: activity.activityExecutionId,
    workflowExecutionId: activity.workflowExecutionId,
    executableNodeId: activity.executableNodeId,
    authoredActivityId: activity.authoredActivityId,
    activityType: activity.activityType,
    activityTypeVersion: activity.activityTypeVersion,
    status: activity.status,
    subStatus: activity.subStatus,
    executionSequence: 1,
    scheduledAt: activity.scheduledAt,
    startedAt: activity.startedAt,
    completedAt: activity.completedAt,
    firstCheckpointId: "checkpoint:start",
    lastCheckpointId: "checkpoint:complete",
    lastCommittedAt: activity.completedAt,
    provenance: {
      parentActivityExecutionId: null,
      schedulingActivityExecutionId: null,
      schedulingWorkflowExecutionId: activity.workflowExecutionId,
      branchId: null,
      iterationId: null,
      executionPathId: null,
      executionScopeId: null,
      schedulingCause: null,
      metadata: {}
    },
    outcomeNames: [],
    bookmarks: [],
    incidents: [],
    valueSnapshots,
    metadata: {}
  };
}

describe("WorkflowActivityExecutionDetails", () => {
  it("loads and renders captured runtime input payloads for the selected execution", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Message",
        subject: "ActivityInput",
        captureMode: "Payload",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:01Z",
        payload: "Hello at runtime",
        captureReason: "Policy captured payload.",
        isSensitive: false,
        metadata: {}
      },
      {
        name: "Result",
        subject: "ActivityOutput",
        captureMode: "Payload",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:02Z",
        payload: "ignored output",
        captureReason: "Policy captured payload.",
        isSensitive: false,
        metadata: {}
      }
    ]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Hello at runtime"));
    expect(getActivityExecutionInspection).toHaveBeenCalledWith(context, "wf-1", "ae-1");
    expect(container.textContent).toContain("Message");
    expect(container.textContent).toContain("System.String");
    expect(container.textContent).not.toContain("ignored output");
  });

  it("shows the capture reason when input values were omitted by policy", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Message",
        subject: "ActivityInput",
        captureMode: "None",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:01Z",
        payload: null,
        captureReason: "Input and output snapshots are omitted by default.",
        isSensitive: false,
        metadata: {}
      }
    ]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Input and output snapshots are omitted by default."));
    expect(container.textContent).toContain("None");
    expect(container.textContent).not.toContain("null");
  });

  it("shows an empty state when no input snapshots exist", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("No runtime input snapshots were recorded for this execution."));
  });
});

describe("formatSnapshotPayload", () => {
  it("formats structured payloads as readable JSON", () => {
    expect(formatSnapshotPayload({ message: "hello", count: 2 })).toBe("{\n  \"message\": \"hello\",\n  \"count\": 2\n}");
  });
});
