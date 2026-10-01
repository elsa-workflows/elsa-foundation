import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { StudioEndpointContext, StudioExpressionEditorContribution } from "@elsa-workflows/studio-sdk";
import { afterEach, describe, expect, it, vi } from "vitest";
import { getActivityExecutionDescendants, getActivityExecutionInspection, getActivityExecutionLayout } from "../api/runtime";
import { getActivityExecutionValuePayload } from "../api/activityExecutionValuePayload";
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
  ActivityExecutionInspectionValueSnapshot,
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
  getActivityExecutionLayout: vi.fn(),
  getActivityExecutionValuePayload: vi.fn()
}));

vi.mock("../api/activityExecutionValuePayload", () => ({
  getActivityExecutionValuePayload: vi.fn()
}));

vi.mock("../workflow-editor/ReusableBoundaryInspector", () => ({
  ReusableBoundaryInspector: ({ inspection }: { inspection: ActivityExecutionInspection }) => (
    <section>
      <h4>Boundary lifecycle</h4>
      <span>{inspection.boundary?.definitionVersionId}</span>
      <h4>Descendant aggregate</h4>
      <span>Pinned historical layout</span>
    </section>
  )
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
  vi.mocked(getActivityExecutionValuePayload).mockReset();
});

function render(ui: React.ReactElement) {
  const container = document.createElement("div");
  document.body.appendChild(container);
  const root = createRoot(container);
  flushSync(() => root.render(ui));
  active = { root, container };
  return container;
}

function rerender(ui: React.ReactElement) {
  if (!active) throw new Error("No active render to update.");
  flushSync(() => active!.root.render(ui));
  return active.container;
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

function valueEvidence(overrides: Partial<ActivityExecutionInspectionValueSnapshot> = {}): ActivityExecutionInspectionValueSnapshot {
  return {
    evidenceId: "evidence-1",
    name: "Captured value",
    subject: "ActivityInput",
    captureMode: "DiagnosticSnapshot",
    captureState: "diagnosticSnapshotCaptured",
    state: "captured",
    type: { kind: "alias", id: "Int32", schema: null },
    capturedAt: "2026-07-09T10:00:01Z",
    payload: null,
    snapshot: null,
    captureReason: "Diagnostic snapshot captured.",
    isSensitive: false,
    accessState: "resolutionAvailable",
    metadata: {},
    ...overrides
  };
}

describe("WorkflowActivityExecutionDetails", () => {
  it("uses frozen source-reference wording before the live catalog fallback", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([]));
    const container = render(
      <WorkflowActivityExecutionDetails
        context={context}
        activity={activity}
        activityCatalog={[{ ...catalog[0]!, displayName: "Renamed live catalog label" }]}
        executableNodeFacts={{
          executableNodeId: "node-1",
          authoredActivityId: "write-line",
          activityType: activity.activityType,
          activityTypeVersion: activity.activityTypeVersion,
          structureKind: null,
          available: true,
          outputCaptures: [],
          authoredInputsAccess: "visible",
          authoredInputs: [],
          inputBindings: [],
          presentation: {
            nodeId: "write-line",
            displayName: "Frozen notify step",
            description: "Wording captured with this source reference."
          }
        }}
      />
    );

    expect(container.querySelector(".wf-activity-overview h4")?.textContent)
      .toBe("Frozen notify step");
    expect(container.querySelector(".wf-activity-overview-description")?.textContent)
      .toBe("Wording captured with this source reference.");
    expect(container.textContent).not.toContain("Renamed live catalog label");
  });

  it("defers reusable Boundary inspection to its independently lazy Runtime Evidence chunk", async () => {
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
    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Loading reusable boundary inspector"));
    expect(getActivityExecutionInspection).toHaveBeenCalledWith(context, "wf-1", "ae-1", expect.any(AbortSignal));
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
    expect(getActivityExecutionInspection).toHaveBeenCalledWith(context, "wf-1", "ae-1", expect.any(AbortSignal));
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

  it("resolves metadata-only detail evidence through the payload capability and renders its diagnostic tree", async () => {
    const snapshot = valueEvidence();
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([snapshot]));
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "evidence-1",
      captureMode: "DiagnosticSnapshot",
      payload: {
        kind: "object",
        typeName: "Result",
        properties: [
          { name: "status", value: { kind: "string", preview: "diagnostic tree readable value", length: 28, truncated: false } },
          { name: "empty", value: { kind: "string", preview: "", length: 0, truncated: false } }
        ],
        truncated: false
      }
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("diagnostic tree readable value"));
    expect(getActivityExecutionValuePayload).toHaveBeenCalledWith(
      context,
      "wf-1",
      "ae-1",
      "evidence-1",
      "DiagnosticSnapshot",
      expect.any(AbortSignal)
    );
    expect(container.textContent).toContain("Int32");
    expect(container.textContent).toContain('""');
    expect(container.querySelector(".wf-runtime-snapshot-node")).not.toBeNull();
    expect(container.textContent).not.toContain('"kind": "object"');
  });

  it("renders a resolved Payload as raw data even when the object has a kind property", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      captureMode: "Payload",
      captureState: "payloadCaptured"
    })]));
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "evidence-1",
      captureMode: "Payload",
      payload: { kind: "string", preview: "This is raw payload data." }
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain('"kind": "string"'));
    expect(container.textContent).toContain("This is raw payload data.");
    expect(container.querySelector(".wf-runtime-snapshot-node")).toBeNull();
  });

  it("requires a fresh explicit action for sensitive evidence after a selection change with slash-containing IDs", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      name: "Secret",
      captureMode: "Payload",
      captureState: "payloadCaptured",
      isSensitive: true
    })]));
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "evidence-1",
      captureMode: "Payload",
      payload: "TOP_SECRET_RAW_VALUE"
    });

    const firstActivity = { ...activity, workflowExecutionId: "wf/a", activityExecutionId: "b" };
    const secondActivity = { ...activity, workflowExecutionId: "wf", activityExecutionId: "a/b" };
    const container = render(<WorkflowActivityExecutionDetails context={context} activity={firstActivity} activityCatalog={catalog} />);
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(getActivityExecutionValuePayload).not.toHaveBeenCalled();
    expect(container.textContent).not.toContain("TOP_SECRET_RAW_VALUE");
    const showButton = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Show captured value");
    expect(showButton).toBeDefined();
    showButton!.click();

    await waitFor(() => expect(container.textContent).toContain("TOP_SECRET_RAW_VALUE"));
    expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1);

    rerender(<WorkflowActivityExecutionDetails context={context} activity={secondActivity} activityCatalog={catalog} />);
    await waitFor(() => expect(container.textContent).toContain("Show captured value"));
    expect(container.textContent).not.toContain("TOP_SECRET_RAW_VALUE");
    expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1);
    const nextShowButton = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Show captured value")!;
    nextShowButton.click();
    await waitFor(() => expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(2));
    expect(getActivityExecutionValuePayload).toHaveBeenLastCalledWith(
      context, "wf", "a/b", "evidence-1", "Payload", expect.any(AbortSignal));
  });

  it("does not request redacted, unavailable, metadata-only, off, or permission-required evidence", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([
      valueEvidence({ evidenceId: "redacted", name: "Redacted", captureMode: "Payload", captureState: "payloadCaptured", payload: "REDACTED_INLINE_SECRET", accessState: "redacted" }),
      valueEvidence({ evidenceId: "permission", name: "Needs permission", captureMode: "Payload", captureState: "payloadCaptured", payload: "NO_PERMISSION_INLINE_SECRET", accessState: "resolutionPermissionRequired" }),
      valueEvidence({ evidenceId: "sensitive-permission", name: "Sensitive permission", captureMode: "Payload", captureState: "payloadCaptured", payload: "SENSITIVE_PERMISSION_SECRET", accessState: "resolutionPermissionRequired", isSensitive: true }),
      valueEvidence({ evidenceId: "metadata", name: "Metadata only", captureMode: "Metadata", captureState: "metadataOnly", payload: "METADATA_INLINE_SECRET", accessState: "unavailable", captureReason: "Policy is set to Metadata." }),
      valueEvidence({ evidenceId: "off", name: "Off", captureMode: "None", captureState: "notCaptured", payload: "OFF_INLINE_SECRET", accessState: "unavailable", captureReason: "Input capture is off." })
    ]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Additional permission is required"));
    expect(container.textContent).toContain("Policy is set to Metadata.");
    expect(container.textContent).toContain("Input capture is off.");
    expect(container.textContent).not.toContain("INLINE_SECRET");
    expect(container.textContent).not.toContain("SENSITIVE_PERMISSION_SECRET");
    expect(container.textContent).not.toContain("Show captured value");
    expect(getActivityExecutionValuePayload).not.toHaveBeenCalled();
  });

  it("cancels a payload request and drops its result when the selected activity changes", async () => {
    const oldSnapshot = valueEvidence({ evidenceId: "old-evidence" });
    vi.mocked(getActivityExecutionInspection).mockImplementation(async (_context, _workflowExecutionId, activityExecutionId) =>
      inspection(activityExecutionId === "ae-1" ? [oldSnapshot] : []));

    let resolvePayload!: (value: { evidenceId: string; captureMode: string; payload: unknown }) => void;
    let payloadSignal: AbortSignal | undefined;
    vi.mocked(getActivityExecutionValuePayload).mockImplementation((_context, _workflowExecutionId, _activityExecutionId, _evidenceId, _captureMode, signal) => {
      payloadSignal = signal;
      return new Promise(resolve => { resolvePayload = resolve; });
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);
    await waitFor(() => expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1));
    expect(payloadSignal?.aborted).toBe(false);

    rerender(<WorkflowActivityExecutionDetails context={context} activity={{ ...activity, activityExecutionId: "ae-2" }} activityCatalog={catalog} />);
    await waitFor(() => expect(getActivityExecutionInspection).toHaveBeenCalledWith(context, "wf-1", "ae-2", expect.any(AbortSignal)));
    expect(payloadSignal?.aborted).toBe(true);
    resolvePayload({ evidenceId: "old-evidence", captureMode: "DiagnosticSnapshot", payload: "STALE_OLD_ACTIVITY_VALUE" });
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.textContent).not.toContain("STALE_OLD_ACTIVITY_VALUE");
    expect(container.textContent).toContain("No declared inputs, pinned bindings, or runtime input evidence are available for this execution.");
  });

  it("drops a resolved value after the same evidence is redacted by a refreshed host context", async () => {
    const refreshedContext = {} as StudioEndpointContext;
    vi.mocked(getActivityExecutionInspection)
      .mockResolvedValueOnce(inspection([valueEvidence()]))
      .mockResolvedValueOnce(inspection([valueEvidence({
        snapshot: { kind: "redacted", reason: "permission-changed" },
        accessState: "redacted",
        captureReason: "Runtime value evidence is redacted."
      })]));

    let payloadSignal: AbortSignal | undefined;
    vi.mocked(getActivityExecutionValuePayload).mockImplementation(async (_context, _workflowExecutionId, _activityExecutionId, _evidenceId, _captureMode, signal) => {
      payloadSignal = signal;
      return { evidenceId: "evidence-1", captureMode: "DiagnosticSnapshot", payload: "VALUE_BEFORE_PERMISSION_CHANGE" };
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);
    await waitFor(() => expect(container.textContent).toContain("VALUE_BEFORE_PERMISSION_CHANGE"));

    rerender(<WorkflowActivityExecutionDetails context={refreshedContext} activity={activity} activityCatalog={catalog} />);
    await waitFor(() => expect(container.textContent).toContain("Runtime value evidence is redacted."));

    expect(payloadSignal?.aborted).toBe(true);
    expect(container.textContent).not.toContain("VALUE_BEFORE_PERMISSION_CHANGE");
    expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(1);
  });

  it.each([
    ["metadata-only detail", "DiagnosticSnapshot", false],
    ["inline diagnostic", "DiagnosticSnapshot", true],
    ["inline payload", "Payload", true]
  ] as const)("handles loading and retry for %s evidence without using unauthorized inline fields", async (_label, captureMode, inline) => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      subject: "ActivityOutput", captureMode,
      captureState: captureMode === "Payload" ? "payloadCaptured" : "diagnosticSnapshotCaptured",
      ...(inline ? {
        payload: "INLINE_UNAUTHORIZED_VALUE",
        snapshot: { kind: "string", preview: "INLINE_UNAUTHORIZED_VALUE" }
      } : {})
    })]));
    let rejectPending!: (reason: unknown) => void;
    const pending = new Promise<{ evidenceId: string; captureMode: string; payload: unknown }>((_resolve, reject) => {
      rejectPending = reject;
    });
    vi.mocked(getActivityExecutionValuePayload)
      .mockImplementationOnce(() => pending)
      .mockResolvedValueOnce({ evidenceId: "evidence-1", captureMode, payload: "retried value" });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.textContent).toContain("Resolving captured value..."));
    expect(container.textContent).not.toContain("INLINE_UNAUTHORIZED_VALUE");
    rejectPending(new Error("temporary payload service failure"));
    await waitFor(() => expect(container.textContent).toContain("temporary payload service failure"));
    expect(container.textContent).not.toContain("INLINE_UNAUTHORIZED_VALUE");
    const retryButton = [...container.querySelectorAll<HTMLButtonElement>("button")]
      .find(button => button.textContent === "Retry");
    expect(retryButton).toBeDefined();
    retryButton!.click();

    await waitFor(() => expect(container.textContent).toContain("retried value"));
    expect(getActivityExecutionValuePayload).toHaveBeenCalledTimes(2);
    expect(container.textContent).not.toContain("INLINE_UNAUTHORIZED_VALUE");
  });

  it.each(["visible", "allowed"])("preserves an authorized inline null payload with %s access", async accessState => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      captureMode: "Payload",
      captureState: "payloadCaptured",
      accessState,
      payload: null
    })]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.querySelector(".wf-runtime-input-value")?.textContent).toBe("null"));
    expect(getActivityExecutionValuePayload).not.toHaveBeenCalled();
  });

  it("wraps compact resolved Payload strings so narrow inspectors can show the entire value", async () => {
    const payload = "A compact captured value with enough content to span multiple lines in the narrow activity inspector.";
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      subject: "ActivityOutput", captureMode: "Payload", captureState: "payloadCaptured"
    })]));
    vi.mocked(getActivityExecutionValuePayload).mockResolvedValue({
      evidenceId: "evidence-1", captureMode: "Payload", payload
    });

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.querySelector(".wf-runtime-input-value")?.textContent).toBe(payload));
    expect(container.querySelector(".wf-runtime-input-value")?.classList.contains("wf-runtime-snapshot-value")).toBe(true);
  });

  it.each([undefined, "captured"] as const)("uses metadata-only captureState in the input summary with legacy state %s", async state => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([valueEvidence({
      captureMode: "Metadata", captureState: "metadataOnly", state,
      accessState: "visible", payload: undefined, snapshot: null, captureReason: ""
    })]));

    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={catalog} />);

    await waitFor(() => expect(container.querySelector(".wf-input-inspection-content > .wf-instance-note")?.textContent).toContain("Metadata only"));
    expect([...container.querySelectorAll(".wf-input-inspection-preview code")].map(node => node.textContent)).toContain("metadata Only");
  });

  it("retains native input disclosures with distinct controlled regions for punctuation-containing keys", async () => {
    vi.mocked(getActivityExecutionInspection).mockResolvedValue(inspection([]));
    const pairedCatalog: ActivityCatalogItem[] = [{
      ...catalog[0]!,
      inputs: [
        { referenceKey: "a.b", name: "First", typeName: "System.String" },
        { referenceKey: "a-b", name: "Second", typeName: "System.String" }
      ]
    }];
    const container = render(<WorkflowActivityExecutionDetails context={context} activity={activity} activityCatalog={pairedCatalog} />);
    await waitFor(() => expect(container.textContent).toContain("No runtime output snapshots were recorded"));

    const summaries = [...container.querySelectorAll<HTMLElement>(".wf-input-inspection-summary")];
    const ids = summaries.map(summary => summary.getAttribute("aria-controls"));
    expect(summaries).toHaveLength(2);
    expect(new Set(ids).size).toBe(2);
    for (const summary of summaries) {
      const disclosure = summary.parentElement!;
      expect(disclosure.tagName).toBe("DETAILS");
      expect(disclosure.hasAttribute("role")).toBe(false);
      expect(disclosure.closest("[role=listitem]")).not.toBe(disclosure);
      const region = document.getElementById(summary.getAttribute("aria-controls")!);
      expect(region?.querySelector("section")?.getAttribute("aria-label"))
        .toBe(`${summary.querySelector("strong")!.textContent} runtime evidence`);
    }
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
      outputCaptures: [],
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
          outputCaptures: [],
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
