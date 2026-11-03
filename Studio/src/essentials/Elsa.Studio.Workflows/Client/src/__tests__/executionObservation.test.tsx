import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import type { StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import type { WorkflowInstanceDetails } from "../workflowTypes";
import { isTerminalExecution, readObservedExecution, useExecutionObservation } from "../workflow-editor/useExecutionObservation";
const api = vi.hoisted(() => ({ read: vi.fn() }));
vi.mock("../api/runtime", () => ({ getWorkflowInstance: api.read }));
const context = { baseUrl: "https://runtime.example" } as StudioEndpointContext;
const details = (status = "Running", artifactId = "artifact") => ({ instance: { workflowExecutionId: "run", artifactId, status }, activities: [] }) as unknown as WorkflowInstanceDetails;
let root: Root;
let container: HTMLDivElement;
let observed: ReturnType<typeof useExecutionObservation<WorkflowInstanceDetails>>;
let reader: Parameters<typeof useExecutionObservation<WorkflowInstanceDetails>>[0]["read"];
function Probe() { observed = useExecutionObservation({ context, identity: "direct:run", read: reader }); return null; }
function render() { flushSync(() => root.render(<Probe />)); }
async function settle() { for (let i = 0; i < 20; i++) await Promise.resolve(); render(); }
beforeEach(() => {
  vi.useFakeTimers(); vi.resetAllMocks();
  vi.spyOn(document, "hidden", "get").mockReturnValue(false); vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  api.read.mockResolvedValue(details());
  reader = async scope => { const value = await readObservedExecution(context, "run", "artifact", scope.signal); return { value, terminal: isTerminalExecution(value.instance.status) }; };
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });
it("follows an already running instance and stops polling only after confirmed completion", async () => {
  render(); await settle(); expect(observed.value?.instance.status).toBe("Running");
  expect(api.read).toHaveBeenLastCalledWith(context, "run", expect.any(AbortSignal));
  api.read.mockResolvedValue(details("Completed")); await vi.advanceTimersByTimeAsync(2000); await settle();
  expect(observed.value?.instance.status).toBe("Completed"); expect(api.read).toHaveBeenCalledTimes(2);
  await vi.advanceTimersByTimeAsync(10000); expect(api.read).toHaveBeenCalledTimes(2);
});
it("rejects another artifact instead of presenting it as the pinned execution", async () => {
  api.read.mockResolvedValue(details("Completed", "foreign")); render(); await settle();
  expect(observed.status).toBe("unavailable"); expect(observed.value).toBeNull();
  await vi.advanceTimersByTimeAsync(10000); expect(api.read).toHaveBeenCalledTimes(1);
});
it("disables interaction when a manually paused view becomes hidden", async () => {
  render(); await settle(); flushSync(() => observed.setFollowing(false)); expect(observed.canInteract).toBe(true);
  vi.spyOn(document, "hidden", "get").mockReturnValue(true); flushSync(() => document.dispatchEvent(new Event("visibilitychange")));
  expect(observed.status).toBe("paused"); expect(observed.value?.instance.status).toBe("Running"); expect(observed.canInteract).toBe(false);
});
it("publishes confirmed runtime state while ancillary evidence is pending, without overlapping reads", async () => {
  let release!: () => void;
  const pending = new Promise<void>(resolve => { release = resolve; });
  const reads = vi.fn();
  reader = async scope => { reads(); scope.publish(() => details()); await pending; return { value: details(), terminal: false }; };
  render(); await settle(); expect(observed.value?.instance.status).toBe("Running"); expect(observed.revision).toBe(1);
  await vi.advanceTimersByTimeAsync(10000); expect(reads).toHaveBeenCalledTimes(1);
  release(); await settle(); expect(observed.revision).toBe(1);
  await vi.advanceTimersByTimeAsync(2000); expect(reads).toHaveBeenCalledTimes(2);
});
