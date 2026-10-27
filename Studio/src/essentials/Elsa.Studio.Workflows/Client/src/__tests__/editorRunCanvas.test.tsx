import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { authSessionEndedEvent } from "@elsa-workflows/studio-sdk";
import type { Node } from "@xyflow/react";
import { useEditorRunCanvas, type EditorRunCanvas } from "../workflow-editor/useEditorRunCanvas";
import { AttachedTestRunStatus } from "../workflow-editor/AttachedTestRunStatus";
import type { WorkflowTestRunState } from "../workflow-editor/editorTypes";
import type { WorkflowNodeData } from "../workflowAdapter";
import type { ActivityExecutionStateSummary, WorkflowExecutableDetails, WorkflowInstanceDetails } from "../workflowTypes";
import { ApiCapabilityUnavailableError, capabilityIds } from "../api/capabilities";
import { getDraftSignature } from "../workflow-editor/editorHelpers";
import { useWorkflowOperations } from "../workflow-editor/useWorkflowOperations";
import { writeLine } from "./fixtures";

const api = vi.hoisted(() => ({ instance: vi.fn(), executable: vi.fn(), browse: vi.fn(), start: vi.fn() }));
vi.mock("../api/runtime", async importOriginal => ({ ...await importOriginal<object>(), getWorkflowInstance: api.instance, getExecutable: api.executable, browseActivityExecutions: api.browse }));
vi.mock("../api/publishing", async importOriginal => ({ ...await importOriginal<object>(), startWorkflowDraftTestRun: api.start }));

const context = {} as StudioEndpointContext;
const catalog = [writeLine];
const nodes: Node<WorkflowNodeData>[] = [{ id: "write", position: { x: 240, y: 160 }, selected: true,
  data: { label: "Write Line", activityVersionId: writeLine.activityVersionId, childSlots: [], acceptsInbound: true, sourcePorts: [{ name: "Done", displayName: "Done" }] } }];
const snapshot = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "write", activityVersionId: writeLine.activityVersionId, inputs: [], outputs: [] } }, layout: [], validationErrors: [] };
const run: WorkflowTestRunState = { draftSignature: getDraftSignature(snapshot), snapshot, endpointContext: context,
  view: { testRunId: "test-1", definitionId: "definition", definitionVersionId: "draft", workflowExecutionId: "run-1", artifactId: "artifact", status: "Dispatched" } };
const activity: ActivityExecutionStateSummary = { activityExecutionId: "activity-1", workflowExecutionId: "run-1", executableNodeId: "exec-write", authoredActivityId: "write",
  activityType: writeLine.activityTypeKey, activityTypeVersion: "1.0.0", status: "Completed", scheduledAt: "2026-10-27T10:00:00Z", metadata: {} };
const details = { instance: { workflowExecutionId: "run-1", artifactId: "artifact", status: "Completed" }, activities: [], incidents: [], inspectionGranularity: "boundary-level" } as unknown as WorkflowInstanceDetails;
const executable = { artifactId: "artifact", rootActivity: { executableNodeId: "exec-write", authoredActivityId: "write", activityType: writeLine.activityTypeKey,
  activityTypeVersion: "1.0.0", childSlots: [], inputBindings: [] } } as unknown as WorkflowExecutableDetails;
let root: Root;
let container: HTMLDivElement;
let latest: EditorRunCanvas;
let props: Parameters<typeof useEditorRunCanvas>[0];

beforeEach(() => {
  vi.resetAllMocks();
  api.instance.mockResolvedValue(details);
  api.executable.mockResolvedValue(executable);
  api.browse.mockResolvedValue({ totalCount: 1, items: [{ execution: activity }] });
  container = document.createElement("div");
  document.body.append(container);
  root = createRoot(container);
  props = { context, definitionId: "definition", testRun: run, draftSignature: run.draftSignature, catalog, nodes, selectedNodeId: "write" };
});

afterEach(() => { flushSync(() => root.unmount()); container.remove(); });

function Probe() {
  latest = useEditorRunCanvas(props);
  return <><AttachedTestRunStatus view={props.testRun!.view} canvas={latest} onOpenDetails={() => {}} onOpenRun={() => {}} />
    <div data-node-status={latest.nodes[0]?.data.runtime?.status ?? "authoring"} />
  </>;
}

function render(update: Partial<typeof props> = {}) {
  props = { ...props, ...update };
  flushSync(() => root.render(<Probe />));
}

async function settle() {
  await new Promise(resolve => setTimeout(resolve, 0));
  flushSync(() => undefined);
}

function click(label: string) {
  const button = [...container.querySelectorAll("button")].find(item => item.textContent === label);
  expect(button).toBeDefined();
  flushSync(() => button!.dispatchEvent(new MouseEvent("click", { bubbles: true })));
}

describe("execution attached to the editor canvas", () => {
  it("projects a verified unique occurrence without changing the document, selection or geometry", async () => {
    render(); await settle();
    expect(latest.nodes[0].data.runtime?.status).toBe("Completed");
    expect(latest.nodes[0].ariaLabel).toContain("Run state: Completed");
    expect(latest.nodes[0].selected).toBe(true);
    expect(latest.nodes[0].position).toEqual({ x: 240, y: 160 });
    expect(nodes[0].data.runtime).toBeUndefined();
    expect(api.browse).toHaveBeenCalledWith(context, "run-1", { executableNodeId: "exec-write", limit: 1 }, expect.any(AbortSignal));
    expect(container.textContent).toContain("Activity state is recorded at commit boundaries.");
  });

  it("clears overlays in the first render after a draft change, before any async effect", async () => {
    render(); await settle();
    expect(latest.nodes[0].data.runtime).toBeDefined();
    render({ draftSignature: "a later design" });
    expect(latest.nodes).toBe(nodes);
    expect(container.textContent).toContain("The design changed. Run overlays are hidden.");
    expect(container.textContent).not.toContain("Execution on this canvas");
    expect(props.testRun!.snapshot).toBe(snapshot);
  });

  it("returns to authoring and can reattach without dispatching or changing the canvas", async () => {
    render(); await settle();
    click("Return to editing");
    expect(latest.nodes).toBe(nodes);
    expect(container.textContent).toContain("Editing the current draft");
    click("Show execution"); await settle();
    expect(latest.nodes[0].data.runtime?.status).toBe("Completed");
    expect(props.testRun!.view.workflowExecutionId).toBe("run-1");
  });

  it("withholds overlays for an identical graph belonging to another definition", async () => {
    render(); await settle();
    render({ definitionId: "another-definition" });
    expect(latest.nodes).toBe(nodes);
    expect(latest.stale).toBe(true);
  });

  it("withholds a global latest result for a node that has multiple occurrences", async () => {
    api.browse.mockResolvedValue({ totalCount: 10, items: [{ execution: activity }] });
    render(); await settle();
    expect(latest.nodes[0].data.runtime).toBeUndefined();
    expect(container.textContent).toContain("Open the run to choose an occurrence.");
  });

  it("does not rely on the bounded first activity page to determine node uniqueness", async () => {
    api.instance.mockResolvedValue({ ...details, activities: [activity], activityNextContinuationToken: "more" });
    api.browse.mockResolvedValue({ totalCount: 20, items: [{ execution: activity }] });
    render(); await settle();
    expect(latest.nodes[0].data.runtime).toBeUndefined();
    expect(api.browse).toHaveBeenCalledOnce();
  });

  it("rejects mismatched run/artifact evidence before reading or rendering activities", async () => {
    api.instance.mockResolvedValue({ ...details, instance: { ...details.instance, artifactId: "another-artifact" } });
    render(); await settle();
    expect(api.browse).not.toHaveBeenCalled();
    expect(latest.nodes).toBe(nodes);
    expect(container.textContent).toContain("does not match the Test Run snapshot");
  });

  it("ignores an obsolete request after another run is attached", async () => {
    let resolveOld!: (value: WorkflowInstanceDetails) => void;
    api.instance.mockImplementationOnce(() => new Promise(resolve => { resolveOld = resolve; }));
    render(); await settle();
    const nextRun = { ...run, view: { ...run.view, testRunId: "test-2", workflowExecutionId: "run-2" } };
    api.instance.mockResolvedValue({ ...details, instance: { ...details.instance, workflowExecutionId: "run-2" } });
    api.browse.mockResolvedValue({ totalCount: 1, items: [{ execution: { ...activity, workflowExecutionId: "run-2", status: "Suspended" } }] });
    render({ testRun: nextRun }); await settle();
    resolveOld(details); await settle();
    expect(latest.nodes[0].data.runtime?.status).toBe("Suspended");
    expect(api.executable).toHaveBeenCalledOnce();
  });

  it("clears evidence immediately when the endpoint context changes", async () => {
    render(); await settle();
    api.instance.mockImplementationOnce(() => new Promise(() => {}));
    render({ context: {} as StudioEndpointContext });
    expect(latest.nodes).toBe(nodes);
    expect(latest.eligible).toBe(false);
    await settle();
    expect(api.instance).toHaveBeenCalledOnce();
    expect(container.textContent).toContain("belongs to another backend session");
  });

  it("removes evidence on sign-out and ignores a late successful response", async () => {
    render(); await settle();
    flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
    expect(latest.nodes).toBe(nodes);
    expect(container.textContent).toContain("Sign in again");
  });

  it("offers a successful retry after unavailable runtime capabilities", async () => {
    api.instance.mockRejectedValueOnce(new ApiCapabilityUnavailableError(capabilityIds.runtime, "workflow-instance"));
    render(); await settle();
    expect(container.textContent).toContain("Execution evidence is unavailable on this backend.");
    click("Refresh execution"); await settle();
    expect(latest.nodes[0].data.runtime?.status).toBe("Completed");
    expect(container.textContent).not.toContain("unavailable on this backend");
  });

  it("does not load runtime evidence for dispatch rejection or a legacy run without a snapshot", async () => {
    render({ testRun: { ...run, snapshot: undefined, view: { ...run.view, workflowExecutionId: null, status: "Rejected" } } });
    await settle();
    expect(api.instance).not.toHaveBeenCalled();
    expect(latest.nodes).toBe(nodes);
    expect(container.textContent).toContain("Test Run rejected");
  });

  it("captures the dispatched snapshot before awaiting the server, without saving or publishing", async () => {
    const draft = structuredClone(snapshot);
    const signature = getDraftSignature(draft);
    const saveDraft = vi.fn();
    const startTestRun = vi.fn();
    let operations!: ReturnType<typeof useWorkflowOperations>;
    let complete!: (view: WorkflowTestRunState["view"]) => void;
    api.start.mockImplementation(() => new Promise(resolve => { complete = resolve; }));
    function OperationsProbe() {
      operations = useWorkflowOperations({ context, draft, details: null, catalog, busy: false, saveDraft,
        flushPendingSave: vi.fn(), reload: vi.fn(), startTestRun, setPublishedArtifact: vi.fn(), setOperation: vi.fn(),
        setStatus: vi.fn(), setError: vi.fn(), setActiveRightPanelId: vi.fn(), setInspectorCollapsed: vi.fn(), setAutosavePaused: vi.fn() });
      return null;
    }
    flushSync(() => root.render(<OperationsProbe />));
    const pending = operations.runTestDrive(draft, {}, { kind: "direct" }, "Direct start", []);
    draft.state.rootActivity!.nodeId = "later-node";
    expect(api.start.mock.calls[0][1].state.rootActivity.nodeId).toBe("write");
    complete(run.view);
    await pending; await settle();
    expect(startTestRun).toHaveBeenCalledWith(expect.objectContaining({ draftSignature: signature, snapshot: expect.objectContaining({ state: snapshot.state }) }));
    expect(startTestRun.mock.calls[0][0].snapshot).not.toBe(draft);
    expect(saveDraft).not.toHaveBeenCalled();
  });
});
