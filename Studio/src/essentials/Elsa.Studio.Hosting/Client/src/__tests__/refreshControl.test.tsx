import React from "react";
import { flushSync } from "react-dom";
import { createRoot, type Root } from "react-dom/client";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { StudioRefreshControl, type StudioRefreshContribution } from "../app/ui/layout/RefreshControl";

// The host configuration uses production React, which does not export act.
const realSetTimeout = globalThis.setTimeout;
let container: HTMLDivElement;
let root: Root;
let refresh: StudioRefreshContribution;
let hidden: boolean;
let unmounted: boolean;

beforeEach(() => {
  vi.useFakeTimers();
  hidden = false;
  unmounted = false;
  vi.spyOn(document, "hidden", "get").mockImplementation(() => hidden);
  vi.spyOn(HTMLElement.prototype, "getBoundingClientRect").mockReturnValue({
    x: 20, y: 20, left: 20, top: 20, right: 220, bottom: 60, width: 200, height: 40,
    toJSON: () => ({})
  });
  container = document.createElement("div");
  document.body.appendChild(container);
  root = createRoot(container);
  refresh = { id: "executables", label: "Refresh executables", onRefresh: vi.fn() };
});

afterEach(() => {
  if (!unmounted) flushSync(() => root.unmount());
  container.remove();
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
  vi.useRealTimers();
});

function render(changes: Partial<StudioRefreshContribution> = {}) {
  refresh = { ...refresh, ...changes };
  flushSync(() => root.render(<StudioRefreshControl key={refresh.id} refresh={refresh} />));
}

function main() {
  return container.querySelector<HTMLButtonElement>(".studio-refresh-main")!;
}

function trigger() {
  return container.querySelector<HTMLButtonElement>(".studio-refresh-trigger")!;
}

function items() {
  return Array.from(document.querySelectorAll<HTMLButtonElement>('[role="menuitemradio"]'));
}

function click(element: HTMLElement) {
  flushSync(() => element.click());
}

function key(element: HTMLElement, value: string) {
  flushSync(() => element.dispatchEvent(new KeyboardEvent("keydown", { key: value, bubbles: true, cancelable: true })));
}

function select(label: string) {
  click(trigger());
  const item = items().find(option => option.textContent?.endsWith(label));
  expect(item).toBeDefined();
  click(item!);
}

async function advance(ms: number) {
  await vi.advanceTimersByTimeAsync(ms);
  await settle();
}

async function settle() {
  await new Promise<void>(resolve => realSetTimeout(resolve, 0));
}

function deferred() {
  let resolve!: () => void;
  let reject!: (reason?: unknown) => void;
  const promise = new Promise<void>((success, failure) => { resolve = success; reject = failure; });
  return { promise, resolve, reject };
}

function visibility(value: boolean) {
  hidden = value;
  flushSync(() => document.dispatchEvent(new Event("visibilitychange")));
}

describe("StudioRefreshControl", () => {
  it("is manual by default, preserves exact resting text, forwards the ref and exposes its stable slot", async () => {
    const buttonRef = React.createRef<HTMLButtonElement>();
    render({ buttonRef });
    expect(container.firstElementChild?.getAttribute("data-studio-slot")).toBe("studio.page-header.refresh");
    expect(main().textContent).toBe("Refresh");
    expect(main().getAttribute("aria-label")).toBe("Refresh executables");
    expect(buttonRef.current).toBe(main());
    expect(trigger()).toBeNull();
    await advance(600_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    click(main());
    await settle();
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    expect(main().textContent).toBe("Refresh");
  });

  it("requires explicit auto opt-in and still starts in manual mode", async () => {
    render({ autoRefresh: false });
    expect(trigger()).toBeNull();
    render({ autoRefresh: true });
    expect(trigger().getAttribute("aria-haspopup")).toBe("menu");
    await advance(600_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    click(trigger());
    expect(items().map(item => item.getAttribute("aria-checked"))).toEqual(["true", "false", "false", "false", "false"]);
  });

  it("waits a full selected interval and continues polling after synchronous completion", async () => {
    render({ autoRefresh: true });
    select("Every 15 seconds");
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    expect(main().textContent).toBe("Refresh");
    expect(document.getElementById(main().getAttribute("aria-describedby")!)?.textContent).toContain("Every 15 seconds");
    await advance(14_999);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    await advance(15_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(2);
    await advance(15_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(3);
  });

  it("resets the countdown on interval changes and stops when manual is selected", async () => {
    render({ autoRefresh: true });
    select("Every 15 seconds");
    await advance(10_000);
    select("Every 30 seconds");
    await advance(29_999);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    select("Manual");
    expect(main().textContent).toBe("Refresh");
    await advance(600_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
  });

  it.each([["Every minute", 60_000], ["Every 5 minutes", 300_000]] as const)("supports %s", async (label, delay) => {
    render({ autoRefresh: true });
    select(label);
    await advance(delay - 1);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
  });

  it("uses the latest callback without restarting the countdown for inline prop identities", async () => {
    const first = vi.fn();
    const latest = vi.fn();
    render({ autoRefresh: true, onRefresh: first });
    select("Every 15 seconds");
    await advance(10_000);
    render({ onRefresh: () => latest() });
    await advance(4_999);
    expect(latest).not.toHaveBeenCalled();
    await advance(1);
    expect(first).not.toHaveBeenCalled();
    expect(latest).toHaveBeenCalledTimes(1);
  });

  it("guards same-tick double clicks and waits a full interval after an internal request completes", async () => {
    const request = deferred();
    const onRefresh = vi.fn(() => request.promise);
    render({ autoRefresh: true, onRefresh });
    select("Every 15 seconds");
    flushSync(() => {
      main().click();
      main().click();
    });
    expect(onRefresh).toHaveBeenCalledTimes(1);
    expect(main().disabled).toBe(true);
    expect(main().textContent).toContain("Refreshing…");
    expect(main().getAttribute("aria-busy")).toBe("true");
    await advance(60_000);
    expect(onRefresh).toHaveBeenCalledTimes(1);
    request.resolve();
    await settle();
    await advance(14_999);
    expect(onRefresh).toHaveBeenCalledTimes(1);
    await advance(1);
    expect(onRefresh).toHaveBeenCalledTimes(2);
  });

  it("never overlaps externally owned refreshing and resumes after it clears", async () => {
    render({ autoRefresh: true });
    select("Every 15 seconds");
    await advance(10_000);
    render({ refreshing: true });
    expect(main().disabled).toBe(true);
    click(main());
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    render({ refreshing: false });
    await advance(14_999);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
  });

  it("allows switching to manual while an automatic request is pending", async () => {
    const request = deferred();
    render({ autoRefresh: true, onRefresh: vi.fn(() => request.promise) });
    select("Every 15 seconds");
    await advance(15_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    expect(trigger().disabled).toBe(false);
    select("Manual");
    request.resolve();
    await settle();
    await advance(600_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    expect(main().disabled).toBe(false);
  });

  it("waits for both a slow automatic callback and external refreshing before scheduling again", async () => {
    const request = deferred();
    render({ autoRefresh: true, onRefresh: vi.fn(() => request.promise) });
    select("Every 15 seconds");
    await advance(15_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    render({ refreshing: true });
    await advance(60_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    request.resolve();
    await settle();
    await advance(60_000);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    expect(main().disabled).toBe(true);
    render({ refreshing: false });
    await advance(14_999);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(2);
  });

  it("pauses in a hidden document and resumes with a fresh countdown", async () => {
    render({ autoRefresh: true });
    select("Every 15 seconds");
    await advance(10_000);
    visibility(true);
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    visibility(false);
    await advance(14_999);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
  });

  it.each(["active", "disabled"] as const)("pauses for %s and resumes with a fresh countdown", async field => {
    render({ autoRefresh: true, active: true });
    select("Every 15 seconds");
    await advance(10_000);
    render({ [field]: field === "disabled" });
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    if (field === "disabled") {
      expect(main().disabled).toBe(true);
      expect(trigger().disabled).toBe(true);
    }
    render({ [field]: field === "active" });
    await advance(14_999);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    await advance(1);
    expect(refresh.onRefresh).toHaveBeenCalledTimes(1);
  });

  it("stops polling when opt-in is removed and does not restore an old interval", async () => {
    render({ autoRefresh: true });
    select("Every 15 seconds");
    render({ autoRefresh: false });
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    render({ autoRefresh: true });
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
    click(trigger());
    expect(items()[0].getAttribute("aria-checked")).toBe("true");
  });

  it("cleans up timeouts and visibility listeners on unmount", async () => {
    const removeListener = vi.spyOn(document, "removeEventListener");
    render({ autoRefresh: true });
    select("Every 15 seconds");
    await advance(0);
    expect(vi.getTimerCount()).toBe(1);
    flushSync(() => root.unmount());
    unmounted = true;
    expect(vi.getTimerCount()).toBe(0);
    expect(removeListener).toHaveBeenCalledWith("visibilitychange", expect.any(Function));
    visibility(true);
    visibility(false);
    await advance(60_000);
    expect(refresh.onRefresh).not.toHaveBeenCalled();
  });

  it("resets interval identity on a keyed remount and ignores the old pending completion", async () => {
    const request = deferred();
    const oldRefresh = vi.fn(() => request.promise);
    const nextRefresh = vi.fn();
    render({ autoRefresh: true, onRefresh: oldRefresh });
    select("Every 15 seconds");
    await advance(15_000);
    render({ id: "another-context", onRefresh: nextRefresh });
    request.reject(new Error("Old context failed"));
    await settle();
    expect(container.querySelector('[role="alert"]')).toBeNull();
    expect(main().disabled).toBe(false);
    await advance(60_000);
    expect(nextRefresh).not.toHaveBeenCalled();
    select("Every 15 seconds");
    await advance(15_000);
    expect(nextRefresh).toHaveBeenCalledTimes(1);
  });

  it.each(["throw", "reject"] as const)("announces a callback %s, stops auto and recovers only when re-enabled", async failure => {
    const onRefresh = vi.fn<() => void | Promise<void>>()
      .mockImplementationOnce(() => {
        if (failure === "throw") throw new Error("Unavailable");
        return Promise.reject(new Error("Unavailable"));
      })
      .mockImplementation(() => undefined);
    render({ autoRefresh: true, onRefresh });
    select("Every 15 seconds");
    await advance(15_000);
    const alert = container.querySelector('[role="alert"]');
    expect(alert?.textContent).toContain("Refresh failed");
    expect(main().getAttribute("aria-describedby")).toContain(alert?.id);
    expect(main().disabled).toBe(false);
    await advance(60_000);
    expect(onRefresh).toHaveBeenCalledTimes(1);
    click(main());
    await settle();
    expect(onRefresh).toHaveBeenCalledTimes(2);
    expect(container.querySelector('[role="alert"]')).toBeNull();
    await advance(60_000);
    expect(onRefresh).toHaveBeenCalledTimes(2);
    select("Every 15 seconds");
    await advance(15_000);
    expect(onRefresh).toHaveBeenCalledTimes(3);
  });

  it("catches a rejection after unmount without leaving timers or state updates", async () => {
    const request = deferred();
    const consoleError = vi.spyOn(console, "error");
    render({ autoRefresh: true, onRefresh: () => request.promise });
    click(main());
    flushSync(() => root.unmount());
    unmounted = true;
    request.reject(new Error("Late failure"));
    await settle();
    expect(consoleError).not.toHaveBeenCalled();
    expect(vi.getTimerCount()).toBe(0);
  });

  it("provides radio semantics, arrow/Home/End navigation, Escape and focus return", () => {
    render({ autoRefresh: true });
    trigger().focus();
    key(trigger(), "ArrowDown");
    expect(trigger().getAttribute("aria-expanded")).toBe("true");
    const menu = document.querySelector<HTMLElement>('[role="menu"]')!;
    expect(trigger().getAttribute("aria-controls")).toBe(menu.id);
    expect(items()).toHaveLength(5);
    expect(document.activeElement).toBe(items()[0]);
    key(items()[0], "ArrowDown");
    expect(document.activeElement).toBe(items()[1]);
    key(items()[1], "End");
    expect(document.activeElement).toBe(items()[4]);
    key(items()[4], "ArrowDown");
    expect(document.activeElement).toBe(items()[0]);
    key(items()[0], "ArrowUp");
    expect(document.activeElement).toBe(items()[4]);
    key(items()[4], "Home");
    expect(document.activeElement).toBe(items()[0]);
    key(items()[0], "Escape");
    expect(document.querySelector('[role="menu"]')).toBeNull();
    expect(document.activeElement).toBe(trigger());
    key(trigger(), "ArrowUp");
    expect(document.activeElement).toBe(items()[4]);
    click(items()[2]);
    expect(document.activeElement).toBe(trigger());
    click(trigger());
    expect(document.activeElement).toBe(items()[2]);
    expect(items().map(item => item.getAttribute("aria-checked"))).toEqual(["false", "false", "true", "false", "false"]);
    key(items()[2], "Tab");
    expect(document.querySelector('[role="menu"]')).toBeNull();
  });
});