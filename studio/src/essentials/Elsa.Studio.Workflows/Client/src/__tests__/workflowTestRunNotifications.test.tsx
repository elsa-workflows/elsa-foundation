import { flushSync } from "react-dom";
import { createRoot } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { StudioEndpointContext, StudioNotificationInput } from "@elsa-workflows/studio-sdk";
import type { WorkflowTestRunView } from "../workflowTypes";
import { useWorkflowOperations } from "../workflow-editor/useWorkflowOperations";
import { setNotifications } from "../workflow-editor/notifications";

const start = vi.hoisted(() => vi.fn());
vi.mock("../api/publishing", async original => ({ ...await original<object>(), startWorkflowDraftTestRun: start }));
const draft = { id: "draft", definitionId: "definition", state: { rootActivity: { nodeId: "write", activityVersionId: "write-v1", inputs: [], outputs: [] } }, layout: [], validationErrors: [] };
const view: WorkflowTestRunView = { testRunId: "test", definitionId: "definition", definitionVersionId: "draft", status: "DispatchAccepted", workflowExecutionId: "run/1" };
const notify = vi.fn((_input: StudioNotificationInput) => ({ id: "toast", update: vi.fn(), dismiss: vi.fn() }));
let root: ReturnType<typeof createRoot>;
let operations: ReturnType<typeof useWorkflowOperations>;
const setError = vi.fn();
const startTestRun = vi.fn();
const setStatus = vi.fn();
const setActiveRightPanelId = vi.fn();
const setInspectorCollapsed = vi.fn();

beforeEach(() => {
  vi.clearAllMocks();
  setNotifications({ notify, track: work => work });
  root = createRoot(document.createElement("div"));
  function Probe() {
    operations = useWorkflowOperations({ context: {} as StudioEndpointContext, draft, details: null, catalog: [], busy: false,
      saveDraft: vi.fn(), flushPendingSave: vi.fn(), reload: vi.fn(), startTestRun, setPublishedArtifact: vi.fn(),
      setOperation: vi.fn(), setStatus, setError, setActiveRightPanelId, setInspectorCollapsed, setAutosavePaused: vi.fn() });
    return null;
  }
  flushSync(() => root.render(<Probe />));
});
afterEach(() => { flushSync(() => root.unmount()); setNotifications(undefined); });

const cases: { patch: Partial<WorkflowTestRunView>; level: string; title: string }[] = [
  { patch: {}, level: "success", title: "Test run started" },
  { patch: { status: "Prepared", workflowExecutionId: null }, level: "success", title: "Test trigger prepared" },
  { patch: { status: "Rejected", reason: "Invalid input" }, level: "error", title: "Test run rejected" },
  { patch: { commandDispatchStatus: "AcceptedButFaulted" }, level: "error", title: "Test run needs attention" },
  { patch: { incidentCount: 2 }, level: "error", title: "Test run needs attention" },
  { patch: { status: "DispatchDeferred" }, level: "info", title: "Test run dispatch deferred" },
  { patch: { status: "DispatchDuplicate" }, level: "info", title: "Test run already dispatched" }
];

describe("Test Run notifications", () => {
  it.each(cases)("retains $title on the appropriate feedback surface", async ({ patch, level, title }) => {
    const result = { ...view, ...patch };
    start.mockResolvedValue(result);
    await operations.runTestDrive(draft, {}, { kind: "direct" }, "Direct start", []);
    expect(startTestRun).toHaveBeenCalledWith(expect.objectContaining({ view: result }));
    expect(setActiveRightPanelId).toHaveBeenCalledWith("runtime");
    expect(setInspectorCollapsed).toHaveBeenCalledWith(false);
    if (level === "error") {
      expect(notify).not.toHaveBeenCalled();
      expect(setStatus).toHaveBeenCalledWith(result.status === "Rejected" ? "Test run rejected" : "");
      return;
    }
    expect(notify).toHaveBeenCalledOnce();
    const notification = notify.mock.calls[0][0];
    expect(notification).toMatchObject({ level, title });
    if (result.workflowExecutionId) expect(notification.link).toEqual({ label: "Open run", path: "/workflows/instances/run%2F1" });
    else expect(notification.link).toBeUndefined();
  });

  it("retains dispatch failures inline with their error details", async () => {
    start.mockRejectedValue(new Error("Connection lost"));
    await operations.runTestDrive(draft, {}, { kind: "direct" }, "Direct start", []);
    expect(notify).not.toHaveBeenCalled();
    expect(setStatus).toHaveBeenCalledWith("");
    expect(setError).toHaveBeenCalledWith(expect.objectContaining({ message: "Connection lost" }));
    expect(startTestRun).not.toHaveBeenCalled();
  });
});
