import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, expect, it, vi } from "vitest";
import { authSessionEndedEvent, authSessionStartedEvent, type StudioEndpointContext } from "@elsa-workflows/studio-sdk";
import { useHistoryResource } from "../workflow-editor/run-history/useHistoryResource";

let root: Root, container: HTMLDivElement;
let context: StudioEndpointContext;
let identity: string, refresh: number;
let observed: ReturnType<typeof useHistoryResource<{ id: string }>>;
const read = vi.fn<(signal: AbortSignal) => Promise<{ id: string }>>();
function Probe() { observed = useHistoryResource(context, identity, read, true, true, refresh); return null; }
function render() { flushSync(() => root.render(<Probe />)); }
async function settle() { for (let count = 0; count < 30; count++) await Promise.resolve(); render(); }

beforeEach(() => {
  vi.useFakeTimers(); read.mockReset(); read.mockResolvedValue({ id: "original" });
  context = { baseUrl: "https://history.example" } as StudioEndpointContext; identity = "target"; refresh = 0;
  vi.spyOn(document, "hidden", "get").mockReturnValue(false); vi.spyOn(navigator, "onLine", "get").mockReturnValue(true);
  container = document.createElement("div"); document.body.append(container); root = createRoot(container);
});
afterEach(() => { flushSync(() => root.unmount()); container.remove(); vi.restoreAllMocks(); vi.useRealTimers(); });

it.each([400, 401, 403, 404, 409, 501])("clears an inaccessible or invalid exact detail and stops automatic retries (%s)", async status => {
  render(); await settle();
  expect(observed.value?.id).toBe("original");
  read.mockRejectedValueOnce(Object.assign(new Error("private provider detail"), { status }));
  await vi.advanceTimersByTimeAsync(5000); await settle();
  expect(observed.value).toBeNull(); expect(observed.status).toBe("unavailable");
  expect(observed.error).not.toContain("private provider detail");
  await vi.advanceTimersByTimeAsync(10000); expect(read).toHaveBeenCalledTimes(2);
});
it("retains confirmed data after temporary unavailability and retries serially", async () => {
  render(); await settle(); read.mockRejectedValueOnce(Object.assign(new Error("temporary"), { status: 503 }));
  await vi.advanceTimersByTimeAsync(5000); await settle();
  expect(observed.value?.id).toBe("original"); expect(observed.status).toBe("stale");
  read.mockResolvedValue({ id: "replacement" }); await vi.advanceTimersByTimeAsync(5000); await settle();
  expect(observed.value?.id).toBe("replacement"); expect(observed.status).toBe("ready");
});
it("aborts obsolete target/context reads and cannot publish their later responses", async () => {
  let complete!: (value: { id: string }) => void;
  read.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(); await settle(); const signal = read.mock.calls[0][0];
  identity = "other-target"; read.mockResolvedValue({ id: "other-target" }); render(); await settle();
  expect(signal.aborted).toBe(true); expect(observed.value?.id).toBe("other-target");
  complete({ id: "obsolete" }); await settle(); expect(observed.value?.id).toBe("other-target");
  context = { baseUrl: "https://other.example" } as StudioEndpointContext;
  read.mockImplementationOnce(() => new Promise(() => {})); render(); await settle();
  expect(observed.value).toBeNull();
});
it("drops protected data on sign-out and requires a fresh read after sign-in", async () => {
  render(); await settle(); flushSync(() => window.dispatchEvent(new Event(authSessionEndedEvent))); await settle();
  expect(observed.value).toBeNull(); await vi.advanceTimersByTimeAsync(10000); expect(read).toHaveBeenCalledTimes(1);
  let complete!: (value: { id: string }) => void;
  read.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  flushSync(() => window.dispatchEvent(new Event(authSessionStartedEvent))); await settle();
  expect(observed.value).toBeNull(); complete({ id: "fresh-session" }); await settle();
  expect(observed.value?.id).toBe("fresh-session");
});
it("coalesces refresh requests during one in-flight exact read without concurrency", async () => {
  let complete!: (value: { id: string }) => void;
  read.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(); await settle(); refresh++; render(); await settle(); refresh++; render(); await settle();
  expect(read).toHaveBeenCalledTimes(1);
  complete({ id: "first" }); await settle(); expect(read).toHaveBeenCalledTimes(2);
  expect(observed.value?.id).toBe("original");
});
it("aborts while hidden and refreshes the same target when visible again", async () => {
  let complete!: (value: { id: string }) => void;
  read.mockImplementationOnce(() => new Promise(resolve => { complete = resolve; }));
  render(); await settle(); const signal = read.mock.calls[0][0];
  vi.spyOn(document, "hidden", "get").mockReturnValue(true); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  expect(signal.aborted).toBe(true); expect(observed.loading).toBe(false);
  complete({ id: "hidden-response" }); await settle(); expect(observed.value).toBeNull();
  vi.spyOn(document, "hidden", "get").mockReturnValue(false); flushSync(() => document.dispatchEvent(new Event("visibilitychange"))); await settle();
  expect(read).toHaveBeenCalledTimes(2); expect(observed.value?.id).toBe("original");
});
