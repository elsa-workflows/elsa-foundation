import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityExecutionInspection } from "../api/workflows";
import {
  WorkflowActivityExecutionDetails,
  WorkflowIncidentList,
  buildInstanceCanvas,
  formatSnapshotPayload,
  getIncidentStackTrace
} from "../workflow-editor/WorkflowInstances";
import type { ScopeFrame } from "../workflowAdapter";
import type {
  ActivityCatalogItem,
  ActivityExecutionInspection,
  ActivityExecutionStateSummary,
  IncidentStateSummary,
  WorkflowDefinitionVersionDetails,
  WorkflowInstanceDetails
} from "../workflowTypes";
import { flowchartActivity, flowchartNode, forEachActivity, forEachNode, leafNode, writeLine } from "./fixtures";

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

const incident: IncidentStateSummary = {
  incidentId: "incident-1",
  workflowExecutionId: "wf-1",
  activityExecutionId: "ae-1",
  executableNodeId: "node-1",
  severity: "Error",
  status: "Blocking",
  resolutionAction: "WaitForIntervention",
  failureType: "InputMaterializationFailed",
  message: "Input failed to evaluate.",
  createdAt: "2026-07-09T10:00:03Z",
  resolvedAt: null,
  isBlocking: true,
  metadata: {}
};

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
  it("loads and renders diagnostic snapshots for selected execution inputs and outputs", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Message",
        subject: "ActivityInput",
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:01Z",
        snapshot: { kind: "string", typeName: "String", preview: "Hello at runtime", length: 16, truncated: false },
        captureReason: "Diagnostic snapshot captured.",
        isSensitive: false,
        metadata: {}
      },
      {
        name: "Result",
        subject: "ActivityOutput",
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:02Z",
        snapshot: { kind: "object", typeName: "Result", properties: [{ name: "id", value: { kind: "string", preview: "customer-1", length: 10, truncated: false } }], truncated: false },
        captureReason: "Diagnostic snapshot captured.",
        isSensitive: false,
        metadata: {}
      }
    ]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Hello at runtime"));
    expect(getActivityExecutionInspection).toHaveBeenCalledWith(context, "wf-1", "ae-1");
    expect(container.textContent).toContain("Inputs");
    expect(container.textContent).toContain("Outputs");
    expect(container.textContent).toContain("Message");
    expect(container.textContent).toContain("Result");
    expect(container.textContent).toContain("customer-1");
    expect(container.textContent).toContain("System.String");
  });

  it("shows the capture reason when input values were omitted by policy", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Message",
        subject: "ActivityInput",
        captureMode: "None",
        state: "notCaptured",
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
    expect(container.textContent).toContain("No runtime output snapshots were recorded for this execution.");
  });

  it("renders redaction, truncation, permission-hidden, and payload reference markers", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Secret",
        subject: "ActivityInput",
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:01Z",
        snapshot: { kind: "redacted", reason: "sensitive-name", displayName: "Protected value" },
        captureReason: "Diagnostic snapshot captured.",
        isSensitive: true,
        metadata: {}
      },
      {
        name: "Archive",
        subject: "ActivityOutput",
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.Byte[]" },
        capturedAt: "2026-07-09T10:00:02Z",
        snapshot: {
          kind: "payloadReference",
          referenceKind: "blob",
          referenceId: "rpr_1",
          displayName: "Large file",
          contentType: "application/zip",
          size: 128,
          resolution: { canResolve: false, reason: "Reference resolution is not available in this release." }
        },
        captureReason: "Diagnostic snapshot captured.",
        isSensitive: false,
        metadata: {}
      },
      {
        name: "Hidden",
        subject: "ActivityOutput",
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:03Z",
        snapshot: { kind: "permissionHidden", reason: "missing-permission", requiredPermission: "workflows.runtimeEvidence.viewSnapshots" },
        captureReason: "Hidden.",
        isSensitive: false,
        metadata: {}
      }
    ]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("redacted: sensitive-name"));
    expect(container.textContent).toContain("Marked sensitive by runtime evidence.");
    expect(container.textContent).toContain("Large file");
    expect(container.textContent).toContain("application/zip");
    expect(container.textContent).toContain("Reference resolution is not available in this release.");
    expect(container.textContent).toContain("permission Hidden: missing-permission");
  });
});

describe("WorkflowIncidentList", () => {
  it("renders a full stack trace disclosure when incident metadata includes one", () => {
    const stackTrace = "System.InvalidOperationException: No value\n   at Elsa.Tests.WriteLine.Execute()";
    const container = render(<WorkflowIncidentList incidents={[{ ...incident, metadata: { stackTrace } }]} />);

    const details = container.querySelector("details");
    expect(details).not.toBeNull();
    expect(details?.textContent).toContain("System.InvalidOperationException: No value");
    expect(details?.querySelector("pre")?.textContent).toBe(stackTrace);
  });

  it("does not render a stack trace disclosure when none is available", () => {
    const container = render(<WorkflowIncidentList incidents={[incident]} />);

    expect(container.querySelector("details")).toBeNull();
    expect(container.textContent).toContain("Input failed to evaluate.");
  });
});

describe("getIncidentStackTrace", () => {
  it("prefers direct stack trace fields over metadata", () => {
    expect(getIncidentStackTrace({
      ...incident,
      stackTrace: "direct stack",
      metadata: { stackTrace: "metadata stack" }
    })).toBe("direct stack");
  });

  it("reads runtime stack trace metadata keys", () => {
    expect(getIncidentStackTrace({
      ...incident,
      metadata: { "runtime.faultStackTrace": "runtime stack" }
    })).toBe("runtime stack");
  });
});

describe("formatSnapshotPayload", () => {
  it("formats structured payloads as readable JSON", () => {
    expect(formatSnapshotPayload({ message: "hello", count: 2 })).toBe("{\n  \"message\": \"hello\",\n  \"count\": 2\n}");
  });
});

describe("buildInstanceCanvas", () => {
  // ForEach whose Body holds a Flowchart with two leaves — the editor's descend policy must apply to
  // run inspection too: entering Body shows the flowchart's contents, not a one-node canvas.
  const innerFlowchart = flowchartNode("fc-inner", [leafNode("wl-1"), leafNode("wl-2")]);
  const foreach = forEachNode("fe-1", innerFlowchart);
  const root = flowchartNode("fc-root", [foreach]);
  const instanceCatalog = [writeLine, flowchartActivity, forEachActivity];

  const definitionVersion: WorkflowDefinitionVersionDetails = {
    id: "def-v1",
    version: "1.0.0",
    definition: { id: "def-1", name: "Test", description: null, createdAt: "2026-07-09T10:00:00Z", lastModifiedAt: "2026-07-09T10:00:00Z" },
    state: { rootActivity: root },
    layout: []
  };

  const instanceDetails = (activities: ActivityExecutionStateSummary[]): WorkflowInstanceDetails => ({
    instance: {} as WorkflowInstanceDetails["instance"],
    activities,
    incidents: []
  });

  function enterForEachBody() {
    const navigated: ScopeFrame[][] = [];
    const canvas = buildInstanceCanvas(definitionVersion, instanceCatalog, instanceDetails([]), null, [], frames => navigated.push(frames));
    const feNode = canvas.nodes.find(node => node.id === "fe-1")!;
    feNode.data.onEnterSlot!(feNode.data.childSlots[0]);
    expect(navigated).toHaveLength(1);
    return navigated[0];
  }

  it("descends through a single flowchart Body child on slot entry", () => {
    const frames = enterForEachBody();

    // Hidden descent hop through the ForEach, visible leaf frame on the flowchart carrying the crumb.
    expect(frames.map(frame => ({ ownerNodeId: frame.ownerNodeId, label: frame.label }))).toEqual([
      { ownerNodeId: "fe-1", label: "" },
      { ownerNodeId: "fc-inner", label: "For Each / Body" }
    ]);

    const descended = buildInstanceCanvas(definitionVersion, instanceCatalog, instanceDetails([]), null, frames, () => {});
    expect(descended.nodes.map(node => node.id).sort()).toEqual(["wl-1", "wl-2"]);
  });

  it("attaches runtime evidence overlays inside the descended canvas", () => {
    const frames = enterForEachBody();
    const execution: ActivityExecutionStateSummary = { ...activity, executableNodeId: "wl-1", authoredActivityId: "wl-1" };

    const descended = buildInstanceCanvas(definitionVersion, instanceCatalog, instanceDetails([execution]), null, frames, () => {});
    const overlaid = descended.nodes.find(node => node.id === "wl-1")!;
    expect(overlaid.data.runtime?.status).toBe("Completed");
    expect(descended.nodes.find(node => node.id === "wl-2")!.data.runtime).toBeUndefined();
  });
});
