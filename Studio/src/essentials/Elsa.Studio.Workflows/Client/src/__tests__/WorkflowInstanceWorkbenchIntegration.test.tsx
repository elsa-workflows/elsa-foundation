import React from "react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import type { ReactFlowInstance } from "@xyflow/react";
import type { StudioAiContributionApi, StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { WorkflowInstanceDetailsWorkbench } from "../workflow-editor/WorkflowInstances";
import type {
  ActivityExecutionInspection,
  ActivityExecutionStateSummary,
  ActivityCatalogItem,
  IncidentStateSummary,
  WorkflowExecutableDetails,
  WorkflowExecutableNode,
  WorkflowInstanceDetails,
  WorkflowInstanceSummary
} from "../workflowTypes";
import { bpmnActivity, sequenceActivity, writeLine } from "./fixtures";

const api = vi.hoisted(() => ({
  getActivityExecutionInspection: vi.fn(),
  getExecutable: vi.fn(),
  getExecutableInputSources: vi.fn(),
  getWorkflowInstance: vi.fn(),
  listWorkflowInstances: vi.fn(),
  supportsActivityExecutionInspection: vi.fn(),
  supportsWorkflowInstanceHealthFilter: vi.fn(),
  listActivities: vi.fn()
}));
const mockFlowInstance = vi.hoisted(() => ({ fitView: vi.fn() }));
const useActualReactFlow = vi.hoisted(() => ({ enabled: false }));

vi.mock("../api/runtime", () => ({
  ...api,
  WorkflowInstanceHealthFilterUnavailableError: class extends Error {}
}));
vi.mock("../api/activityDesign", () => ({ listActivities: api.listActivities }));
vi.mock("@xyflow/react", async importOriginal => {
  const actual = await importOriginal<typeof import("@xyflow/react")>();
  return {
    ...actual,
    ReactFlow: (props: {
      nodes: Array<{ id: string; selected?: boolean; data: Record<string, unknown> }>;
      edges?: unknown[];
      onInit?: (instance: ReactFlowInstance) => void;
      [key: string]: unknown;
    }) => {
      const { nodes, onInit } = props;
      React.useEffect(() => {
        if (!useActualReactFlow.enabled) onInit?.(mockFlowInstance as unknown as ReactFlowInstance);
      }, [onInit]);
      if (useActualReactFlow.enabled) {
        return React.createElement(actual.ReactFlow as React.ComponentType<Record<string, unknown>>, props);
      }
      return (
        <div className="wf-mock-react-flow">
          {nodes.map(node => {
            const runtimeNodeId = String(node.data.runtimeNodeId ?? node.id);
            return (
              <button
                type="button"
                key={node.id}
                tabIndex={-1}
                data-id={node.id}
                data-runtime-node-id={runtimeNodeId}
                data-flow-node-id={runtimeNodeId}
                data-selected={String(!!node.selected)}
              >
                {String(node.data.label ?? "")}
              </button>
            );
          })}
        </div>
      );
    },
    Background: () => null,
    Controls: () => null,
    MiniMap: () => null
  };
});

let container: HTMLDivElement;
let root: Root;
let observedWidth = 1100;

beforeEach(() => {
  window.localStorage.clear();
  window.localStorage.setItem("elsa-studio-run-detail-inspector-maximized", "true");
  useActualReactFlow.enabled = false;
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  observedWidth = 1100;
  vi.stubGlobal("ResizeObserver", class {
    private readonly callback: ResizeObserverCallback;
    private readonly observed = new WeakSet<Element>();
    private active = true;
    constructor(callback: ResizeObserverCallback) { this.callback = callback; }
    observe(target: Element) {
      if (this.observed.has(target)) return;
      this.observed.add(target);
      const isFlowNode = target.classList.contains("react-flow__node");
      setTimeout(() => {
        if (!this.active) return;
        this.callback([{
          target,
          contentRect: { width: isFlowNode ? 220 : observedWidth, height: isFlowNode ? 110 : 800 }
        } as ResizeObserverEntry], this as unknown as ResizeObserver);
      });
    }
    unobserve() {}
    disconnect() { this.active = false; }
  });

  const catalog = [sequenceActivity, { ...writeLine, inputs: [{
    referenceKey: "text",
    name: "Text",
    typeName: "string",
    displayName: "Text"
  }] } satisfies ActivityCatalogItem];
  api.listActivities.mockResolvedValue({ activities: catalog });
  api.getWorkflowInstance.mockResolvedValue(workflowDetails());
  api.getExecutable.mockResolvedValue(executable());
  api.getExecutableInputSources.mockResolvedValue({ authoredInputs: [], compiledInputs: [], access: "allowed" });
  api.supportsActivityExecutionInspection.mockResolvedValue(false);
  api.getActivityExecutionInspection.mockResolvedValue(activityInspection());
});

afterEach(() => {
  flushSync(() => root.unmount());
  container.remove();
  window.localStorage.clear();
  vi.unstubAllGlobals();
  vi.resetAllMocks();
});

describe("workflow run workbench incident navigation", () => {
  it("keeps measured BPMN runtime nodes and incident cues visible when incident navigation rebuilds overlays", async () => {
    useActualReactFlow.enabled = true;
    vi.stubGlobal("DOMMatrixReadOnly", class { readonly m22 = 1; });
    api.listActivities.mockResolvedValue({ activities: [sequenceActivity, bpmnActivity, writeLine] });
    api.getExecutable.mockResolvedValue(bpmnExecutable());
    const originalGetBoundingClientRect = HTMLElement.prototype.getBoundingClientRect;
    const nodeBounds = { x: 0, y: 0, width: 220, height: 110, top: 0, right: 220, bottom: 110, left: 0, toJSON: () => ({}) } as DOMRect;
    const boundsSpy = vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockImplementation(function (this: HTMLElement) {
      return this.classList.contains("react-flow__node") ? nodeBounds : originalGetBoundingClientRect.call(this);
    });
    const widthSpy = vi.spyOn(HTMLElement.prototype, "offsetWidth", "get").mockImplementation(function (this: HTMLElement) {
      return this.classList.contains("react-flow__node") ? 220 : 0;
    });
    const heightSpy = vi.spyOn(HTMLElement.prototype, "offsetHeight", "get").mockImplementation(function (this: HTMLElement) {
      return this.classList.contains("react-flow__node") ? 110 : 0;
    });
    try {
      renderWorkbench();

      const task = () => container.querySelector<HTMLElement>(".react-flow__node[data-id='bpmn-task']");
      await vi.waitFor(() => expect(task()).toBeTruthy());
      await vi.waitFor(() => {
        expect(task()).toBeTruthy();
        expect(getComputedStyle(task()!).visibility).toBe("visible");
        expect(task()?.querySelector(".wf-bpmn-incident-action")).toBeTruthy();
      });

      click(buttonByText(container, "Show affected activity"));
      await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
      expect(getComputedStyle(task()!).visibility).toBe("visible");
      expect(task()?.querySelector(".wf-bpmn-incident-action")).toBeTruthy();

      click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
      click(buttonByText(container, "Show affected activity"));
      await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
      expect(getComputedStyle(task()!).visibility).toBe("visible");
      expect(task()?.querySelector(".wf-bpmn-incident-action")).toBeTruthy();
    } finally {
      boundsSpy.mockRestore();
      widthSpy.mockRestore();
      heightSpy.mockRestore();
    }
  });

  it("restores the nested canvas from a maximized Issues view and keeps input evidence navigation in Activity", async () => {
    renderWorkbench();

    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.inspector-maximized")).toBeTruthy());
    expect(container.querySelector("[data-tab-id='issues']")?.getAttribute("aria-selected")).toBe("true");
    click(buttonByText(container, "Show affected activity"));

    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.inspector-maximized")).toBeNull());
    await vi.waitFor(() => expect(container.querySelector("[data-flow-node-id='target-node']")).toBeTruthy());
    expect(container.querySelector("[data-flow-node-id='target-node']")?.getAttribute("data-selected")).toBe("true");
    expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true");

    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(container.querySelector<HTMLDetailsElement>(".wf-input-inspection-row details")?.open).toBe(true));
  });

  it("reframes and refocuses the same affected activity on each explicit activation", async () => {
    renderWorkbench();

    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.inspector-maximized")).toBeTruthy());
    click(buttonByText(container, "Show affected activity"));
    await vi.waitFor(() => expect(mockFlowInstance.fitView).toHaveBeenCalledTimes(1));
    await vi.waitFor(() => expect(container.querySelector("[data-flow-node-id='target-node']")).toBeTruthy());
    const targetNode = container.querySelector<HTMLButtonElement>("[data-flow-node-id='target-node']")!;
    await vi.waitFor(() => expect(document.activeElement).toBe(targetNode));
    const focus = vi.spyOn(targetNode, "focus");

    targetNode.blur();
    expect(document.activeElement).not.toBe(targetNode);
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
    const showAffectedActivityAgain = buttonByText(container, "Show affected activity");
    expect(showAffectedActivityAgain).toBeTruthy();
    click(showAffectedActivityAgain);
    await vi.waitFor(() => expect(mockFlowInstance.fitView).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(focus).toHaveBeenCalledTimes(1));
    expect(document.activeElement).toBe(targetNode);
    expect(mockFlowInstance.fitView).toHaveBeenLastCalledWith({
      nodes: [{ id: "target-node" }],
      duration: 220,
      padding: 0.35
    });
  });

  it("opens a direct Issues tab query after the workbench measures at phone width", async () => {
    observedWidth = 390;
    renderWorkbench();

    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.narrow-inspector-view")).toBeTruthy());
    click(container.querySelector<HTMLButtonElement>("[aria-label='Back to workflow run canvas']"));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.narrow-canvas-view")).toBeTruthy());
  });

  it("does not restore Activity or reopen the drawer after a user changes tab and closes during lookup", async () => {
    observedWidth = 600;
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [] }));
    api.supportsActivityExecutionInspection.mockReset().mockResolvedValueOnce(false).mockResolvedValue(true);
    const lookup = deferred<ActivityExecutionInspection>();
    api.getActivityExecutionInspection.mockReturnValue(lookup.promise);

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.inspector-drawer-open")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(1));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='details']"));
    click(container.querySelector<HTMLButtonElement>("[aria-label='Close run details panel']"));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench.inspector-drawer-open")).toBeNull());

    lookup.resolve(activityInspection());
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='details']")?.getAttribute("aria-selected")).toBe("true"));
    expect(container.querySelector(".wf-instance-detail-workbench.inspector-drawer-open")).toBeNull();
  });

  it("clears stale incident state on refresh and ignores an exact lookup that completes afterward", async () => {
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [] }));
    api.supportsActivityExecutionInspection.mockReset()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false);
    const lateLookup = deferred<ActivityExecutionInspection>();
    let lookupCount = 0;
    api.getActivityExecutionInspection.mockImplementation(() => {
      lookupCount++;
      if (lookupCount === 1) return Promise.reject(Object.assign(new Error("Forbidden"), { status: 403 }));
      return lateLookup.promise;
    });

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.textContent).toContain("You do not have permission to inspect this activity execution"));
    expect(container.querySelector(".wf-instance-incident[data-selected='true']")).toBeTruthy();

    click(buttonByText(container, "Refresh"));
    await vi.waitFor(() => expect(api.getWorkflowInstance).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(container.textContent).not.toContain("You do not have permission to inspect this activity execution"));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident-summary")).toBeTruthy());
    expect(container.querySelector(".wf-instance-incident[data-selected='true']")).toBeNull();

    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(api.supportsActivityExecutionInspection).toHaveBeenCalledTimes(4));
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(2));
    click(buttonByText(container, "Refresh"));
    await vi.waitFor(() => expect(api.getWorkflowInstance).toHaveBeenCalledTimes(3));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident-summary")).toBeTruthy());
    expect(container.querySelector(".wf-instance-incident[data-selected='true']")).toBeNull();

    lateLookup.resolve(activityInspection());
    await lateLookup.promise;
    await new Promise(resolve => setTimeout(resolve, 0));
    expect(container.querySelector(".wf-instance-incident[data-selected='true']")).toBeNull();
    expect(container.querySelector("[data-tab-id='issues']")?.getAttribute("aria-selected")).toBe("true");
  });

  it("keeps denied and mismatched activity evidence unavailable while retrying only transient failures", async () => {
    const incidents = [
      incident({ incidentId: "denied-incident", activityExecutionId: "denied-execution" }),
      incident({ incidentId: "mismatch-incident", activityExecutionId: "mismatch-execution" }),
      incident({ incidentId: "transient-incident", activityExecutionId: "transient-execution" })
    ];
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents }));
    api.supportsActivityExecutionInspection.mockResolvedValue(true);
    let transientAttempt = 0;
    api.getActivityExecutionInspection.mockImplementation(async (_context, workflowExecutionId: string, activityExecutionId: string) => {
      if (activityExecutionId === "denied-execution") throw Object.assign(new Error("Forbidden"), { status: 403 });
      if (activityExecutionId === "mismatch-execution") return activityInspection({ activityExecutionId: "different-execution", workflowExecutionId });
      transientAttempt++;
      if (transientAttempt === 1) throw new Error("Runtime unavailable");
      return activityInspection({ activityExecutionId, workflowExecutionId });
    });

    renderWorkbench();
    await vi.waitFor(() => expect(container.textContent).toContain("You do not have permission to inspect 1 exact activity execution"));
    expect(container.textContent).toContain("Runtime returned a different run or activity execution for 1 association");
    expect(container.textContent).toContain("Some affected activities are not shown yet (1 remaining)");
    click(buttonByText(container, "Load more affected activities (1)"));

    await vi.waitFor(() => expect(transientAttempt).toBe(2));
    await vi.waitFor(() => expect(container.textContent).not.toContain("Loading affected activities..."));
    expect(api.getActivityExecutionInspection.mock.calls.map(([, , activityExecutionId]) => activityExecutionId)).toEqual([
      "denied-execution",
      "mismatch-execution",
      "transient-execution",
      "transient-execution"
    ]);
    expect(buttonByText(container, "Load more affected activities (1)")).toBeUndefined();
    expect(container.textContent).toContain("You do not have permission to inspect 1 exact activity execution");
    expect(container.textContent).toContain("Runtime returned a different run or activity execution for 1 association");
    expect(container.textContent).not.toContain("Some affected activities are not shown yet");
  });

  it("uses the pinned graph to label node-only incidents when exact execution inspection is absent", async () => {
    const nodeOnlyIncident = incident({
      activityExecutionId: null,
      executableNodeId: "compiled-target",
      metadata: { "runtime.inputKey": "text" }
    });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [nodeOnlyIncident] }));
    const pinnedExecutable = executable();
    const nestedActivity = pinnedExecutable.rootActivity.childSlots[0]!.activities[0]!.childSlots[0]!.activities[0]!;
    nestedActivity.activityTypeVersion = "0.9.0";
    pinnedExecutable.chosenReference = {
      ...pinnedExecutable.chosenReference!,
      activityPresentation: [{ executableNodeId: "compiled-target", displayName: "Frozen Write Line" }]
    };
    api.getExecutable.mockResolvedValue(pinnedExecutable);

    renderWorkbench();

    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident-summary strong")?.textContent)
      .toBe("Frozen Write Line · Text input"));
    expect(api.getActivityExecutionInspection).not.toHaveBeenCalled();
  });

  it("keeps the visible incident summary in the selection button's accessible name", async () => {
    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident-summary")).toBeTruthy());

    const summary = container.querySelector<HTMLButtonElement>(".wf-instance-incident-summary")!;
    expect(summary.hasAttribute("aria-label")).toBe(false);
    expect(summary.textContent).toContain("Write Line · Text input");
    expect(summary.textContent).toContain("The Text input failed.");
    expect(summary.textContent).toContain("Needs intervention · Open");
  });

  it("clears input focus when a different incident is selected", async () => {
    const firstActivity = {
      ...activityExecution(),
      activityExecutionId: "execution-first",
      executableNodeId: "compiled-first",
      authoredActivityId: "target-first",
      incidentIds: ["incident-first"]
    };
    const secondActivity = {
      ...activityExecution(),
      activityExecutionId: "execution-second",
      executableNodeId: "compiled-second",
      authoredActivityId: "target-second",
      incidentIds: ["incident-second"]
    };
    const firstIncident = incident({ incidentId: "incident-first", activityExecutionId: "execution-first", executableNodeId: "compiled-first", failureType: "FirstFailure", message: "First failure." });
    const secondIncident = incident({ incidentId: "incident-second", activityExecutionId: "execution-second", executableNodeId: "compiled-second", failureType: "SecondFailure", message: "Second failure." });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [firstActivity, secondActivity], incidents: [firstIncident, secondIncident] }));
    api.getActivityExecutionInspection.mockImplementation((_context, workflowExecutionId: string, activityExecutionId: string) =>
      Promise.resolve(activityInspection({ activityExecutionId, workflowExecutionId })));

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector<HTMLDetailsElement>(".wf-input-inspection-row details")?.open).toBe(true));

    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
    click([...container.querySelectorAll<HTMLButtonElement>(".wf-instance-incident-summary")]
      .find(button => button.textContent?.includes("Second failure.")));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='activity']"));

    await vi.waitFor(() => expect(container.textContent).toContain("execution-second"));
    await vi.waitFor(() => expect(container.querySelector(".wf-input-inspection-row details")).toBeTruthy());
    expect(container.querySelector<HTMLDetailsElement>(".wf-input-inspection-row details")?.open).toBe(false);
  });

  it("clears a denied incident lookup message when a healthy activity is selected", async () => {
    const deniedIncident = incident({
      incidentId: "incident-denied",
      activityExecutionId: "execution-denied",
      executableNodeId: "compiled-denied",
      failureType: "DeniedFailure"
    });
    const healthyActivity = {
      ...activityExecution(),
      activityExecutionId: "execution-healthy",
      executableNodeId: "compiled-healthy",
      authoredActivityId: "healthy-node",
      incidentCount: 0,
      incidentIds: []
    };
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [healthyActivity], incidents: [deniedIncident] }));
    api.supportsActivityExecutionInspection.mockReset().mockResolvedValueOnce(false).mockResolvedValue(true);
    api.getActivityExecutionInspection.mockImplementation((_context, workflowExecutionId: string, activityExecutionId: string) =>
      activityExecutionId === "execution-denied"
        ? Promise.reject(Object.assign(new Error("Forbidden"), { status: 403 }))
        : Promise.resolve(activityInspection({ activityExecutionId, workflowExecutionId })));

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.textContent).toContain("You do not have permission to inspect this activity execution"));

    click(container.querySelector<HTMLButtonElement>("[data-tab-id='timeline']"));
    click(container.querySelector<HTMLButtonElement>(".wf-timeline-entry"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(container.textContent).toContain("execution-healthy"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));

    expect(container.textContent).not.toContain("You do not have permission to inspect this activity execution");
  });

  it("does not cache an on-demand association after Refresh replaces the run snapshot", async () => {
    const oldIncident = incident({ incidentId: "incident-old", failureType: "OldFailure" });
    const refreshedIncident = incident({
      incidentId: "incident-current",
      failureType: "RefreshedFailure",
      status: "Resolved",
      isBlocking: false
    });
    const refreshedActivity = {
      ...activityExecution(),
      status: "Completed",
      completedAt: "2026-10-01T12:01:00Z",
      incidentCount: 0,
      incidentIds: []
    };
    api.getWorkflowInstance
      .mockResolvedValueOnce(workflowDetails({ activities: [], incidents: [oldIncident] }))
      .mockResolvedValueOnce(workflowDetails({ activities: [refreshedActivity], incidents: [refreshedIncident] }));
    api.supportsActivityExecutionInspection.mockReset()
      .mockResolvedValueOnce(false)
      .mockResolvedValueOnce(true)
      .mockResolvedValueOnce(false);
    const lateLookup = deferred<ActivityExecutionInspection>();
    api.getActivityExecutionInspection.mockReturnValue(lateLookup.promise);

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(1));

    click(buttonByText(container, "Refresh"));
    await vi.waitFor(() => expect(api.getWorkflowInstance).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(container.textContent).toContain("RefreshedFailure"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='timeline']"));
    click(container.querySelector<HTMLButtonElement>(".wf-timeline-entry"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed"));

    lateLookup.resolve(activityInspection({
      activityExecutionId: "execution-target",
      status: "Faulted",
      incidents: [oldIncident]
    }));
    await lateLookup.promise;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed");
    const incidentCount = [...container.querySelectorAll(".wf-activity-summary-grid dt")]
      .find(label => label.textContent === "Incidents")?.nextElementSibling?.textContent;
    expect(incidentCount).toBe("0");
  });

  it("does not cache a Load more response after Refresh replaces activity statuses", async () => {
    const lastIndex = 50;
    const executionId = (index: number) => `execution-${index}`;
    const activityAt = (index: number, status: "Faulted" | "Completed") => ({
      ...activityExecution(),
      activityExecutionId: executionId(index),
      executableNodeId: `compiled-${index}`,
      authoredActivityId: `target-${index}`,
      status,
      scheduledAt: new Date(Date.UTC(2026, 9, 1, 12, 0, index)).toISOString(),
      completedAt: status === "Completed" ? "2026-10-01T12:01:00Z" : null,
      incidentCount: status === "Completed" ? 0 : 1,
      incidentIds: status === "Completed" ? [] : [`incident-${index}`]
    });
    const incidentAt = (index: number, status: "Open" | "Resolved") => incident({
      incidentId: `incident-${index}`,
      activityExecutionId: executionId(index),
      executableNodeId: `compiled-${index}`,
      failureType: `${status}Failure-${index}`,
      status,
      isBlocking: status === "Open"
    });
    const initialIncidents = Array.from({ length: lastIndex + 1 }, (_, index) => incidentAt(index, "Open"));
    const refreshedIncidents = Array.from({ length: lastIndex + 1 }, (_, index) => incidentAt(index, "Resolved"));
    const refreshedActivities = Array.from({ length: lastIndex + 1 }, (_, index) => activityAt(index, "Completed"));
    const lateLookup = deferred<ActivityExecutionInspection>();
    api.getWorkflowInstance
      .mockResolvedValueOnce(workflowDetails({ activities: [], incidents: initialIncidents }))
      .mockResolvedValueOnce(workflowDetails({ activities: refreshedActivities, incidents: refreshedIncidents }));
    api.supportsActivityExecutionInspection.mockResolvedValue(true);
    api.getActivityExecutionInspection.mockImplementation((_context, workflowExecutionId: string, activityExecutionId: string) => {
      const index = Number(activityExecutionId.replace("execution-", ""));
      if (index === lastIndex) return lateLookup.promise;
      return Promise.resolve(activityInspection({
        activityExecutionId,
        workflowExecutionId,
        executableNodeId: `compiled-${index}`,
        authoredActivityId: `target-${index}`,
        incidents: [initialIncidents[index]!]
      }));
    });

    renderWorkbench();
    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (1)")).toBeTruthy());
    click(buttonByText(container, "Load more affected activities (1)"));
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(lastIndex + 1));

    click(buttonByText(container, "Refresh"));
    await vi.waitFor(() => expect(api.getWorkflowInstance).toHaveBeenCalledTimes(2));
    await vi.waitFor(() => expect(container.textContent).toContain("ResolvedFailure-50"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='timeline']"));
    const timelineEntries = container.querySelectorAll<HTMLButtonElement>(".wf-timeline-entry");
    expect(timelineEntries).toHaveLength(lastIndex + 1);
    click(timelineEntries[lastIndex]);
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    await vi.waitFor(() => expect(container.textContent).toContain(executionId(lastIndex)));

    lateLookup.resolve(activityInspection({
      activityExecutionId: executionId(lastIndex),
      executableNodeId: `compiled-${lastIndex}`,
      authoredActivityId: `target-${lastIndex}`,
      status: "Faulted",
      incidents: [initialIncidents[lastIndex]!]
    }));
    await lateLookup.promise;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed");
    const incidentCount = [...container.querySelectorAll(".wf-activity-summary-grid dt")]
      .find(label => label.textContent === "Incidents")?.nextElementSibling?.textContent;
    expect(incidentCount).toBe("0");
  });

  it("removes a transiently failed association from Load more after exact on-demand inspection succeeds", async () => {
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [incident()] }));
    api.supportsActivityExecutionInspection.mockResolvedValue(true);
    let lookupAttempt = 0;
    api.getActivityExecutionInspection.mockImplementation(async (_context, workflowExecutionId: string, activityExecutionId: string) => {
      lookupAttempt++;
      if (lookupAttempt === 1) throw new Error("Runtime temporarily unavailable");
      return activityInspection({ workflowExecutionId, activityExecutionId });
    });

    renderWorkbench();
    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (1)")).toBeTruthy());
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));

    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (1)")).toBeUndefined());
    expect(container.textContent).not.toContain("Some affected activities are not shown yet");
  });

  it("clears a denied association after on-demand success while preserving another pending activity", async () => {
    const deniedIncident = incident({ incidentId: "incident-denied", activityExecutionId: "execution-denied", failureType: "DeniedFailure" });
    const pendingIncident = incident({ incidentId: "incident-pending", activityExecutionId: "execution-pending", executableNodeId: "compiled-pending", failureType: "PendingFailure" });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [deniedIncident, pendingIncident] }));
    api.supportsActivityExecutionInspection.mockResolvedValue(true);
    let deniedAttempts = 0;
    api.getActivityExecutionInspection.mockImplementation(async (_context, workflowExecutionId: string, activityExecutionId: string) => {
      if (activityExecutionId === "execution-denied") {
        deniedAttempts++;
        if (deniedAttempts === 1) throw Object.assign(new Error("Forbidden"), { status: 403 });
        return activityInspection({ workflowExecutionId, activityExecutionId });
      }
      throw new Error("Runtime temporarily unavailable");
    });

    renderWorkbench();
    await vi.waitFor(() => expect(container.textContent).toContain("You do not have permission to inspect 1 exact activity execution"));
    expect(container.textContent).toContain("Some affected activities are not shown yet (1 remaining)");
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));

    await vi.waitFor(() => expect(container.textContent).not.toContain("You do not have permission to inspect 1 exact activity execution"));
    expect(container.textContent).toContain("Some affected activities are not shown yet (1 remaining)");
    expect(buttonByText(container, "Load more affected activities (1)")).toBeTruthy();
  });

  it("marks exact inspection supported after an on-demand success and keeps other unsupported associations pending", async () => {
    const firstIncident = incident({ incidentId: "incident-first", activityExecutionId: "execution-first", failureType: "FirstFailure" });
    const secondIncident = incident({ incidentId: "incident-second", activityExecutionId: "execution-second", executableNodeId: "compiled-second", failureType: "SecondFailure" });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [firstIncident, secondIncident] }));
    api.supportsActivityExecutionInspection.mockReset().mockResolvedValueOnce(false).mockResolvedValue(true);
    api.getActivityExecutionInspection.mockImplementation((_context, workflowExecutionId: string, activityExecutionId: string) =>
      Promise.resolve(activityInspection({ workflowExecutionId, activityExecutionId })));

    renderWorkbench();
    await vi.waitFor(() => expect(container.textContent).toContain("Exact affected activity evidence is unavailable from this server."));
    click(buttonByText(container, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));

    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (1)")).toBeTruthy());
    expect(container.textContent).not.toContain("Exact affected activity evidence is unavailable from this server.");
    expect(container.textContent).toContain("Some affected activities are not shown yet (1 remaining)");
  });

  it("keeps exact successes authoritative when an older Load more batch settles", async () => {
    const lastIndex = 52;
    const executionId = (index: number) => `execution-${index}`;
    const incidentAt = (index: number) => incident({
      incidentId: `incident-${index}`,
      activityExecutionId: executionId(index),
      executableNodeId: `compiled-${index}`,
      failureType: `Failure-${index}`
    });
    const incidents = Array.from({ length: lastIndex + 1 }, (_, index) => incidentAt(index));
    const pendingBatch = [deferred<ActivityExecutionInspection>(), deferred<ActivityExecutionInspection>(), deferred<ActivityExecutionInspection>()];
    const callsByExecutionId = new Map<string, number>();
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents }));
    api.supportsActivityExecutionInspection.mockResolvedValue(true);
    api.getActivityExecutionInspection.mockImplementation((_context, workflowExecutionId: string, activityExecutionId: string) => {
      const index = Number(activityExecutionId.replace("execution-", ""));
      if (index < 50) {
        return Promise.resolve(activityInspection({ activityExecutionId, workflowExecutionId, executableNodeId: `compiled-${index}`, incidents: [incidents[index]!] }));
      }
      const callCount = (callsByExecutionId.get(activityExecutionId) ?? 0) + 1;
      callsByExecutionId.set(activityExecutionId, callCount);
      if (callCount === 1) return pendingBatch[index - 50]!.promise;
      return Promise.resolve(activityInspection({
        activityExecutionId,
        workflowExecutionId,
        executableNodeId: `compiled-${index}`,
        status: "Completed",
        incidents: [incidents[index]!]
      }));
    });

    renderWorkbench();
    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (3)")).toBeTruthy());
    click(buttonByText(container, "Load more affected activities (3)"));
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(53));

    for (let index = 50; index <= lastIndex; index++) {
      const row = [...container.querySelectorAll<HTMLElement>(".wf-instance-incident")]
        .find(candidate => candidate.textContent?.includes(`Failure-${index}`));
      click(buttonByText(row!, "View input evidence"));
      await vi.waitFor(() => expect(container.querySelector("[data-tab-id='activity']")?.getAttribute("aria-selected")).toBe("true"));
      await vi.waitFor(() => expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed"));
      if (index < lastIndex) {
        click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
        await vi.waitFor(() => expect(container.querySelector("[data-tab-id='issues']")?.getAttribute("aria-selected")).toBe("true"));
      }
    }

    click(container.querySelector<HTMLButtonElement>("[data-tab-id='issues']"));
    await vi.waitFor(() => expect(buttonByText(container, "Load more affected activities (3)")).toBeUndefined());
    expect(container.textContent).not.toContain("You do not have permission to inspect 1 exact activity execution");

    pendingBatch[0]!.reject(new Error("Runtime temporarily unavailable"));
    pendingBatch[1]!.reject(Object.assign(new Error("Forbidden"), { status: 403 }));
    pendingBatch[2]!.resolve(activityInspection({
      activityExecutionId: executionId(lastIndex),
      executableNodeId: `compiled-${lastIndex}`,
      status: "Faulted",
      incidents: [incidents[lastIndex]!]
    }));
    await pendingBatch[2]!.promise;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.textContent).not.toContain("Some affected activities are not shown yet");
    expect(container.textContent).not.toContain("You do not have permission to inspect 1 exact activity execution");
    click(buttonByText([...container.querySelectorAll<HTMLElement>(".wf-instance-incident")]
      .find(candidate => candidate.textContent?.includes(`Failure-${lastIndex}`))!, "View input evidence"));
    await vi.waitFor(() => expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed"));
    const incidentCount = [...container.querySelectorAll(".wf-activity-summary-grid dt")]
      .find(label => label.textContent === "Incidents")?.nextElementSibling?.textContent;
    expect(incidentCount).toBe("1");
  });

  it("keeps the newest incident selected when exact activity lookups finish out of order", async () => {
    const firstIncident = incident({ incidentId: "incident-first", activityExecutionId: "execution-first", executableNodeId: "compiled-first", failureType: "FirstFailure", metadata: {} });
    const secondIncident = incident({ incidentId: "incident-second", activityExecutionId: "execution-second", executableNodeId: "compiled-second", failureType: "SecondFailure", metadata: {} });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [firstIncident, secondIncident] }));
    api.supportsActivityExecutionInspection.mockReset().mockResolvedValueOnce(false).mockResolvedValue(true);
    const firstLookup = deferred<ActivityExecutionInspection>();
    const secondLookup = deferred<ActivityExecutionInspection>();
    api.getActivityExecutionInspection.mockImplementation((_context, _workflowExecutionId, activityExecutionId: string) =>
      activityExecutionId === "execution-first" ? firstLookup.promise : secondLookup.promise);

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-detail-workbench")).toBeTruthy());
    const incidentRows = [...container.querySelectorAll<HTMLElement>(".wf-instance-incident")];
    expect(incidentRows).toHaveLength(2);
    click(buttonByText(incidentRows[0]!, "Show affected activity"));
    click(buttonByText(incidentRows[1]!, "Show affected activity"));
    expect(container.querySelector(".wf-instance-incident[data-selected='true']")?.textContent).toContain("SecondFailure");
    expect(container.querySelector("[data-tab-id='issues']")?.getAttribute("aria-selected")).toBe("true");
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(2));

    secondLookup.resolve(activityInspection({
      activityExecutionId: "execution-second",
      executableNodeId: "compiled-second",
      authoredActivityId: "target-second",
      incidents: [secondIncident]
    }));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident[data-selected='true']")?.textContent).toContain("SecondFailure"));
    firstLookup.resolve(activityInspection({
      activityExecutionId: "execution-first",
      executableNodeId: "compiled-first",
      authoredActivityId: "target-first",
      incidents: [firstIncident]
    }));
    await vi.waitFor(() => expect(container.querySelector(".wf-instance-incident[data-selected='true']")?.textContent).toContain("SecondFailure"));
  });

  it("keeps the newest exact summary when two incidents share one activity execution", async () => {
    const olderIncident = incident({
      incidentId: "incident-older",
      activityExecutionId: "execution-shared",
      executableNodeId: "compiled-target",
      failureType: "OlderFailure",
      message: "The earlier attempt failed."
    });
    const newerIncident = incident({
      incidentId: "incident-newer",
      activityExecutionId: "execution-shared",
      executableNodeId: "compiled-target",
      failureType: "NewerFailure",
      message: "The current attempt failed."
    });
    api.getWorkflowInstance.mockResolvedValue(workflowDetails({ activities: [], incidents: [olderIncident, newerIncident] }));
    api.supportsActivityExecutionInspection.mockReset().mockResolvedValueOnce(false).mockResolvedValue(true);
    const olderLookup = deferred<ActivityExecutionInspection>();
    const newerLookup = deferred<ActivityExecutionInspection>();
    let lookupCount = 0;
    api.getActivityExecutionInspection.mockImplementation(() =>
      ++lookupCount === 1 ? olderLookup.promise : newerLookup.promise);

    renderWorkbench();
    await vi.waitFor(() => expect(container.querySelectorAll(".wf-instance-incident")).toHaveLength(2));
    const incidentRow = (message: string) => [...container.querySelectorAll<HTMLElement>(".wf-instance-incident")]
      .find(row => row.textContent?.includes(message));
    const olderAction = buttonByText(incidentRow("The earlier attempt failed.")!, "View input evidence");
    expect(olderAction).toBeTruthy();
    click(olderAction);
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(1));
    const newerAction = buttonByText(incidentRow("The current attempt failed.")!, "View input evidence");
    expect(newerAction).toBeTruthy();
    click(newerAction);
    await vi.waitFor(() => expect(api.getActivityExecutionInspection).toHaveBeenCalledTimes(2));

    newerLookup.resolve(activityInspection({
      activityExecutionId: "execution-shared",
      status: "Completed",
      incidents: [newerIncident],
      bookmarks: [bookmarkSummary("current-1"), bookmarkSummary("current-2")]
    }));
    await vi.waitFor(() => expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed"));
    const metadataValue = (label: string) => [...container.querySelectorAll<HTMLElement>(".wf-activity-meta-item")]
      .find(item => item.querySelector("dt")?.textContent === label)
      ?.querySelector(".wf-activity-meta-value")?.textContent;
    expect(metadataValue("Incidents")).toBe("1");
    expect(metadataValue("Bookmarks")).toBe("2");

    olderLookup.resolve(activityInspection({
      activityExecutionId: "execution-shared",
      status: "Faulted",
      incidents: [olderIncident, incident({ incidentId: "incident-older-extra", activityExecutionId: "execution-shared" })],
      bookmarks: [bookmarkSummary("stale-bookmark")]
    }));
    await olderLookup.promise;
    await new Promise(resolve => setTimeout(resolve, 0));

    expect(container.querySelector(".wf-activity-overview-status")?.textContent).toContain("Completed");
    expect(metadataValue("Incidents")).toBe("1");
    expect(metadataValue("Bookmarks")).toBe("2");
  });
});

function buttonByText(scope: ParentNode, text: string) {
  return [...scope.querySelectorAll<HTMLButtonElement>("button")].find(button => button.textContent?.trim() === text);
}

function renderWorkbench() {
  flushSync(() => root.render(<WorkflowInstanceDetailsWorkbench
    context={{} as StudioEndpointContext}
    ai={{ promptActions: { list: () => [] } } as unknown as StudioAiContributionApi}
    workflowExecutionId="run-1"
    initialInspectorTab="issues"
    navigate={() => {}}
  />));
}

function click(button: HTMLButtonElement | undefined | null) {
  flushSync(() => button?.click());
}

function deferred<T>() {
  let resolve!: (value: T) => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<T>((done, fail) => {
    resolve = done;
    reject = fail;
  });
  return { promise, resolve, reject };
}

function workflowDetails({ activities = [activityExecution()], incidents = [incident()] }: {
  activities?: ActivityExecutionStateSummary[];
  incidents?: IncidentStateSummary[];
} = {}): WorkflowInstanceDetails {
  const instance: WorkflowInstanceSummary = {
    workflowExecutionId: "run-1",
    artifactId: "artifact-1",
    artifactVersion: "1.0.0",
    artifactHash: "sha256:1",
    definitionId: "definition-1",
    definitionVersionId: "version-1",
    sourceReferenceId: "reference-1",
    status: "Faulted",
    createdAt: "2026-10-01T12:00:00Z",
    startedAt: "2026-10-01T12:00:00Z",
    completedAt: null,
    activityCount: activities.length,
    incidentCount: incidents.length
  };
  return { instance, activities, incidents };
}

function activityExecution(): ActivityExecutionStateSummary {
  return {
    activityExecutionId: "execution-target",
    workflowExecutionId: "run-1",
    executableNodeId: "compiled-target",
    authoredActivityId: "target-node",
    activityType: writeLine.activityTypeKey,
    activityTypeVersion: writeLine.version,
    status: "Faulted",
    scheduledAt: "2026-10-01T12:00:00Z",
    bookmarkIds: [],
    incidentCount: 1,
    incidentIds: ["incident-target"],
    faultCount: 1,
    aggregateFaultCount: 0,
    metadata: {}
  };
}

function incident(overrides: Partial<IncidentStateSummary> = {}): IncidentStateSummary {
  return {
    incidentId: "incident-target",
    workflowExecutionId: "run-1",
    activityExecutionId: "execution-target",
    executableNodeId: "compiled-target",
    severity: "Error",
    status: "Open",
    resolutionAction: "None",
    failureType: "ActivityInputFailed",
    message: "The Text input failed.",
    createdAt: "2026-10-01T12:00:00Z",
    isBlocking: true,
    metadata: { "runtime.inputKey": "text" },
    ...overrides
  };
}

function executable(): WorkflowExecutableDetails {
  const target: WorkflowExecutableNode = {
    executableNodeId: "compiled-target",
    authoredActivityId: "target-node",
    activityType: writeLine.activityTypeKey,
    activityTypeVersion: writeLine.version,
    inputBindings: [{ inputName: "Text", inputKey: "text", source: "Literal", literalValue: "hello" }],
    childSlots: []
  };
  const nestedSequence: WorkflowExecutableNode = {
    executableNodeId: "compiled-nested-sequence",
    authoredActivityId: "nested-sequence",
    activityType: sequenceActivity.activityTypeKey,
    activityTypeVersion: sequenceActivity.version,
    structureKind: "elsa.sequence.structure",
    inputBindings: [],
    childSlots: [{ name: "Sequence.Activities", activities: [target] }]
  };
  const rootActivity: WorkflowExecutableNode = {
    executableNodeId: "compiled-root-sequence",
    authoredActivityId: "root-sequence",
    activityType: sequenceActivity.activityTypeKey,
    activityTypeVersion: sequenceActivity.version,
    structureKind: "elsa.sequence.structure",
    inputBindings: [],
    childSlots: [{ name: "Sequence.Activities", activities: [nestedSequence] }]
  };
  return {
    artifactId: "artifact-1",
    artifactHash: "sha256:1",
    createdAt: "2026-10-01T12:00:00Z",
    rootActivityType: sequenceActivity.activityTypeKey,
    rootActivityVersion: sequenceActivity.version,
    nodeCount: 3,
    resumeTargetCount: 0,
    rootActivity,
    chosenReference: { sourceReferenceId: "reference-1", selection: "requested", layout: [] },
    references: []
  };
}

function bpmnExecutable(): WorkflowExecutableDetails {
  const target: WorkflowExecutableNode = {
    executableNodeId: "compiled-target",
    authoredActivityId: "target-node",
    activityType: writeLine.activityTypeKey,
    activityTypeVersion: writeLine.version,
    inputBindings: [],
    childSlots: []
  };
  const rootActivity: WorkflowExecutableNode = {
    ...target,
    executableNodeId: "compiled-bpmn",
    authoredActivityId: "bpmn-root",
    activityType: bpmnActivity.activityTypeKey,
    activityTypeVersion: bpmnActivity.version,
    structureKind: "elsa.bpmn.structure",
    childSlots: [{ name: "Bpmn.Activities", activities: [target] }],
    bpmnStructure: {
      elements: [
        { elementId: "bpmn-start", elementType: "startEvent" },
        { elementId: "bpmn-task", elementType: "task", childNodeId: target.executableNodeId },
        { elementId: "bpmn-end", elementType: "endEvent" }
      ],
      sequenceFlows: [
        { flowId: "start-task", sourceRef: "bpmn-start", targetRef: "bpmn-task" },
        { flowId: "task-end", sourceRef: "bpmn-task", targetRef: "bpmn-end" }
      ]
    }
  };
  return {
    ...executable(),
    rootActivityType: bpmnActivity.activityTypeKey,
    rootActivityVersion: bpmnActivity.version,
    nodeCount: 2,
    rootActivity
  };
}

function activityInspection(overrides: Partial<ActivityExecutionInspection> = {}): ActivityExecutionInspection {
  return {
    activityExecutionId: "execution-target",
    workflowExecutionId: "run-1",
    executableNodeId: "compiled-target",
    authoredActivityId: "target-node",
    activityType: writeLine.activityTypeKey,
    activityTypeVersion: writeLine.version,
    status: "Faulted",
    executionSequence: 1,
    scheduledAt: "2026-10-01T12:00:00Z",
    provenance: {} as ActivityExecutionInspection["provenance"],
    outcomeNames: [],
    bookmarks: [],
    incidents: [incident()],
    valueSnapshots: [],
    metadata: {},
    ...overrides
  };
}

function bookmarkSummary(bookmarkId: string): ActivityExecutionInspection["bookmarks"][number] {
  return {
    bookmarkId,
    resumeTargetId: "target",
    stimulusType: "timer",
    stimulusHash: "sha256:timer",
    createdAt: "2026-10-01T12:00:00Z",
    metadata: {}
  };
}
