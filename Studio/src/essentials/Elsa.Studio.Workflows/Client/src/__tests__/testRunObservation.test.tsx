import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { ApiCapabilityUnavailableError, capabilityIds } from "../api/capabilities";
import type { WorkflowInstanceDetails, WorkflowTestRunView } from "../workflowTypes";
import { useTestRunObservation, type TestRunObservation } from "../workflow-editor/useTestRunObservation";

const api = vi.hoisted(() => ({ run: vi.fn(), waits: vi.fn(), instance: vi.fn() }));
vi.mock("../api/publishing", () => ({ getWorkflowTestRun: api.run, getWorkflowTestRunWaits: api.waits }));
vi.mock("../api/runtime", () => ({ getWorkflowInstance: api.instance }));
const context = { baseUrl: "https://backend.example" } as StudioEndpointContext;
const view: WorkflowTestRunView = { testRunId: "test", definitionId: "definition", definitionVersionId: "draft:1", artifactId: "artifact", workflowExecutionId: "run", status: "DispatchAccepted" };
const details = (status = "Running", run = "run") => ({ instance: { workflowExecutionId: run, artifactId: "artifact", status }, activities: [] }) as unknown as WorkflowInstanceDetails;
let root: Root;
let container: HTMLDivElement;
let props: Parameters<typeof useTestRunObservation>[0];
let current: TestRunObservation;
const notified = vi.fn();
function Probe() { current = useTestRunObservation(props); return null; }
function render(update: Partial<typeof props> = {}) {
  props = { ...props, ...update };
  flushSync(() => root.render(<Probe />));
}
async function settle() {
  for (let index = 0; index < 20; index++) await Promise.resolve();
  flushSync(() => root.render(<Probe />));
}
beforeEach(() => {
  vi.useFakeTimers(); vi.resetAllMocks();
  vi.spyOn(document, "hidden", "get").mockReturnValue(false);
  vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  api.run.mockResolvedValue(view); api.waits.mockResolvedValue({ waits: [] }); api.instance.mockResolvedValue(details());
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
  props = { context, testRun: { view, draftSignature: "snapshot", endpointContext: context }, onRunViewUpdated: notified };
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it("shares confirmed runtime state and waits, polling serially while the run is active", async () => {
  render(); await settle();
  expect(current.details?.instance.status).toBe("Running"); expect(current.status).toBe("connected");
  const firstRevision = current.revision;
  api.instance.mockResolvedValue(details("Suspended"));
  api.waits.mockResolvedValue({ waits: [{ waitId: "wait", nodeId: "node", label: "Continue", isAvailable: true, modes: [] }] });
  await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(current.details?.instance.status).toBe("Suspended"); expect(current.waits[0].waitId).toBe("wait");
  expect(current.revision).toBeGreaterThan(firstRevision); expect(api.run).toHaveBeenCalledTimes(2);
});

it("stops automatic observation at a confirmed terminal state and refreshes without dispatch", async () => {
  api.instance.mockResolvedValue(details("Completed")); render(); await settle();
  await vi.advanceTimersByTimeAsync(10000); expect(api.instance).toHaveBeenCalledTimes(1);
  flushSync(() => current.refresh()); await settle();
  expect(api.instance).toHaveBeenCalledTimes(2); expect(current.details?.instance.status).toBe("Completed");
  await vi.advanceTimersByTimeAsync(10000); expect(api.instance).toHaveBeenCalledTimes(2);
});

it("keeps observing an accepted stop until runtime confirms cancellation", async () => {
  api.run.mockResolvedValue({ ...view, status: "Stopped" });
  api.waits.mockResolvedValue({ waits: [{ waitId: "old-wait", nodeId: "node", label: "Continue", isAvailable: true, modes: [] }] });
  render(); await settle(); expect(current.details?.instance.status).toBe("Running");
  api.instance.mockResolvedValue(details("Cancelled"));
  await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(current.details?.instance.status).toBe("Cancelled"); expect(api.instance).toHaveBeenCalledTimes(2);
  expect(current.waits).toEqual([]);
  await vi.advanceTimersByTimeAsync(10000); expect(api.instance).toHaveBeenCalledTimes(2);
});

it("pauses observation without stopping execution, permits one refresh, then resumes following explicitly", async () => {
  render(); await settle();
  flushSync(() => current.setFollowing(false)); expect(current.status).toBe("paused");
  expect(current.details?.instance.status).toBe("Running");
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(1);
  api.instance.mockResolvedValue(details("Suspended"));
  flushSync(() => current.refresh()); await settle();
  expect(current.details?.instance.status).toBe("Suspended"); expect(current.following).toBe(false);
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(2);
  flushSync(() => current.setFollowing(true)); await settle();
  expect(current.status).toBe("connected"); expect(api.run).toHaveBeenCalledTimes(3);
  await vi.advanceTimersByTimeAsync(2000); expect(api.run).toHaveBeenCalledTimes(4);
});

it("does not poll a hidden view, and reconciles immediately when it becomes visible", async () => {
  render(); await settle();
  vi.spyOn(document, "hidden", "get").mockReturnValue(true);
  flushSync(() => document.dispatchEvent(new Event("visibilitychange")));
  expect(current.status).toBe("paused"); await vi.advanceTimersByTimeAsync(10000);
  expect(api.run).toHaveBeenCalledTimes(1);
  vi.spyOn(document, "hidden", "get").mockReturnValue(false);
  flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  expect(api.run).toHaveBeenCalledTimes(2); expect(current.status).toBe("connected");
});

it("clears live evidence while offline and reconnects the same execution", async () => {
  render(); await settle();
  flushSync(() => window.dispatchEvent(new Event("offline")));
  expect(current.status).toBe("disconnected"); expect(current.details).toBeNull();
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(1);
  flushSync(() => window.dispatchEvent(new Event("online"))); await settle();
  expect(current.status).toBe("connected"); expect(api.instance).toHaveBeenLastCalledWith(context, "run");
});

it("discards a late result when another run replaces the attachment", async () => {
  let release!: (value: WorkflowTestRunView) => void;
  api.run.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  render();
  const replacement = { ...view, testRunId: "test-2", workflowExecutionId: "run-2" };
  api.run.mockResolvedValue(replacement); api.instance.mockResolvedValue(details("Completed", "run-2"));
  render({ testRun: { ...props.testRun!, view: replacement } }); await settle();
  release(view); await settle();
  expect(current.details?.instance.workflowExecutionId).toBe("run-2");
  expect(notified.mock.calls.every(([value]) => value.testRunId === "test-2")).toBe(true);
  expect(api.instance).not.toHaveBeenCalledWith(context, "run");
});

it("rejects pending same-run evidence when authorization refreshes without logout", async () => {
  let release!: (value: WorkflowInstanceDetails) => void;
  api.instance.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  render(); await settle();
  api.instance.mockResolvedValue(details("Completed"));
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent))); await settle();
  release(details("Faulted")); await settle();
  expect(current.details?.instance.status).toBe("Completed");
});

it("revokes evidence on logout and stops observing until a new authorized session", async () => {
  render(); await settle();
  flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent)));
  expect(current.details).toBeNull(); expect(current.waits).toEqual([]);
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(1);
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent))); await settle();
  expect(api.run).toHaveBeenCalledTimes(2); expect(current.details?.instance.status).toBe("Running");
});

it("rejects an endpoint change even when its ids match the previous backend", async () => {
  render(); await settle(); render({ context: {} as StudioEndpointContext });
  expect(current.details).toBeNull(); await vi.advanceTimersByTimeAsync(10000);
  expect(api.run).toHaveBeenCalledTimes(1);
});

it.each(["artifactId", "workflowExecutionId", "definitionVersionId"] as const)("rejects a foreign %s and waits for explicit retry", async field => {
  api.run.mockResolvedValue({ ...view, [field]: "foreign" }); render(); await settle();
  expect(current.status).toBe("unavailable"); expect(current.error).toContain("identity");
  expect(current.details).toBeNull(); expect(api.instance).not.toHaveBeenCalled();
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(1);
});

it("stops on unavailable capabilities and reports expired runs without reading runtime values", async () => {
  api.run.mockRejectedValue(new ApiCapabilityUnavailableError(capabilityIds.publishing, "workflow-test-run-status"));
  render(); await settle(); expect(current.status).toBe("unavailable");
  await vi.advanceTimersByTimeAsync(10000); expect(api.run).toHaveBeenCalledTimes(1);
  api.run.mockResolvedValue({ ...view, expiresAt: new Date(Date.now() - 1000).toISOString() });
  flushSync(() => current.refresh()); await settle();
  expect(current.status).toBe("expired"); expect(current.waits).toEqual([]); expect(api.instance).not.toHaveBeenCalled();
});

it("backs off disconnected reads and does not overlap a slow reconciliation", async () => {
  api.run.mockRejectedValueOnce(new Error("connection dropped")); render(); await settle();
  expect(current.status).toBe("disconnected"); await vi.advanceTimersByTimeAsync(3999);
  expect(api.run).toHaveBeenCalledTimes(1);
  let release!: (value: WorkflowTestRunView) => void;
  api.run.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  await vi.advanceTimersByTimeAsync(1); expect(api.run).toHaveBeenCalledTimes(2);
  await vi.advanceTimersByTimeAsync(20000); expect(api.run).toHaveBeenCalledTimes(2);
  release(view); await settle(); expect(current.status).toBe("connected");
});

it("does not let a manual refresh's superseded waits restore older data", async () => {
  let release!: (value: { waits: unknown[] }) => void;
  api.waits.mockImplementationOnce(() => new Promise(resolve => { release = resolve; }));
  render(); await settle();
  api.waits.mockResolvedValue({ waits: [] }); flushSync(() => current.refresh()); await settle();
  release({ waits: [{ waitId: "obsolete" }] }); await settle();
  expect(current.waits).toEqual([]);
});

it.each(["resolve", "reject"] as const)("discards an older waits %s after a failed runtime read and successful retry", async outcome => {
  let resolveOld!: (value: { waits: unknown[] }) => void;
  let rejectOld!: (error: Error) => void;
  api.waits.mockImplementationOnce(() => new Promise((resolve, reject) => { resolveOld = resolve; rejectOld = reject; }));
  api.instance.mockRejectedValueOnce(new Error("runtime temporarily unavailable"));
  render(); await settle(); expect(current.status).toBe("disconnected");
  expect(api.waits.mock.calls[0][2].aborted).toBe(true);
  const confirmed = { waitId: "current-wait", nodeId: "node", label: "Continue", isAvailable: true, modes: [] };
  api.waits.mockResolvedValue({ waits: [confirmed] });
  await vi.advanceTimersByTimeAsync(4000); await settle();
  expect(current.status).toBe("connected"); expect(current.waits).toEqual([confirmed]);
  if (outcome === "resolve") resolveOld({ waits: [{ waitId: "obsolete-wait" }] });
  else rejectOld(new Error("obsolete wait read failed"));
  await settle();
  expect(current.waits).toEqual([confirmed]); expect(current.waitsError).toBe("");
});

it.each(["disconnected", "unavailable", "expired"] as const)("keeps a manual refresh's %s state when following is paused", async expected => {
  render(); await settle(); expect(current.revision).toBeGreaterThan(0);
  flushSync(() => current.setFollowing(false)); expect(current.status).toBe("paused");
  if (expected === "expired") api.run.mockResolvedValue({ ...view, status: "Expired" });
  else api.run.mockRejectedValue(expected === "unavailable"
    ? new ApiCapabilityUnavailableError(capabilityIds.publishing, "workflow-test-run-status") : new Error("server unavailable"));
  flushSync(() => current.refresh()); await settle();
  expect(current.status).toBe(expected); expect(current.details).toBeNull(); expect(current.waits).toEqual([]);
  // The command surface accepts connected or explicitly paused confirmed observation only.
  expect(current.status === "connected" || current.status === "paused" && !current.following && current.revision > 0).toBe(false);
});
