import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityExecutionDescendants, getActivityExecutionInspection, getActivityExecutionLayout } from "../api/runtime";
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
import type { ExecutableGraphNodeFacts } from "../executableGraph";
import { flowchartActivity, flowchartNode, forEachActivity, forEachNode, leafNode, writeLine } from "./fixtures";

vi.mock("../api/runtime", async importOriginal => ({
  ...(await importOriginal<typeof import("../api/runtime")>()),
  getActivityExecutionInspection: vi.fn(),
  getActivityExecutionDescendants: vi.fn(),
  getActivityExecutionLayout: vi.fn()
}));

let active: { root: Root; container: HTMLElement } | null = null;
let restoreClipboard: (() => void) | null = null;

afterEach(() => {
  if (active) {
    flushSync(() => active!.root.unmount());
    active.container.remove();
    active = null;
  }
  restoreClipboard?.();
  restoreClipboard = null;
  vi.mocked(getActivityExecutionInspection).mockReset();
  vi.mocked(getActivityExecutionDescendants).mockReset();
  vi.mocked(getActivityExecutionLayout).mockReset();
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

function installClipboard(writeText: (value: string) => Promise<void>) {
  const descriptor = Object.getOwnPropertyDescriptor(navigator, "clipboard");
  Object.defineProperty(navigator, "clipboard", { configurable: true, value: { writeText } });
  restoreClipboard = () => {
    if (descriptor) Object.defineProperty(navigator, "clipboard", descriptor);
    else Reflect.deleteProperty(navigator, "clipboard");
  };
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
  it("separates outer Boundary lifecycle from Descendant aggregate and reads its pinned historical layout", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue({
      ...inspection([]),
      boundary: {
        kind: "ReusableActivity",
        definitionId: "invoice-definition",
        definitionVersionId: "invoice-version-2",
        version: "2.0.0",
        templateHash: "sha256:invoice-v2",
        invocationOrigin: [{ kind: "TemplateBoundary", id: "invoice-version-2" }],
        executionScopeId: "scope-invoice",
        hasChildren: true,
        directChildCount: 1,
        committedDescendantCount: 2,
        aggregate: {
          status: "Completed",
          total: 2,
          scheduled: 0,
          running: 0,
          suspended: 0,
          completed: 2,
          faulted: 0,
          cancelled: 0,
          blockingIncidentCount: 0,
          retryCount: 0,
          lastExecutionSequence: 3
        },
        layoutAvailable: true
      }
    });
    vi.mocked(getActivityExecutionDescendants).mockResolvedValue({
      root: {
        workflowExecutionId: "wf-1",
        activityExecutionId: "ae-1",
        executionScopeId: "scope-invoice",
        definitionVersionId: "invoice-version-2",
        templateHash: "sha256:invoice-v2"
      },
      committedThroughSequence: 3,
      effectiveLimit: 100,
      items: [{
        activityExecutionId: "ae-child",
        workflowExecutionId: "wf-1",
        executableNodeId: "invoice-write",
        authoredActivityId: "write",
        activityType: "Elsa.WriteLine",
        activityTypeVersion: "1",
        status: "Completed",
        executionSequence: 2,
        scheduledAt: "2026-07-09T10:00:01Z",
        relativeDepth: 1,
        outcomeNames: [],
        bookmarkCount: 0,
        incidentCount: 0,
        blockingIncidentCount: 0,
        metadata: {}
      }],
      nextCursor: null
    });
    vi.mocked(getActivityExecutionLayout).mockResolvedValue({
      workflowExecutionId: "wf-1",
      activityExecutionId: "ae-1",
      artifactId: "artifact-workflow",
      sourceReferenceId: "source-reference-published",
      selection: "ExecutedReference",
      boundaryOrigin: [{ kind: "TemplateBoundary", id: "invoice-version-2" }],
      templateHash: "sha256:invoice-v2",
      nodes: [{
        templateNodeId: "template-write",
        authoredActivityId: "write",
        executableNodeId: "invoice-write",
        x: 120,
        y: 80,
        hasPinnedGeometry: true
      }],
      connections: [],
      nestedBoundaries: []
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Pinned historical layout"));
    expect(container.textContent).toContain("Boundary lifecycle");
    expect(container.textContent).toContain("Descendant aggregate");
    expect(container.textContent).toContain("source-reference-published");
    expect(container.textContent).toContain("invoice-write");
    expect(getActivityExecutionDescendants).toHaveBeenCalledWith(context, "wf-1", "ae-1");
    expect(getActivityExecutionLayout).toHaveBeenCalledWith(context, "wf-1", "ae-1");
  });

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

    const inputSection = [...container.querySelectorAll<HTMLElement>(".wf-instance-section")]
      .find(section => section.querySelector("h4")?.textContent?.includes("Inputs"));
    expect(inputSection?.querySelectorAll("[role=listitem]")).toHaveLength(1);
    expect(inputSection?.querySelector(".wf-runtime-evidence-count")?.textContent).toBe("1");
    expect(inputSection?.querySelector(".wf-runtime-capture-mode")?.textContent).toBe("Paired evidence");
    expect(inputSection?.querySelector(".wf-runtime-input .wf-runtime-capture-mode")).toBeNull();
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

  it("pairs runtime evidence with pinned authored source and structured compiled behavior", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      {
        name: "Message",
        subject: "ActivityInput",
        inputKey: "message-key",
        evaluationId: "invoke-1",
        phase: "invoke",
        sequence: 1,
        captureMode: "DiagnosticSnapshot",
        state: "captured",
        type: { typeName: "System.String" },
        capturedAt: "2026-07-09T10:00:01Z",
        snapshot: { kind: "string", preview: "Hello at runtime", length: 16, truncated: false },
        captureReason: "Diagnostic snapshot captured.",
        isSensitive: false,
        access: "visible",
        metadata: {}
      }
    ]));
    const executableNodeFacts: ExecutableGraphNodeFacts = {
      executableNodeId: "node-1",
      authoredActivityId: "write-line",
      activityType: activity.activityType,
      activityTypeVersion: activity.activityTypeVersion,
      structureKind: null,
      available: true,
      authoredInputsAccess: "visible",
      authoredInputs: [{ executableNodeId: "node-1", inputKey: "message-key", expressionType: "JavaScript", value: "variables.message" }],
      inputBindings: [{
        inputKey: "message-key",
        inputName: "Message",
        source: "Expression",
        expression: { language: "JavaScript", expression: "variables.message" },
        summary: "legacy summary must not render"
      }]
    };
    const expressionEditor: StudioExpressionEditorContribution = {
      id: "test.javascript",
      supports: context => context.syntax === "JavaScript",
      surfaces: {},
      sourceRenderer: {
        compact: ({ context }) => <strong>{String(context.value)}</strong>,
        expanded: ({ context }) => <strong>JavaScript source: {String(context.value)}</strong>
      }
    };
    const pairedCatalog: ActivityCatalogItem[] = [{
      ...catalog[0]!,
      inputs: [{ referenceKey: "message-key", name: "Message", displayName: "Message", typeName: "System.String" }]
    }];

    const container = render(
      <WorkflowActivityExecutionDetails
        context={context}
        activity={activity}
        activityCatalog={pairedCatalog}
        executableNodeFacts={executableNodeFacts}
        expressionEditors={[expressionEditor]}
      />
    );

    await waitFor(() => expect(container.textContent).toContain("Hello at runtime"));
    expect(container.textContent).toContain("Evaluated at runtime");
    expect(container.textContent).toContain("JavaScript source: variables.message");
    expect(container.textContent).toContain("Compiled binding (Expression)");
    expect(container.textContent).not.toContain("legacy summary must not render");
    expect([...container.querySelectorAll(".wf-instance-section > h4, .wf-instance-section > header h4")].map(item => item.textContent?.replace(/\d+$/, "")))
      .toEqual(expect.arrayContaining(["Inputs", "Outputs"]));
  });

  it("keeps authored source hidden when source access is denied while runtime evidence remains visible", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([{
      name: "Message",
      subject: "ActivityInput",
      inputKey: "message-key",
      evaluationId: "invoke-1",
      phase: "invoke",
      sequence: 1,
      captureMode: "DiagnosticSnapshot",
      state: "captured",
      type: { typeName: "System.String" },
      capturedAt: "2026-07-09T10:00:01Z",
      snapshot: { kind: "string", preview: "Allowed runtime evidence", length: 24, truncated: false },
      captureReason: "Captured.",
      isSensitive: false,
      access: "visible",
      metadata: {}
    }]));

    const container = render(
      <WorkflowActivityExecutionDetails
        context={context}
        activity={activity}
        activityCatalog={[{ ...catalog[0]!, inputs: [{ referenceKey: "message-key", name: "Message", typeName: "System.String" }] }]}
        executableNodeFacts={{
          executableNodeId: "node-1",
          authoredActivityId: "write-line",
          activityType: activity.activityType,
          activityTypeVersion: activity.activityTypeVersion,
          structureKind: null,
          available: true,
          authoredInputsAccess: "permissionHidden",
          authoredInputs: [],
          inputBindings: []
        }}
      />
    );

    await waitFor(() => expect(container.textContent).toContain("Allowed runtime evidence"));
    expect(container.textContent).toContain("Authored source is hidden by source permissions.");
  });

  it("shows an empty state when no input snapshots exist", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("No declared inputs, pinned bindings, or runtime input evidence are available for this execution."));
    expect(container.textContent).toContain("No runtime output snapshots were recorded for this execution.");
  });

  it("prioritizes an activity summary and copies every metadata value", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([]));
    const writeText = vi.fn<(value: string) => Promise<void>>().mockResolvedValue(undefined);
    installClipboard(writeText);

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);
    const overview = container.querySelector<HTMLElement>(".wf-activity-overview")!;

    expect(overview.querySelector("h4")?.textContent).toBe("Write Line");
    expect(overview.querySelectorAll(".wf-activity-summary-grid .wf-activity-meta-item")).toHaveLength(3);
    expect(container.querySelector(".wf-activity-execution-details")?.hasAttribute("open")).toBe(false);
    expect(container.querySelectorAll(".wf-copy-button")).toHaveLength(10);

    const executionIdCopy = container.querySelector<HTMLButtonElement>("[aria-label='Copy activity execution ID']")!;
    executionIdCopy.click();

    await waitFor(() => {
      expect(writeText).toHaveBeenCalledWith("ae-1");
      expect(overview.querySelector("[role=status]")?.textContent).toBe("Copied activity execution ID.");
    });
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

  it("gives an unsupported scope owner no slot navigation, matching the editor's static placeholder", () => {
    // A leaf activity as root has no structure and no slots, so its designer support is "unsupported"
    // and the viewer renders the one-node placeholder canvas.
    const unsupportedVersion: WorkflowDefinitionVersionDetails = { ...definitionVersion, state: { rootActivity: leafNode("leaf-root") } };
    const navigated: ScopeFrame[][] = [];
    const canvas = buildInstanceCanvas(unsupportedVersion, instanceCatalog, instanceDetails([]), null, [], frames => navigated.push(frames));

    const placeholder = canvas.nodes.find(node => node.id === "leaf-root")!;
    // No child slots → the graph renders no badges, so slot entry is unreachable through the UI…
    expect(placeholder.data.childSlots).toEqual([]);
    // …and even a forced call plans no navigation (planSlotNavigation returns null for a slot the
    // owner does not expose), mirroring the editor's disabled badges on unsupported designers.
    placeholder.data.onEnterSlot!({ id: "bogus", label: "Bogus", property: "bogus", cardinality: "single", mode: "generic", activities: [] });
    expect(navigated).toEqual([]);
  });

  it("attaches runtime evidence overlays inside the descended canvas", () => {
    const frames = enterForEachBody();
    const execution: ActivityExecutionStateSummary = { ...activity, executableNodeId: "wl-1", authoredActivityId: "wl-1" };

    const descended = buildInstanceCanvas(definitionVersion, instanceCatalog, instanceDetails([execution]), null, frames, () => {});
    const overlaid = descended.nodes.find(node => node.id === "wl-1")!;
    expect(overlaid.data.runtime?.status).toBe("Completed");
    expect(descended.nodes.find(node => node.id === "wl-2")!.data.runtime).toBeUndefined();
  });

  it("renders a projected Flowchart connection as a focusable, named run-canvas edge", () => {
    const connectedRoot = flowchartNode("fc-connected", [leafNode("wl-1"), leafNode("wl-2")]);
    connectedRoot.structure = {
      ...connectedRoot.structure!,
      payload: {
        ...connectedRoot.structure!.payload,
        connections: [{ source: { nodeId: "wl-1", port: "Done" }, target: { nodeId: "wl-2" } }]
      }
    };
    const connectedVersion: WorkflowDefinitionVersionDetails = {
      ...definitionVersion,
      state: { rootActivity: connectedRoot },
      layout: [{ nodeId: "wl-1", x: 100, y: 120 }, { nodeId: "wl-2", x: 480, y: 120 }]
    };

    const canvas = buildInstanceCanvas(connectedVersion, instanceCatalog, instanceDetails([]), null, [], () => {});

    expect(canvas.nodes.map(node => ({ id: node.id, position: node.position }))).toEqual([
      { id: "wl-1", position: { x: 100, y: 120 } },
      { id: "wl-2", position: { x: 480, y: 120 } }
    ]);
    expect(canvas.edges).toHaveLength(1);
    expect(canvas.edges[0]).toMatchObject({
      source: "wl-1",
      target: "wl-2",
      sourceHandle: "Done",
      focusable: true,
      ariaRole: "button",
      ariaLabel: "Connection from Write Line (wl-1), Done output, to Write Line (wl-2). Not selected.",
      domAttributes: { "aria-pressed": false }
    });
  });
});
